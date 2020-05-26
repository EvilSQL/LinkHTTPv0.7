using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Collections.Concurrent;
using LinkHTTP;

namespace HTTPServer
{
    class Server
    {
        TcpListener Listener; // Объект, принимающий TCP-клиентов

        public static Stopwatch Swatch = new Stopwatch();
        public static ConcurrentDictionary<string, DateTime> FileList = new ConcurrentDictionary<string, DateTime>();

        public static int MaxThreadsCount = Environment.ProcessorCount * 32; // Максимальное количество потоков
        public static int MinThreadsCount = Environment.ProcessorCount * 16; // Минималное количество потоков

        public static int ServerPort = LinkHTTP.Properties.Settings.Default.HTTPPort;

        public static string tmp = string.Format("*{0}*", LinkHTTP.Properties.Settings.Default.DefaultParamName);

        public static string[] PatchList = { @LinkHTTP.Properties.Settings.Default.EnterprisePatch, @LinkHTTP.Properties.Settings.Default.PfrPatch, @LinkHTTP.Properties.Settings.Default.StatPatch };

        public static string elapsedTime;

        public static DateTime Date;

        public static Thread ThreadScan, ThreadTmp;
        public static TimeSpan TimeSpan;
        
        // индексируем всегда
        public static bool Scan = true;

        private static object ThreadLock = new object();

