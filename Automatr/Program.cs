using System;
using System.Reflection;

using CommandLine;

namespace Automatr
{
    public class Program
    {

        public static AutomatrOptions Options { get; private set; }

        public static string Version
        {
            get
            {
                Assembly assembly = typeof(Program).Assembly;
                AssemblyName name = assembly.GetName();
                return name.Version.ToString();
            }
        }

        public static void Main(string[] args)
        {
            Options = new AutomatrOptions();
            Parser parser = new Parser(new Action<ParserSettings>((ParserSettings p) => { p.CaseSensitive = false; p.IgnoreUnknownArguments = false; p.MutuallyExclusive = true; }));
            bool parse = parser.ParseArguments(args, Options);

            if (Options.Version)
            {
                AutomatrLog.Log("Automatr " + Version, AutomatrLog.LogLevel.Info);
                return;
            }

            AutomatrConfig config = AutomatrConfig.Load(Options.ConfigPath);
            Automatr automatr = new Automatr(config);
            automatr.Run();
        }
    }
}
