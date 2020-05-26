using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using HTTPServer;
using System.Security.Cryptography;

namespace LinkHTTP
{
    class Functions
    {
        // Метод очистки консоли
        public static void StrClean(int positionY, int positionX)
        {
            Server.ConsoleWrite("", positionY, positionX);
            Server.ConsoleWrite("", 10, 1);
            Server.ConsoleWrite("", 11, 1);
            Server.ConsoleWrite("", 12, 1);
        }

        public static string generateHash(string input)
        {
            MD5 md5Hasher = MD5.Create();
            byte[] data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(input));
            return BitConverter.ToString(data);
        }
    }
}
