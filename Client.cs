using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Security;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Collections.Concurrent;
using LinkHTTP;

namespace HTTPServer
{
    // Класс-обработчик клиента
    class Client
    {
        public static List<string> getData;
        public static List<string> tempList = new List<string>();

        public static ConcurrentDictionary<string, int> lockedList = new ConcurrentDictionary<string, int>();

        public static ConcurrentDictionary<string, DateTime> ParamData = new ConcurrentDictionary<string, DateTime>(); // ФНС
        public static ConcurrentDictionary<string, DateTime> ParamPfr = new ConcurrentDictionary<string, DateTime>(); // ПФР
        public static ConcurrentDictionary<string, DateTime> ParamStat = new ConcurrentDictionary<string, DateTime>(); // РОССТАТ

        public static string clientIPAddress, ResponseStr, guid, getDataHash, getDataINN;

        private void SendError(TcpClient TcpClient, int Code)
        {
            // Получаем строку вида "200 OK"
            // HttpStatusCode хранит в себе все статус-коды HTTP/1.1
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            // Код простой HTML-странички
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
            string Str = "HTTP/1.1 " + CodeStr + "\nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            // Приведем строку к виду массива байт
            byte[] Buffer = Encoding.UTF8.GetBytes(Str);
            // Отправим его клиенту
            TcpClient.GetStream().Write(Buffer, 0, Buffer.Length);
            // Закроем соединение
            TcpClient.Close();
        }

        public static void ErrorLog(Exception ex)
        {
            System.IO.File.AppendAllText("error_" + DateTime.Now.ToString("d") + ".log", "[" + DateTime.Now.ToLongTimeString() + "] \r\n " + ex.ToString() + Environment.NewLine + "-----------------------------" + Environment.NewLine);
        }

        // Отправка страницы с результатом запроса
        private void SendResponse(TcpClient TcpClient, int Code, string param)
        {
            try
            {
                string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
                byte[] Buffer = Encoding.UTF8.GetBytes(param);
                TcpClient.GetStream().Write(Buffer, 0, Buffer.Length);
                TcpClient.Close();
            }
            catch (Exception ex)
            {
                ErrorLog(ex);
            }
        }

        /* ***  Добавление настроечных файлов в коллекции *** */
        public void ParamAdd(string str, DateTime dt)
        {
            string readfile = File.ReadAllText(str, Encoding.Default);

            if (readfile.IndexOf(getData[0], StringComparison.OrdinalIgnoreCase) != -1)
            {
                if (str.Contains(@LinkHTTP.Properties.Settings.Default.EnterprisePatch))	
					ParamData.TryAdd(str, dt);
				
                if (str.Contains(@LinkHTTP.Properties.Settings.Default.PfrPatch))
					ParamPfr.TryAdd(str, dt);
				
                if (str.Contains(@LinkHTTP.Properties.Settings.Default.StatPatch))
					ParamStat.TryAdd(str, dt);
            }
        }
        /*                       Конец                         */


        /* ***   Конструктор класса. Ему нужно передавать принятого клиента от TcpListener *** */
        public Client(TcpClient TcpClient)
        {
            // Получаем IP адрес клиента.
            clientIPAddress = Convert.ToString(IPAddress.Parse(((IPEndPoint)TcpClient.Client.RemoteEndPoint).Address.ToString()));

            // Объявим строку, в которой будет хранится запрос клиента
            string Request = "";
            // Буфер для хранения принятых от клиента данных
            byte[] Buffer = new byte[1024];
            // Переменная для хранения количества байт, принятых от клиента
            int Count;
            // Читаем из потока клиента до тех пор, пока от него поступают данные
            while ((Count = TcpClient.GetStream().Read(Buffer, 0, Buffer.Length)) > 0)
            {
                try
                {
                    // Преобразуем эти данные в строку и добавим ее к переменной Request
                    Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                    // Запрос должен обрываться последовательностью \r\n\r\n
                    // Либо обрываем прием данных сами, если длина строки Request превышает 4 килобайта
                    // Нам не нужно получать данные из POST-запроса (и т. п.), а обычный запрос
                    // по идее не должен быть больше 4 килобайт
                    if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096)
                        break;
                }
                catch (Exception ex)
                {
                    continue;
                }
            }

