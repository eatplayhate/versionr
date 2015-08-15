using System;
using System.Net;
using System.Threading.Tasks;

namespace Versionr.Network
{
    internal class WebService
    {
        static bool TriedToRunNetSH = false;
        ServerConfig Config { get; set; }
        public WebService(ServerConfig config)
        {
            Config = config;
        }
        internal void Run()
        {
            HttpListener listener = new HttpListener();
            Retry:
            string bind = "http://+:" + Config.WebService.HttpPort + string.Format("/{0}", Config.WebService.HttpSubdirectory);
            listener.Prefixes.Add(bind);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException e)
            {
                if (!Versionr.Utilities.MultiArchPInvoke.IsRunningOnMono && e.NativeErrorCode == 5 && TriedToRunNetSH == false)
                {
                    // access denied - we probably need to run netsh
                    Printer.PrintError("Unable to bind web interface. Requesting access rights.");
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        Verb = "runas",
                        FileName = "netsh",
                        Arguments = string.Format("http add urlacl url=\"{0}\" user=everyone", bind),
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process ps = System.Diagnostics.Process.Start(psi);
                    ps.WaitForExit();
                    TriedToRunNetSH = true;
                    goto Retry;
                }
                throw e;
            }
            Printer.PrintMessage("Running web interface on port #b#{0}##.", Config.WebService.HttpPort);
            while (true)
            {
                var ctx = listener.GetContext();
                Task.Run(() =>
                {
                    HandleRequest(ctx);
                });
            }
            listener.Stop();
        }

        private void HandleRequest(HttpListenerContext httpListenerContext)
        {
            Printer.PrintDiagnostics("Received web request: {0}", httpListenerContext.Request.Url);
            using (var sr = new System.IO.StreamWriter(httpListenerContext.Response.OutputStream))
                sr.Write("<html><head><title>WOW</title></head><body>Omg it works</body></html>");
        }
    }
}