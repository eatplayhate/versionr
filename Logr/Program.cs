using CommandLine;
using System;
using System.IO;
using System.Security.Permissions;
using Versionr;

namespace Logr
{
    public class Program
    {
        public static LogrOptions Options { get; private set; }

        static void ExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            using (StreamWriter writer = File.AppendText(@"C:\Versionr\logr.log"))
            {
                writer.WriteLine("{0} {1}: {2}", DateTime.Now.ToLongTimeString(), DateTime.Now.ToLongDateString(), ((Exception)e.ExceptionObject).Message);
            }
            Environment.Exit(0);
        }

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlAppDomain)]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(ExceptionHandler);

            Printer.PrinterStream printerStream = new Printer.PrinterStream();
            Parser parser = new Parser(new Action<ParserSettings>(
                (ParserSettings p) => { p.CaseSensitive = false; p.IgnoreUnknownArguments = false; p.HelpWriter = printerStream; p.MutuallyExclusive = true; }));

            Options = new LogrOptions();
            if (!parser.ParseArguments(args, Options))
            {
                printerStream.Flush();
                Printer.RestoreDefaults();
                Environment.Exit(CommandLine.Parser.DefaultExitCodeFail);
            }
            
            Log log = new Log(Options.Repo, Options.LogFile, (Options.Limit > 0) ? Options.Limit : (int?)null);
            log.Update();
            log.Serialize();
        }
    }
}