            // Парсим строку запроса с использованием регулярных выражений
            // При этом отсекаем все переменные GET-запроса
            Match ReqMatch = Regex.Match(Request, @"^\w+\s+([^\s\?]+)[^\s]*\s+HTTP/.*|");

            // Если запрос не удался
            if (ReqMatch == Match.Empty)
            {
                // Передаем клиенту ошибку 400 - неверный запрос
                SendError(TcpClient, 400);
                return;
            }

            // Получаем строку запроса
            string RequestUri = ReqMatch.Groups[1].Value;

            // Приводим ее к изначальному виду, преобразуя экранированные символы
            // Например, "%20" -> " "
            RequestUri = Uri.UnescapeDataString(RequestUri);

            // Если в строке содержится двоеточие, передадим ошибку 400
            // Это нужно для защиты от URL типа http://example.com/../../file.txt
            if (RequestUri.IndexOf("..") >= 0)
            {
                SendError(TcpClient, 400);
                return;
            }

            // Если строка запроса оканчивается на "/", то добавим к ней index.html
            if (RequestUri.EndsWith("/"))
                RequestUri += "index.html";

            string FilePath = "www/" + RequestUri;

            // Если в папке www не существует данного файла, посылаем ошибку 404
            if (!File.Exists(FilePath))
            {
                SendError(TcpClient, 404);
                return;
            }

            // Получаем расширение файла из строки запроса
            string Extension = RequestUri.Substring(RequestUri.LastIndexOf('.'));

            // Тип содержимого
            string ContentType = "";

            // Пытаемся определить тип содержимого по расширению файла
            switch (Extension)
            {
                case ".htm":
                case ".html":
                    ContentType = "text/html";
                    break;
                case ".xml":
                    ContentType = "application/octet-stream";
                    break;
                default:
                    if (Extension.Length > 1)
                    {
                        ContentType = "application/" + Extension.Substring(1);
                    }
                    else
                    {
                        ContentType = "application/unknown";
                    }
                    break;
            }

            // Открываем файл, страхуясь на случай ошибки
            FileStream FS;
            try
            {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception)
            {
                // Если случилась ошибка, посылаем клиенту ошибку 500
                SendError(TcpClient, 500);
                return;
            }

            if (LinkHTTP.Properties.Settings.Default.Secure == true)
            {
                if (lockedList.Count > 0)
                {
                    foreach (KeyValuePair<string, int> str in lockedList) // ищем
                    {
                        if (str.Key == clientIPAddress) // если нашли
                        {
                            lockedList.TryUpdate(clientIPAddress, str.Value + 1, str.Value);

                            if (str.Value >= 15)
                            {
                                TcpClient.Close();
                                return;
                            }

                            Server.ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] WARNING !!! " + str.Key + " bad get query: #" + str.Value.ToString(), 17, 1);
                        }
                        else
                            lockedList.TryAdd(clientIPAddress, 1);
                    }
                }
                else
                {
                    lockedList.TryAdd(clientIPAddress, 1);
                }
            }

            try
            {
                // Посылаем заголовки
                string Headers = "HTTP/1.1 200 OK\nContent-Type: " + ContentType + "\nContent-Length: " + FS.Length + " \n\n";
                byte[] HeadersBuffer = Encoding.UTF8.GetBytes(Headers);
                TcpClient.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);

                getData = new List<string>();

                string[] split = Request.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int getRequest = split[1].Split('=').Length; // 3 знака равенства значит 3 id

                string getDataType = null;

