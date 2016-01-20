using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automatr
{
    public class AutomatrLog
    {

        public enum LogLevel
        {
            Error = 0,
            Info = 1,
            Verbose = 2
        }

        public static LogLevel Level
        {
            get
            {
                return Program.Options.Verbose ? LogLevel.Verbose : LogLevel.Info;
            }
        }

        public static void Log(string message, LogLevel level = LogLevel.Verbose)
        {
            Log(message, true, level);
        }

        public static void Log(string message, bool newLine, LogLevel level = LogLevel.Verbose)
        {
            if (Level < level)
                return;

            Console.ForegroundColor = GetConsoleColor(level);
            if (newLine)
                Console.WriteLine(message);
            else
                Console.Write(message);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static ConsoleColor GetConsoleColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose: return ConsoleColor.DarkGray;
                case LogLevel.Info: return ConsoleColor.White;
                case LogLevel.Error: return ConsoleColor.Red;
            }
            return ConsoleColor.Gray;
        }

    }
}
