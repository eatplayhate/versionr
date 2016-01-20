using System;
using System.IO;
using System.Security.Permissions;

namespace Logr
{
    public class Program
    {
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

            if (args.Length != 2)
            {
                Console.WriteLine("Usage: Logr.exe [repo path] [log destination path]");
                return;
            }

            Log log = new Log(args[0], args[1]);
            log.Update();
            log.Serialize();
        }
    }
}
