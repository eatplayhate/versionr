﻿using System;
using System.Collections.Generic;
using System.Text;

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

        static public System.IO.StreamWriter LogStream { get; set; }
        static public bool EnableDiagnostics { get; set; }
        static public bool Quiet { get; set; }
        static public ConsoleColor DefaultBGColour { get; set; }
        static public ConsoleColor DefaultColour { get; set; }
        static Printer()
        {
            EnableDiagnostics = false;
            OutputStyles = new Dictionary<char, OutputColour>();
            OutputStyles['b'] = OutputColour.Emphasis;
            OutputStyles['c'] = OutputColour.Cyan;
            OutputStyles['w'] = OutputColour.Warning;
            OutputStyles['e'] = OutputColour.Error;
            OutputStyles['x'] = OutputColour.ErrorHeader;
            OutputStyles['q'] = OutputColour.Trace;
            OutputStyles['i'] = OutputColour.Invert;
            OutputStyles['s'] = OutputColour.Success;
            OutputStyles['z'] = OutputColour.WarningHeader;

			OutputStyles['U'] = OutputColour.Blue;
			OutputStyles['C'] = OutputColour.Cyan;
			OutputStyles['u'] = OutputColour.DarkBlue;
			OutputStyles['c'] = OutputColour.DarkCyan;
			OutputStyles['a'] = OutputColour.DarkGray;
			OutputStyles['g'] = OutputColour.DarkGreen;
			OutputStyles['m'] = OutputColour.DarkMagenta;
			OutputStyles['r'] = OutputColour.DarkRed;
			OutputStyles['y'] = OutputColour.DarkYellow;
			OutputStyles['A'] = OutputColour.Gray;
			OutputStyles['G'] = OutputColour.Green;
			OutputStyles['M'] = OutputColour.Magenta;
			OutputStyles['R'] = OutputColour.Red;
			OutputStyles['I'] = OutputColour.White;
			OutputStyles['Y'] = OutputColour.Yellow;
            
            try
            {
                if (System.Console.IsOutputRedirected)
                    AllowInteractivePrinting = false;
                else
                {
                    System.Console.CursorLeft += 1;
                    System.Console.CursorLeft -= 1;
                    AllowInteractivePrinting = true;
                    System.Console.CursorVisible = false;
                    DefaultBGColour = System.Console.BackgroundColor;
                    DefaultColour = System.Console.ForegroundColor;
                }
            }
            catch
            {
                AllowInteractivePrinting = false;
            }
        }

        public static void OpenLog(string log)
        {
            LogStream = new System.IO.StreamWriter(System.IO.File.Open(log, System.IO.FileMode.Create));
            LogStream.NewLine = "\\r\\n";
            LogStream.AutoFlush = true;
        }
        
        public static void RestoreDefaults()
        {
            try
            {
                System.Console.CursorVisible = true;
                System.Console.BackgroundColor = DefaultBGColour;
                System.Console.ForegroundColor = DefaultColour;
            }
            catch
            {
                AllowInteractivePrinting = false;
            }
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
			SystemDefault,
			Invert,

			// all of these use the DefaultBGColour
            Black,
			DarkBlue,
			DarkCyan,
			DarkGray,
			DarkGreen,
			DarkMagenta,
			DarkRed,
			DarkYellow,
            Blue,
            Cyan,
            Gray,
			Green,
			Magenta,
			Red,
			White,
			Yellow,

			ErrorHeader,
			WarningHeader,
			Normal = SystemDefault,
			Success = Green,
			Emphasis = White,
			Warning = Yellow,
			Error = Red,
			Trace = DarkGray,
        }

        static Dictionary<char, OutputColour> OutputStyles { get; set; }
        
        private static bool SuppressIndent = false;
        private static bool AllowInteractivePrinting = true;
        public static bool NoColours = false;

        public static bool Prompt(string v)
        {
            PrintMessage("\n#b#{0}## - (y)es or (n)o", v);
            while (true)
            {
                string s = System.Console.ReadLine();
                if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                    return true;
                else if (s.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        private static int IndentLevel { get; set; }

        public static string Prefix { get; set; } = "";

        static string Indent
        {
            get
            {
                string s = Prefix;
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

        public static string Escape(string s)
        {
            return s.Replace("#", "\\#");
        }

        private static void FormatOutput(string v, bool bypassLog)
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
                    // escaping a hash sign
                    if (nextFindLocation > 0 && v[nextFindLocation - 1] == '\\')
                    {
                        int len = nextFindLocation - 1 - pos;
                        if (len > 0)
                            outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, len)));
                        outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(nextFindLocation, 1)));
                        pos = nextFindLocation + 1;
                        continue;
                    }
                    int end = v.IndexOf('#', nextFindLocation + 1);
                    if (end <= nextFindLocation + 2 && end != -1)
                    {
                        // valid formatting tag
                        outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, nextFindLocation - pos)));
                        if (end == nextFindLocation + 1)
                        {
                            currentColour = OutputColour.Normal;
                            pos = end + 1;
                        }
                        else
                        {
                            if (!OutputStyles.TryGetValue(v[nextFindLocation + 1], out currentColour))
                            {
                                SetOutputColour(OutputColour.Warning);
                                if (EnableDiagnostics)
                                    System.Console.WriteLine("Unrecognized formatting tag: '{0}'!", v[nextFindLocation + 1]);
                                currentColour = OutputColour.Normal;
                                if (nextFindLocation > pos)
                                {
                                    pos = nextFindLocation;
                                    outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, 1)));
                                }
                                else
                                    pos = pos + 1;
                            }
                            else
                                pos = end + 1;
                        }
                    }
                    else
                    {
                        outputs.Add(new Tuple<OutputColour, string>(currentColour, v.Substring(pos, nextFindLocation - pos)));
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
                    FlushOutput(true, x, bypassLog);
                    newline = false;
                }
                else
                    FlushOutput(false, x, bypassLog);
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
            if (EnableDiagnostics)
                Write(MessageType.Diagnostics, "#q#" + v + "\n");
        }

        public static void PrintDiagnostics(string v, params object[] obj)
        {
            if (EnableDiagnostics)
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
        private static void FlushOutput(bool newline, Tuple<OutputColour, string> x, bool bypassLog)
        {
            lock (SyncObject)
            {
                string indent = Indent;
                if (!string.IsNullOrEmpty(x.Item2))
                {
                    SetOutputColour(x.Item1);
                    if (newline && !SuppressIndent)
                        FlushUnformatted(indent + x.Item2, bypassLog);
                    else
                        FlushUnformatted(x.Item2, bypassLog);
                    SuppressIndent = false;
                }
                else if (newline && !SuppressIndent)
                    FlushUnformatted(indent, bypassLog);
                SuppressIndent = false;
            }
        }

        private static void FlushUnformatted(string message, bool bypassLog)
        {
            System.Console.Write(message);
            if (!bypassLog && LogStream != null)
            {
                LogStream.Write(message);
            }
            if (MessagePrinted != null)
                MessagePrinted(null, new MessagePrintedEventArgs(message));
        }

        static OutputColour PreviousColour = (OutputColour)(-1);

        private static void SetOutputColour(OutputColour style)
        {
            if (NoColours)
                return;
            if (style == PreviousColour)
                return;
            PreviousColour = style;

            if (Utilities.MultiArchPInvoke.RunningPlatform == Utilities.Platform.Windows && !Utilities.MultiArchPInvoke.IsBashOnWindows)
            {
                switch (style)
                {
					case OutputColour.SystemDefault:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = DefaultColour;
						break;

                    case OutputColour.Black:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = ConsoleColor.Black;
                        break;
					case OutputColour.DarkBlue:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkBlue;
						break;
					case OutputColour.DarkCyan:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkCyan;
						break;
					case OutputColour.DarkGray:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkGray;
						break;
					case OutputColour.DarkGreen:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkGreen;
						break;
					case OutputColour.DarkMagenta:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkMagenta;
						break;
					case OutputColour.DarkRed:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkRed;
						break;
					case OutputColour.DarkYellow:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.DarkYellow;
						break;
                    case OutputColour.Blue:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = ConsoleColor.Blue;
                        break;
                    case OutputColour.Cyan:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case OutputColour.Gray:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.Gray;
						break;
					case OutputColour.Green:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.Green;
						break;
					case OutputColour.Magenta:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.Magenta;
						break;
					case OutputColour.Red:
						System.Console.BackgroundColor = DefaultBGColour;
						System.Console.ForegroundColor = ConsoleColor.Red;
						break;
					case OutputColour.White:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case OutputColour.Yellow:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case OutputColour.ErrorHeader:
                        System.Console.BackgroundColor = ConsoleColor.Red;
                        System.Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case OutputColour.WarningHeader:
                        System.Console.BackgroundColor = ConsoleColor.Yellow;
                        System.Console.ForegroundColor = ConsoleColor.Black;
                        break;
                    
                    case OutputColour.Invert:
                        System.Console.BackgroundColor = ConsoleColor.White;
                        System.Console.ForegroundColor = ConsoleColor.Black;
                        break;
                    default:
                        System.Console.BackgroundColor = DefaultBGColour;
                        System.Console.ForegroundColor = DefaultColour;
                        break;
                }
            }
            else
            {
                System.Console.Write("\x1b[0m");
                switch (style)
                {
                    default:
                    case OutputColour.SystemDefault:
                        System.Console.Write("\x1b[0m");
                        break;

                    case OutputColour.Black:
                        System.Console.Write("\x1b[30m");
                        break;
                    case OutputColour.DarkBlue:
                        System.Console.Write("\x1b[34m");
                        break;
                    case OutputColour.DarkCyan:
                        System.Console.Write("\x1b[36m");
                        break;
                    case OutputColour.DarkGray:
                        System.Console.Write("\x1b[90m");
                        break;
                    case OutputColour.DarkGreen:
                        System.Console.Write("\x1b[32m");
                        break;
                    case OutputColour.DarkMagenta:
                        System.Console.Write("\x1b[35m");
                        break;
                    case OutputColour.DarkRed:
                        System.Console.Write("\x1b[31m");
                        break;
                    case OutputColour.DarkYellow:
                        System.Console.Write("\x1b[33m");
                        break;

                    case OutputColour.Blue:
                        System.Console.Write("\x1b[94m");
                        break;
                    case OutputColour.Cyan:
                        System.Console.Write("\x1b[96m");
                        break;
                    case OutputColour.Gray:
                        System.Console.Write("\x1b[37m");
                        break;
                    case OutputColour.Green:
                        System.Console.Write("\x1b[92m");
                        break;
                    case OutputColour.Magenta:
                        System.Console.Write("\x1b[95m");
                        break;
                    case OutputColour.Red:
                        System.Console.Write("\x1b[91m");
                        break;
                    case OutputColour.Yellow:
                        System.Console.Write("\x1b[93m");
                        break;
                    case OutputColour.White:
                        System.Console.Write("\x1b[97m");
                        break;

                    case OutputColour.ErrorHeader:
                        System.Console.Write("\x1b[101;97m");
                        break;
                    case OutputColour.WarningHeader:
                        System.Console.Write("\x1b[30;103m");
                        break;

                    case OutputColour.Invert:
                        System.Console.Write("\033[1;97m");
                        System.Console.Write("\033[0;30m");
                        break;
                }
            }
        }
        public static void Write(MessageType type, string message)
        {
            lock (SyncObject)
            {
                if (type == MessageType.Interactive && !AllowInteractivePrinting)
                    return;
                if (type == MessageType.Diagnostics && !EnableDiagnostics)
                    return;
                if (Quiet && type != MessageType.Warning && type != MessageType.Error)
                    return;
                if (type != MessageType.Interactive)
                {
                    ClearInteractive();
                }
                FormatOutput(message, type == MessageType.Interactive);
            }
        }

        internal static void ClearInteractive()
        {
            if (LastPrinter != null)
            {
                int clearLength = System.Console.CursorLeft;
                System.Console.CursorLeft = 0;
                FormatOutput(new string(' ', clearLength), true);
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


        public class MessagePrintedEventArgs
        {
            public MessagePrintedEventArgs(string message)
            {
                Message = message;
            }
            public string Message { get; private set; }
        }
        public delegate void MessagePrintedEventHandler(object sender, MessagePrintedEventArgs e);
        public static event MessagePrintedEventHandler MessagePrinted;

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

        internal class NullPrinter : InteractivePrinter
        {
            public override void Update(object obj)
            {
            }

            internal override void Start(string s)
            {
            }
        }

        internal class SpinnerPrinter : InteractivePrinter
        {
            static char[] Spinners = new char[] { '/', '-', '\\', '|', '/', '-', '\\', '|' };
            int SpinnerIndex = 0;
            internal override void Start(string title)
            {
                Header = title + " ";
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
                    Last = Formatter(amount) + " " + Next();
                    System.Console.CursorLeft = ConsoleLeft;
                    string output = Last;
                    while (output.Length < last.Length)
                        output += ' ';
                    System.Console.CursorLeft = ConsoleLeft;
                    Write(MessageType.Interactive, output);
                }
            }

            private string Next()
            {
                SpinnerIndex = (SpinnerIndex + 1) % Spinners.Length;
                return new string(Spinners[SpinnerIndex], 1);
            }
            public override void End(object obj)
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
                    string lastValue = Last == null ? string.Empty : Last;
                    string bar = new string(' ', lastValue.Length);
                    Printer.Write(MessageType.Interactive, bar);
                    Printer.Write(MessageType.Interactive, "\n");
                    System.Console.CursorLeft = 0;
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

        internal class SpinnerBarPrinter : InteractivePrinter
        {
            internal int Width { get; set; }
            internal string Before { get; set; }
            internal string Final { get; set; }
            int Index = 0;
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
                lock (Printer.SyncObject)
                {
                    if (Printer.LastPrinter != this)
                    {
                        Printer.ClearInteractive();
                        Printer.LastPrinter = this;
                        ConsoleLeft = 0;
                    }
                    string bar;
                    int extra = 0;
                    if (!string.IsNullOrEmpty(Before))
                    {
                        extra += Before.Length + 2;
                        bar = Before + " [";
                    }
                    else
                        bar = "[";
                    StringBuilder sb = new StringBuilder();
                    int x = Index;
                    for (int i = 0; i < Width - 2; i++)
                    {
                        switch (x)
                        {
                            case 0:
                                sb.Append("#q#=");
                                break;
                            case 1:
                                sb.Append("##=");
                                break;
                            case 2:
                                sb.Append("#b#=");
                                break;
                            case 3:
                                sb.Append("##=");
                                break;
                        }
                        x++;
                        if (x == 4)
                            x = 0;
                    }
                    Index = (Index + 1) % 4;
                    System.Console.CursorLeft = 0;
                    bar += sb.ToString() + "##] " + fmt;
                    string lastValue = Final == null ? string.Empty : Final;
                    Final = bar;
                    while (lastValue.Length > bar.Length)
                        bar += " ";
                    Write(MessageType.Interactive, bar);
                }
            }
            public override void End(object obj)
            {
                lock (Printer.SyncObject)
                {
                    if (Printer.LastPrinter != this)
                    {
                        Printer.ClearInteractive();
                        Printer.LastPrinter = this;
                        ConsoleLeft = 0;
                    }
                    string lastValue = Final == null ? string.Empty : Final;
                    string bar = new string(' ', lastValue.Length);
                    System.Console.CursorLeft = 0;
                    Printer.Write(MessageType.Interactive, bar);
                    System.Console.CursorLeft = 0;
                    Printer.LastPrinter = null;
                }
            }
        }

        internal class BarPrinter : InteractivePrinter
        {
            internal Func<object, float> PercentCalculator { get; set; }
            internal Func<float, object, string> PercentFormatter { get; set; }
            internal int Width { get; set; }
            internal string Before { get; set; }
            internal string Final { get; set; }
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
                if (System.Threading.Monitor.TryEnter(Printer.SyncObject))
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
                    if (Utilities.MultiArchPInvoke.IsRunningOnMono)
                        System.Console.CursorLeft = 0;
                    else
                        bar = "\r" + bar;
                    bar += "] " + fmt;
                    string lastValue = Final == null ? string.Empty : Final;
                    Final = bar;
                    while (lastValue.Length > bar.Length)
                        bar += " ";
                    Write(MessageType.Interactive, bar);
                    System.Threading.Monitor.Exit(Printer.SyncObject);
                }
            }
        }
        static InteractivePrinter LastPrinter = null;
        public static InteractivePrinter CreateSpinnerPrinter(string title, Func<object, string> formatter)
        {
            if (!AllowInteractivePrinting)
                return new NullPrinter();
            InteractivePrinter printer = new SpinnerPrinter()
            {
                Header = title,
                Formatter = formatter
            };

            printer.Start(title);

            return printer;
        }
        public static InteractivePrinter CreateSimplePrinter(string title, Func<object, string> formatter)
        {
            if (!AllowInteractivePrinting)
                return new NullPrinter();
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
            if (!AllowInteractivePrinting)
                return new NullPrinter();
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
        public static InteractivePrinter CreateSpinnerBarPrinter(string initialLine, string title, Func<object, string> formatter, int barWidth)
        {
            if (!AllowInteractivePrinting)
                return new NullPrinter();
            InteractivePrinter printer = new SpinnerBarPrinter()
            {
                Before = title,
                Formatter = formatter,
                Width = barWidth
            };

            printer.Start(initialLine);

            return printer;
        }
    }
}
