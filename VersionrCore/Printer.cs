using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
    public static class Printer
    {
        static public bool EnableDiagnostics { get; set; }
        static Printer()
        {
            EnableDiagnostics = false;
        }
        enum MessageType
        {
            Error,
            Warning,
            Message,
            Diagnostics
        }
        private static void PrintInternal(MessageType type, string message)
        {
            if (type == MessageType.Diagnostics && !EnableDiagnostics)
                return;
            if (type == MessageType.Diagnostics)
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine(message);
            System.Console.ForegroundColor = ConsoleColor.Gray;
        }
        public static void PrintError(string error)
        {
            PrintInternal(MessageType.Error, error);
        }
        public static void PrintError(string error, params object[] args)
        {
            string result = string.Format(error, args);
            PrintError(result);
        }
        public static void PrintWarning(string warning)
        {
            PrintInternal(MessageType.Warning, warning);
        }
        public static void PrintWarning(string warning, params object[] args)
        {
            string result = string.Format(warning, args);
            PrintWarning(result);
        }
        public static void PrintDiagnostics(string message)
        {
            PrintInternal(MessageType.Diagnostics, message);
        }
        public static void PrintDiagnostics(string message, params object[] args)
        {
            string result = string.Format(message, args);
            PrintDiagnostics(result);
        }
        public static void PrintMessage(string message)
        {
            PrintInternal(MessageType.Message, message);
        }
        public static void PrintMessage(string message, params object[] args)
        {
            string result = string.Format(message, args);
            PrintMessage(result);
        }
    }
}
