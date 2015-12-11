using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logr
{
    public class Program
    {

        public static void Main(string[] args)
        {
            try
            {
                if (args.Length != 2)
                {
                    Console.WriteLine("Usage: Logr.exe [repo path] [log destination path]");
                    return;
                }

                Log log = new Log(args[0], args[1]);
                log.Update();
                log.Serialize();
            }
            catch
            {
                return;
            }
        }
    }
}
