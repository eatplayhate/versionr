using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Automatr
{
    public class Program
    {

        public static void Main(string[] args)
        {
            AutomatrConfig config = AutomatrConfig.Load();
            Automatr automatr = new Automatr(config);
            automatr.Run();

            Console.ReadLine();
        }
    }
}