                // В запросе либо 3 параметра либо 2 (1 из параметров постоянный это сама строка.)
                if (getRequest == 3 || getRequest == 4)
                {
                    foreach (string p in Request.Split(new Char[] { ' ', '?' })[2].Split(new Char[] { '&' }))
                        getData.Add(p.Split(new Char[] { '=' })[1]);

                    getDataHash = getData[0]; // Содержит отпечаток сертификата
                    getDataINN = getData[1]; // Содержит ИНН

                    if (getRequest > 3)
                        getDataType = getData[2]; // Содержит тип (fns, pfr, stat)

                    // Очищаем перед использованием
                    if (!ParamData.IsEmpty) ParamData.Clear();
                    if (!ParamPfr.IsEmpty) ParamPfr.Clear();
                    if (!ParamStat.IsEmpty) ParamStat.Clear();

                    // Очистка консоли
                    Functions.StrClean(10, 1);
                    Functions.StrClean(11, 1);
                    Functions.StrClean(12, 1);
                    Functions.StrClean(13, 1);

                    Server.ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] Создан запрос от: (" + clientIPAddress + ") для: (" + getDataINN + ") \n", 9, 1);

                    foreach (KeyValuePair<string, DateTime> file in Server.FileList)
                        if (file.Key.Contains(getDataINN)) // если в имени файла в коллекции FileList содержится наш инн
                            ParamAdd(file.Key, file.Value); // добавим настроечный файл в соответствующую коллецкцию