        // Запуск сервера
        public Server(int Port)
        {
			Listener = new TcpListener(IPAddress.Any, Port); // Создаем "слушателя" для указанного порта
			Listener.Start(); // Запускаем его
			
            // В бесконечном цикле
			while (true)
			{
				try
				{
					// Принимаем новых клиентов. После того, как клиент был принят, он передается в новый поток (ClientThread)
					// с использованием пула потоков.
					ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), Listener.AcceptTcpClient());
				}
				catch (System.IO.IOException ex)
				{
					//ErrorLog(ex);
                    continue; // Пропуск ошибки
				}
			}
			
        }

        static void ClientThread(Object StateInfo)
        {
            try
            {
                // Просто создаем новый экземпляр класса TcpClient и передаем ему приведенный к классу TcpClient объект StateInfo
                new Client((TcpClient)StateInfo);
            }
            catch (System.IO.IOException ex)
            {
                //ErrorLog(ex);
            }
        }

        // Остановка сервера
        ~Server()
        {
            // Если "слушатель" был создан
            if (Listener != null)
                Listener.Stop();
        }

        static void Main(string[] args)
        {
            // Title
            Console.Title = "LinkHTTP v" + LinkHTTP.Properties.Settings.Default.Version;

            // Меняем приоритет текущего процесса (для общего развития)
            Process ps = Process.GetCurrentProcess();
            ps.PriorityClass = ProcessPriorityClass.High;

            Random rand = new Random();

            if (!Directory.Exists(LinkHTTP.Properties.Settings.Default.wwwroot))
                Directory.CreateDirectory(LinkHTTP.Properties.Settings.Default.wwwroot);

            if (!File.Exists(LinkHTTP.Properties.Settings.Default.wwwroot + "\\index.html"))
                File.Create(LinkHTTP.Properties.Settings.Default.wwwroot + "\\index.html");

            ThreadScan = new Thread(delegate() { ParamScan(); }); // Поток сканирования
            ThreadScan.Start();

            ConsoleWrite("Фоновый поток сканирования файлов успешно запущен!", 1, 1);
            
            Thread.Sleep(rand.Next(500 , 1000));

            ThreadTmp = new Thread(delegate() { TmpClean(); }); // Поток удаления мусора
            ThreadTmp.Start();

            ConsoleWrite("Фоновый поток удаления мусора успешно запущен!", 1, 1);

            ThreadPool.SetMaxThreads(MaxThreadsCount, MaxThreadsCount);
            ThreadPool.SetMinThreads(MaxThreadsCount, MaxThreadsCount);

            Thread.Sleep(rand.Next(500, 1000));

            ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] Сервер запущен, необходимо дождатся первой индексации базы.", 1, 1);

            new Server(ServerPort);
        }

        public static void TmpClean()
        {
            DirectoryInfo di;

            while (true)
            {
                di = new DirectoryInfo(LinkHTTP.Properties.Settings.Default.wwwroot);
                
				foreach (DirectoryInfo df in di.GetDirectories())
                {
                    try
                    {
                        Directory.Delete(df.FullName, true);
                    }
                    catch (System.IO.IOException ex)
                    {
                        ErrorLog(ex);
                    }
                }

                if (Client.lockedList.Count > 0)
                    Client.lockedList.Clear();

                Thread.Sleep(LinkHTTP.Properties.Settings.Default.tempcleantime);
            }
        }

        public static void ParamScan()
        {
            while (true)
            {
                try
                {
                    if (Scan == true)
                    {
                        Swatch.Start();

                        Parallel.ForEach(PatchList, new ParallelOptions { MaxDegreeOfParallelism = MaxThreadsCount }, line =>
                        {
                            FindFiles(line, tmp);
                        });

                        Swatch.Stop();

                        TimeSpan = Swatch.Elapsed;
                        elapsedTime = String.Format("{0:00} мин. {1:00} сек. {2:00} мс.", TimeSpan.Minutes, TimeSpan.Seconds, TimeSpan.Milliseconds / 10);

                        ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] Индексировано файлов: " + FileList.Count() + " за (" + elapsedTime + ")", 5, 1);
                        ConsoleWrite("[" + DateTime.Now.ToLongTimeString() + "] Сервер запущен на порту " + ServerPort, 1, 1);
                        ConsoleWrite("-=- Всего доступно потоков: " + MaxThreadsCount, 3, 1);

                        Date = DateTime.Parse(DateTime.Now.ToLongTimeString());
                        ConsoleWrite("- Следующий запуск индексации будет произведен: " + Date.AddMilliseconds(+LinkHTTP.Properties.Settings.Default.indextime), 7, 1);

                        Swatch.Reset();
                    }
                    else
                    {
                        Functions.StrClean(8, 1);
                        Functions.StrClean(10, 1);
                        Functions.StrClean(11, 1);
                        Functions.StrClean(12, 1);

                        ConsoleWrite("- Модуль индексации успешно запущен в " + DateTime.Now.ToLongTimeString() + " - индексация не требуется.", 7, 1);
                        Thread.Sleep(5000);

                        Date = DateTime.Parse(DateTime.Now.ToLongTimeString());
                        ConsoleWrite("- Следующий запуск индексации будет произведен: " + Date.AddMilliseconds(+LinkHTTP.Properties.Settings.Default.indextime), 7, 1);
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(LinkHTTP.Properties.Settings.Default.indextime);
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
        }

        public static void FindInDir(DirectoryInfo dir, string pattern, bool recursive)
        {
                Parallel.ForEach(dir.GetFiles(pattern), new ParallelOptions { MaxDegreeOfParallelism = MaxThreadsCount }, line =>
                {
                    FileList.TryAdd(line.FullName, File.GetLastWriteTime(line.FullName));
                });

                if (recursive)
                {
                    Parallel.ForEach(dir.GetDirectories(), new ParallelOptions { MaxDegreeOfParallelism = MaxThreadsCount }, line2 =>
                    {
                        if (line2.CreationTime.Date.Year >= LinkHTTP.Properties.Settings.Default.Year)
                            FindInDir(line2, pattern, recursive);
                    });
                }
        }

        public static void FindFiles(string dir, string pattern)
        {
            FindInDir(new DirectoryInfo(dir), pattern, true);
        }

        public static void ConsoleWrite(string s, int positionX, int positionY)
        {
            try
            {
                Console.SetCursorPosition(positionY, positionX);
                Console.WriteLine(new String(' ', Console.WindowWidth));
                Console.SetCursorPosition(positionY, positionX);
                Console.WriteLine(s);
            }
            catch (ArgumentOutOfRangeException e)
            {
                Console.Clear();
                Console.WriteLine(e.Message);
            }
        }

        public static void ErrorLog(Exception ex)
        {
            System.IO.File.AppendAllText("error_" + DateTime.Now.ToString("d") + ".log", "[" + DateTime.Now.ToLongTimeString() + "] \r\n " + ex.ToString() + Environment.NewLine + "-----------------------------" + Environment.NewLine);
        }
    }
}
