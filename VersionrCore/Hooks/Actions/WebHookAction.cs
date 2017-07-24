using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Versionr.Hooks
{
    class WebHookAction : IHookAction
    {
        public WebHookAction(Newtonsoft.Json.Linq.JObject configuration)
        {
            m_BaseUrl = (string)configuration["url"];
            m_Username = (string)configuration["username"];
            m_Password = (string)configuration["password"];
        }

        public bool Raise(IHook hook, string filtername)
        {
            Printer.PrintMessage($"WebHookAction {m_BaseUrl}");

            string query = m_BaseUrl;
            var rms = hook.Workspace.GetRemotes();
            
            var url = hook.Workspace.GetRemotes().First().URL.TrimEnd('/');
            Printer.PrintMessage($"      url: {url}");
            query = query.Replace("${url}", url);

            var branch = hook.Branch;
            Printer.PrintMessage($"   branch: {branch?.Name ?? ""} ({branch.ID})");
            query = query.Replace("${branch}", branch?.Name ?? "");

            var version = hook.Version;
            Printer.PrintMessage($"  version: {version?.ID.ToString() ?? ""}");
            query = query.Replace("${version}", version?.ID.ToString() ?? "");

            var tags = hook.AllTags;
            Printer.PrintMessage(String.Format("     tags: [{0}]", String.Join(", ", tags)));
            var urlTags = String.Join("", tags.Select(x => x.Replace('#', '$')));
            query = query.Replace("${url_tags}", urlTags);

            var c = new HttpClient();
            var r = c.SendAsync(new HttpRequestMessage(HttpMethod.Get, query));

            return r.Wait(TimeSpan.FromSeconds(10));
        }

        private string m_BaseUrl = "";
        private string m_Username = "";
        private string m_Password = "";
    }
}
