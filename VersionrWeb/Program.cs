using Nancy.Hosting.Self;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb
{
	class Program
	{
		static void Main(string[] args)
		{
			using (var host = new NancyHost(new Uri("http://localhost:8086")))
			{
				host.Start();
				Console.WriteLine("VersionrWeb is running...");
				Console.ReadLine();
			}
		}
	}
}
