using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace Versionr.Network.SimpleWebService
{
    internal interface SimpleWebHandler
    {
        void HandleRequest(HttpListenerContext context);
    }
    internal class DirectHandler : SimpleWebHandler
    {
        public Action<HttpListenerContext> Handler { get; set; }

        public void HandleRequest(HttpListenerContext context)
        {
            Handler.Invoke(context);
        }
    }
    internal class WebService
    {
        static bool TriedToRunNetSH = false;
        ServerConfig Config { get; set; }
        byte[] Binaries { get; set; }
        Dictionary<string, SimpleWebHandler> RootHandlers { get; set; }
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
            if (Config.RequiresAuthentication && !Config.AllowUnauthenticatedRead && Config.SupportsSimpleAuthentication)
                listener.AuthenticationSchemes = AuthenticationSchemes.Basic;
            if (Config.WebService.ProvideBinaries)
            {
                System.IO.FileInfo assemblyFile = new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
                System.IO.DirectoryInfo containingDirectory = assemblyFile.Directory;
                System.IO.MemoryStream memoryStream = new System.IO.MemoryStream();
                System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create);
                CompressFolder(containingDirectory, string.Empty, archive);
                archive.Dispose();
                Binaries = memoryStream.ToArray();
            }
            RootHandlers = new Dictionary<string, SimpleWebHandler>();
            RootHandlers.Add("/", new DirectHandler()
            {

            });
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

        private void CompressFolder(DirectoryInfo containingDirectory, string path, ZipArchive archive)
        {
            foreach (var x in containingDirectory.EnumerateDirectories())
            {
                CompressFolder(x, (!string.IsNullOrEmpty(path) ? path + "\\" + x.Name : x.Name), archive);
            }
            foreach (var y in containingDirectory.EnumerateFiles())
            {
                if (y.Name == "." || y.Name == "..")
                    continue;
                var entry = archive.CreateEntry((!string.IsNullOrEmpty(path) ? path + "\\" + y.Name : y.Name), CompressionLevel.Optimal);
                using (var stream = entry.Open())
                using (var src = y.OpenRead())
                {
                    src.CopyTo(stream);
                }
            }
        }

        private void HandleRequest(HttpListenerContext httpListenerContext)
        {
            Printer.PrintDiagnostics("Received web request: {0}, raw url: {1}", httpListenerContext.Request.Url, httpListenerContext.Request.RawUrl);
            if (Config.RequiresAuthentication && !Config.AllowUnauthenticatedRead && Config.SupportsSimpleAuthentication)
            {
                HttpListenerBasicIdentity id = (HttpListenerBasicIdentity)httpListenerContext.User.Identity;
                if (!Config.SimpleAuthentication.CheckUser(id.Name, id.Password))
                {
                    httpListenerContext.Response.StatusCode = 401;
                    httpListenerContext.Response.StatusDescription = "Unauthorized";
                    using (var sr = new System.IO.StreamWriter(httpListenerContext.Response.OutputStream))
                        sr.Write("<html><head><title>Versionr Server</title></head><body>Unauthorized.</body></html>");
                    httpListenerContext.Response.Close();
                    Printer.PrintDiagnostics("Request was unauthorized.");
                    return;
                }
            }
            string uri = httpListenerContext.Request.RawUrl.Substring(Config.WebService.HttpSubdirectory.Length);
            if (Binaries != null && uri == "/binaries.pack")
            {
                httpListenerContext.Response.ContentLength64 = Binaries.Length;
                httpListenerContext.Response.SendChunked = false;
                httpListenerContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Zip;
                httpListenerContext.Response.AddHeader("Content-disposition", "attachment; filename=VersionrBinaries.zip");
                httpListenerContext.Response.OutputStream.Write(Binaries, 0, Binaries.Length);
                httpListenerContext.Response.Close();
                return;
            }
            using (var sr = new System.IO.StreamWriter(httpListenerContext.Response.OutputStream))
                sr.Write("<html><head><title>Versionr Server</title></head><body>Omg it works</body></html>");
        }
    }
}