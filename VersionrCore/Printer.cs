using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
    public static class Printer
    {
        public class PrinterStream : System.IO.TextWriter
        {
            StringBuilder Text = new StringBuilder();
            public override void Write(char value)
            {
                Text.Append(value);
            }

            public override Encoding Encoding
            {
                get { return Encoding.Default; }
            }

            public override void Flush()
            {
                Printer.Write(MessageType.Message, Text.ToString());
                Text.Clear();
            }
        }
        static public bool EnableDiagnostics { get; set; }
        static public bool Quiet { get; set; }
        static Printer()
        {
            EnableDiagnostics = false;
            OutputStyles = new Dictionary<char, OutputColour>();
            OutputStyles['b'] = OutputColour.Emphasis;
            OutputStyles['c'] = OutputColour.Blue;
            OutputStyles['w'] = OutputColour.Warning;
            OutputStyles['e'] = OutputColour.Error;
            OutputStyles['x'] = OutputColour.ErrorHeader;
            OutputStyles['q'] = OutputColour.Trace;
            OutputStyles['i'] = OutputColour.Invert;
            OutputStyles['s'] = OutputColour.Success;
            OutputStyles['z'] = OutputColour.WarningHeader;
        }
        public enum MessageType
        {
            Error,
            Warning,
            Message,
            Diagnostics,
            Interactive
        }

        enum OutputColour
        {
            Normal,
            Success,
            Emphasis,
            Blue,
            Warning,
            Error,
            Trace,
            Invert,

            ErrorHeader,
            WarningHeader,
        }

        static Dictionary<char, OutputColour> OutputStyles { get; set; }
        
        private static bool SuppressIndent = false;

        private static int IndentLevel { get; set; }

        static string Indent
        {
            get
            {
                string s = "";
                for (int i = 0; i < IndentLevel; i++)
                    s += "  ";
                return s;
            }
        }

        public static void PushIndent()
        {
            IndentLevel++;
        }
        public static void PopIndent()
        {
            if (IndentLevel > 0)
                IndentLevel--;
        }

        private static void FormatOutput(string v)
        {
            List<Tuple<OutputColour, string>> outputs = new List<Tuple<OutputColour, string>>();
            OutputColour currentColour = OutputColour.Normal;
            int pos = 0;
            while (pos < v.Length)
            {
                int nextLineBreak = v.IndexOf('\n', pos);
                int nextFindLocation = v.IndexOf('#', pos);
                if (nextLineBreak >= 0 && (nextFindLocation == -1 || nextLineBreak < nextFindLocation))
                {
                    outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, nextLineBreak + 1 - pos)));
                    pos = nextLineBreak + 1;
                }
                else if (nextFindLocation >= 0)
                {
                    int end = v.IndexOf('#', nextFindLocation + 1);
                    if (end <= nextFindLocation + 2)
                    {
                        // valid formatting tag
                        outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, nextFindLocation - pos)));
                        if (end == nextFindLocation + 1)
                            currentColour = OutputColour.Normal;
                        else
                        {
                            if (!OutputStyles.TryGetValue(v[nextFindLocation + 1], out currentColour))
                            {
                                SetOutputColour(OutputColour.Warning);
                                System.Console.WriteLine("Unrecognized formatting tag: '{0}'!", v[nextFindLocation + 1]);
                                currentColour = OutputColour.Normal;
                            }
                        }
                        pos = end + 1;
                    }
                    else
                    {
                        outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, nextFindLocation)));
                        pos = nextFindLocation + 1;
                    }
                }
                else if (nextLineBreak == -1)
                {
                    outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, v.Length - pos)));
                    pos = v.Length;
                }
            }

            bool newline = true;
            foreach (var x in outputs)
            {
                if (newline)
                {
                    FlushOutput(true, x);
                    newline = false;
                }
                else
                    FlushOutput(false, x);
                if (x.Item2.EndsWith("\n"))
                    newline = true;
            }
            SetOutputColour(OutputColour.Normal);
        }

        public static void PrintError(string v)
        {
            Write(MessageType.Error, "#x#" + v + "##\n");
        }

        public static void PrintError(string v, params object[] obj)
        {
            Write(MessageType.Error, "#x#" + string.Format(v, obj) + "##\n");
        }

        public static void PrintDiagnostics(string v)
        {
            Write(MessageType.Diagnostics, "#q#" + v + "\n");
        }

        public static void PrintDiagnostics(string v, params object[] obj)
        {
            Write(MessageType.Diagnostics, "#q#" + string.Format(v, obj) + "\n");
        }

        public static void PrintWarning(string v)
        {
            Write(MessageType.Warning, "#w#" + v + "\n");
        }

        public static void PrintWarning(string v, params object[] obj)
        {
            Write(MessageType.Warning, "#w#" + string.Format(v, obj) + "\n");
        }
        public static void PrintMessage(string v)
        {
            Write(MessageType.Message, v + "\n");
        }

        public static void PrintMessage(string v, params object[] obj)
        {
            Write(MessageType.Message, string.Format(v, obj) + "\n");
        }

        public static void PrintMessageSingleLine(string v)
        {
            Write(MessageType.Message, v);
        }

        public static void PrintMessageSingleLine(string v, params object[] obj)
        {
            Write(MessageType.Message, string.Format(v, obj));
        }
        private static object SyncObject = new object();
        private static void FlushOutput(bool newline, Tuple<OutputColour, string> x)
        {
            lock (SyncObject)
            {
                string indent = Indent;
                if (!string.IsNullOrEmpty(x.Item2))
                {
                    SetOutputColour(x.Item1);
                    if (newline && !SuppressIndent)
                        System.Console.Write(indent + x.Item2);
                    else
                        System.Console.Write(x.Item2);
                    SuppressIndent = false;
                }
                else if (newline && !SuppressIndent)
                    System.Console.Write(indent);
                SuppressIndent = false;
            }
        }

        private static void SetOutputColour(OutputColour style)
        {
            switch (style)
            {
                case OutputColour.Normal:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                case OutputColour.Success:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    break;
                case OutputColour.Blue:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case OutputColour.Emphasis:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.White;
                    break;
                case OutputColour.Warning:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case OutputColour.Error:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case OutputColour.ErrorHeader:
                    System.Console.BackgroundColor = ConsoleColor.Red;
                    System.Console.ForegroundColor = ConsoleColor.White;
                    break;
                case OutputColour.WarningHeader:
                    System.Console.BackgroundColor = ConsoleColor.Yellow;
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    break;
                case OutputColour.Trace:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
                case OutputColour.Invert:
                    System.Console.BackgroundColor = ConsoleColor.White;
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    break;
                default:
                    System.Console.BackgroundColor = ConsoleColor.Black;
                    System.Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }
        }
        public static void Write(MessageType type, string message)
        {
            lock (SyncObject)
            {
                if (type == MessageType.Diagnostics && !EnableDiagnostics)
                    return;
                if (Quiet && type != MessageType.Warning && type != MessageType.Error)
                    return;
                if (type != MessageType.Interactive)
                {
                    ClearInteractive();
                }
                FormatOutput(message);
            }
        }

        internal static void ClearInteractive()
        {
            if (LastPrinter != null)
            {
                int clearLength = System.Console.CursorLeft;
                System.Console.CursorLeft = 0;
                FormatOutput(new string(' ', clearLength));
                System.Console.CursorLeft = 0;
                LastPrinter = null;
            }
        }

        public static void WriteLineError(string error)
        {
            Write(MessageType.Error, error + "\n");
        }
        public static void WriteLineError(string error, params object[] args)
        {
            string result = string.Format(error, args);
            WriteLineError(result);
        }
        public static void WriteLineWarning(string warning)
        {
            Write(MessageType.Warning, warning + "\n");
        }
        public static void WriteLineWarning(string warning, params object[] args)
        {
            string result = string.Format(warning, args);
            WriteLineWarning(result);
        }
        public static void WriteLineDiagnostics(string message)
        {
            Write(MessageType.Diagnostics, message + "\n");
        }
        public static void WriteLineDiagnostics(string message, params object[] args)
        {
            string result = string.Format(message, args);
            WriteLineDiagnostics(result);
        }
        public static void WriteLineMessage(string message)
        {
            Write(MessageType.Message, message + "\n");
        }
        public static void WriteLineMessage(string message, params object[] args)
        {
            string result = string.Format(message, args);
            WriteLineMessage(result);
        }

        public abstract class InteractivePrinter
        {
            internal string Header { get; set; }
            internal string Last { get; set; }
            internal int ConsoleLeft { get; set; }
            internal Func<object, string> Formatter { get; set; }
            internal abstract void Start(string s);
            public abstract void Update(object obj);
            public virtual void End(object obj)
            {
                lock (Printer.SyncObject)
                {
                    Update(obj);
                    Printer.Write(MessageType.Interactive, "\n");
                    Printer.LastPrinter = null;
                }
            }
        }

        internal class SimplePrinter : InteractivePrinter
        {
            internal override void Start(string title)
            {
                Header = title + ": ";
                lock (Printer.SyncObject)
                {
                    Printer.ClearInteractive();
                    Printer.Write(MessageType.Interactive, Header);
                    Printer.LastPrinter = this;
                    ConsoleLeft = System.Console.CursorLeft;
                }
                Last = string.Empty;
            }
            public override void Update(object amount)
            {
                lock (Printer.SyncObject)
                {
                    if (Printer.LastPrinter != this)
                    {
                        Printer.ClearInteractive();
                        Printer.Write(MessageType.Interactive, Header);
                        Printer.LastPrinter = this;
                        ConsoleLeft = System.Console.CursorLeft;
                    }
                    string last = Last;
                    Last = Formatter(amount);
                    System.Console.CursorLeft = ConsoleLeft;
                    string output = Last;
                    while (output.Length < last.Length)
                        output += ' ';
                    System.Console.CursorLeft = ConsoleLeft;
                    Write(MessageType.Interactive, output);
                }
            }
        }

        internal class BarPrinter : InteractivePrinter
        {
            internal Func<object, float> PercentCalculator { get; set; }
            internal Func<float, object, string> PercentFormatter { get; set; }
            internal int Width { get; set; }
            internal string Before { get; set; }
            internal override void Start(string title)
            {
                if (string.IsNullOrEmpty(title))
                    Header = null;
                else
                    Header = title + ":\n";
                lock (Printer.SyncObject)
                {
                    if (!string.IsNullOrEmpty(Header))
                        Printer.Write(MessageType.Interactive, Header);
                    Printer.LastPrinter = this;
                    ConsoleLeft = System.Console.CursorLeft;
                }
                Last = string.Empty;
            }
            public override void Update(object amount)
            {
                string fmt = Formatter(amount);
                float pct = PercentCalculator(amount);
                lock (Printer.SyncObject)
                {
                    if (Printer.LastPrinter != this)
                    {
                        Printer.LastPrinter = this;
                        ConsoleLeft = 0;
                    }
                    string pctString = PercentFormatter(pct, amount);
                    string bar;
                    int extra = 0;
                    if (!string.IsNullOrEmpty(Before))
                    {
                        extra += Before.Length + 2;
                        bar = Before + " [";
                    }
                    else
                        bar = "[";
                    int midpoint = ((Width - 2) - pctString.Length) / 2;
                    int totalValue = (int)System.Math.Ceiling(((Width) / 100.0) * pct);
                    for (int i = 0; i < midpoint; i++)
                    {
                        if (i < totalValue)
                            bar += '=';
                        else
                            bar += '.';
                    }
                    bar += pctString;
                    for (int i = bar.Length; i < Width - 1 + extra; i++)
                    {
                        if (i < totalValue + extra)
                            bar += '=';
                        else
                            bar += '.';
                    }
                    System.Console.CursorLeft = 0;
                    bar += "] " + fmt;
                    Write(MessageType.Interactive, bar);
                }
            }
        }
        static InteractivePrinter LastPrinter = null;
        public static InteractivePrinter CreateSimplePrinter(string title, Func<object, string> formatter)
        {
            InteractivePrinter printer = new SimplePrinter()
            {
                Header = title,
                Formatter = formatter
            };

            printer.Start(title);

            return printer;
        }
        public static InteractivePrinter CreateProgressBarPrinter(string initialLine, string title, Func<object, string> formatter, Func<object, float> percentCalculator, Func<float, object, string> percentFormatter, int barWidth)
        {
            InteractivePrinter printer = new BarPrinter()
            {
                Before = title,
                Formatter = formatter,
                PercentCalculator = percentCalculator,
                PercentFormatter = percentFormatter,
                Width = barWidth
            };

            printer.Start(initialLine);

            return printer;
        }
    }
}