                    if (ParamStat.Count != 0 || ParamData.Count != 0 || ParamPfr.Count != 0) // если в коллекциях есть хоть один настроечный файл то
                    {
                        guid = Guid.NewGuid().ToString().Replace("-", "").Trim(); // формируем уникальное имя

                        if (!Directory.Exists(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash))
                            Directory.CreateDirectory(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash);

                        if (getDataType == "fns" || String.IsNullOrEmpty(getDataType))
                        {
                            if (ParamData.Count > 0)
                            {
                                var ParamDataMax = ParamData.OrderByDescending(z => z.Value).ToDictionary(a => a, s => s).First().Value;
                                Server.ConsoleWrite("ФНС: " + Path.GetFileName(ParamDataMax.Key), 11, 1);

                                string tmp = string.Format("*{0}*", LinkHTTP.Properties.Settings.Default.RateName);

                                foreach (string file in Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml", SearchOption.AllDirectories))
                                {
                                    FileInfo fileInf = new FileInfo(file);
                                    if (File.GetLastWriteTime(fileInf.FullName) != ParamDataMax.Value)
                                    {
                                        File.Delete(fileInf.FullName);
                                        tempList.Add(fileInf.FullName);
                                    }
                                    else
                                        ResponseStr += Path.GetFileName(file) + Environment.NewLine;
                                }

                                if (tempList.Count != 0 || Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml").Length == 0)
                                {
                                    if (File.Exists(ParamDataMax.Key))
                                    {
                                        File.Copy(ParamDataMax.Key, LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash + "\\" + LinkHTTP.Properties.Settings.Default.RateName + guid + ".xml");
                                        ResponseStr += LinkHTTP.Properties.Settings.Default.RateName + guid + ".xml" + Environment.NewLine;
                                        tempList.Clear();
                                    }
                                    else
                                    {
                                        Server.ConsoleWrite("Произошла ошибка, файл (" + ParamDataMax.Key + ") перемещён или удалён.", 11, 1);
                                        ResponseStr += "NOT FOUND: (" + ParamDataMax.Key + ")";
                                    }
                                }
                            }
                        }
                        else
                            Server.ConsoleWrite("Произошла ошибка в запросе от (" + clientIPAddress + ")", 11, 1);

                        if (getDataType == "pfr" || String.IsNullOrEmpty(getDataType))
                        {
                            if (ParamPfr.Count > 0)
                            {
                                var ParamPfrMax = ParamPfr.OrderByDescending(z => z.Value).ToDictionary(a => a, s => s).First().Value;
                                Server.ConsoleWrite("ПФР: " + Path.GetFileName(ParamPfrMax.Key), 12, 1);

                                string tmp = string.Format("*{0}*", LinkHTTP.Properties.Settings.Default.PfrName);

                                foreach (string file in Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml", SearchOption.AllDirectories))
                                {
                                    FileInfo fileInf = new FileInfo(file);
                                    if (File.GetLastWriteTime(fileInf.FullName) != ParamPfrMax.Value)
                                    {
                                        File.Delete(fileInf.FullName);
                                        tempList.Add(fileInf.FullName);
                                    }
                                    else
                                        ResponseStr += Path.GetFileName(file) + Environment.NewLine;
                                }

                                if (tempList.Count != 0 || Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml").Length == 0)
                                {
                                    if (File.Exists(ParamPfrMax.Key))
                                    {
                                        File.Copy(ParamPfrMax.Key, LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash + "\\" + LinkHTTP.Properties.Settings.Default.PfrName + guid + ".xml");

                                        ResponseStr += LinkHTTP.Properties.Settings.Default.PfrName + guid + ".xml" + Environment.NewLine;
                                        tempList.Clear();
                                    }
                                    else
                                    {
                                        Server.ConsoleWrite("Произошла ошибка, файл (" + ParamPfrMax.Key + ") перемещён или удалён.", 11, 1);
                                        ResponseStr += "NOT FOUND: (" + ParamPfrMax.Key + ")";
                                    }
                                }
                            }
                        }
                        else
                            Server.ConsoleWrite("Произошла ошибка в запросе от (" + clientIPAddress + ")", 11, 1);

                        if (getDataType == "stat" || String.IsNullOrEmpty(getDataType))
                        {
                            if (ParamStat.Count > 0)
                            {
                                var ParamStatMax = ParamStat.OrderByDescending(z => z.Value).ToDictionary(a => a, s => s).First().Value;
                                Server.ConsoleWrite("РОССТАТ: " + Path.GetFileName(ParamStatMax.Key), 13, 1);

                                string tmp = string.Format("*{0}*", LinkHTTP.Properties.Settings.Default.StatName);

                                foreach (string file in Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml", SearchOption.AllDirectories))
                                {
                                    FileInfo fileInf = new FileInfo(file);
                                    if (File.GetLastWriteTime(fileInf.FullName) != ParamStatMax.Value)
                                    {
                                        File.Delete(fileInf.FullName);
                                        tempList.Add(fileInf.FullName);
                                    }
                                    else
                                        ResponseStr += Path.GetFileName(file) + Environment.NewLine;
                                }

                                if (tempList.Count != 0 || Directory.GetFiles(LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash, tmp + ".xml").Length == 0)
                                {
                                    if (File.Exists(ParamStatMax.Key))
                                    {
                                        File.Copy(ParamStatMax.Key, LinkHTTP.Properties.Settings.Default.wwwroot + "\\" + getDataHash + "\\" + LinkHTTP.Properties.Settings.Default.StatName + guid + ".xml");
                                        ResponseStr += LinkHTTP.Properties.Settings.Default.StatName + guid + ".xml" + Environment.NewLine;
                                        tempList.Clear();
                                    }
                                    else
                                    {
                                        Server.ConsoleWrite("Произошла ошибка, файл (" + ParamStatMax.Key + ") перемещён или удалён.", 11, 1);
                                        ResponseStr += "NOT FOUND: (" + ParamStatMax.Key + ")";
                                    }
                                }
                            }
                        }
                        else
                            Server.ConsoleWrite("Произошла ошибка в запросе от (" + clientIPAddress + ")", 11, 1);

                        SendResponse(TcpClient, 200, ResponseStr); // отправить запрос

                        // Очищаем переменные
                        ResponseStr = null;
                        getDataHash = null;
                        getDataINN = null;
                        guid = null;

                    }
                    else
                    {
                        // Если возникла ошибка передаём собщение на сервер и ответ клиенту.
                        Server.ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] Настроечные файлы для: (" + getDataINN + ") - не найдены.", 11, 1);
                        SendError(TcpClient, 404);
                    }
                }

                while (FS.Position < FS.Length)
                {
                    try
                    {
                        // Читаем данные из файла
                        Count = FS.Read(Buffer, 0, Buffer.Length);
                        // И передаем их клиенту
                        TcpClient.GetStream().Write(Buffer, 0, Count);
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                }

                // Закроем файл, соединение и очистим мусор
                FS.Close();
                TcpClient.Close();
                getData.Clear();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception ex)
            {
                //ErrorLog(ex);
            }


        }

    }
}

