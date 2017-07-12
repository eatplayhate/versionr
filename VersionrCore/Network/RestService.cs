using Grapevine.Interfaces.Server;
using Grapevine.Server;
using Grapevine.Server.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Versionr.Objects;
using Version = Versionr.Objects.Version;

namespace Versionr.Network
{
    // From SlackAPI/Extensions.cs, complete with bad spelling.
    public static class Extensions
    {
        /// <summary>
        /// Converts to a propert JavaScript timestamp interpretted by Slack.  Also handles converting to UTC.
        /// </summary>
        /// <param name="that"></param>
        /// <returns></returns>
        public static string ToProperTimeStamp(this DateTime that, bool toUTC = true)
        {
            if (toUTC)
            {
                return ((that.ToUniversalTime().Ticks - 621355968000000000m) / 10000000m).ToString("F6");
            }
            else
                return that.Subtract(new DateTime(1970, 1, 1)).TotalSeconds.ToString();
        }
    }

    internal class RestVersion
    {
        public string author { get; set; }
        public string id { get; set; }
        public string message { get; set; }
        public string parent { get; set; }
        public string branch { get; set; }
        public string timestamp { get; set; }
        public List<string> tags { get; set; }

        public static RestVersion FromVersion(Area ws, Version version)
        {
            if (version == null)
                return null;
            return new RestVersion()
            {
                author = version.Author,
                id = version.ID.ToString(),
                message = version.Message,
                parent = version.Parent.HasValue ? version.Parent.Value.ToString() : string.Empty,
                branch = ws.Branches.Where(x => x.ID == version.Branch).First().Name,
                timestamp = version.Timestamp.ToProperTimeStamp(),
                tags = ws.GetTagsForVersion(version.ID)
            };
        }
    };

    internal class RestList
    {
        public int maxResults { get; set; }
        public int startAt { get; set; }
        public int total { get; set; }
        public bool isLast { get; set; }
    }

    internal class RestVersionList : RestList
    {
        public List<RestVersion> versions { get; set; } = new List<RestVersion>();
    }

    internal class RestBranchEntry
    {
        public string name { get; set; }
        public string id { get; set; }
        public string parent { get; set; }
        public string deleted { get; set; }
    }

    internal class RestBranchList : RestList
    {
        public List<RestBranchEntry> branches { get; set; } = new List<RestBranchEntry>();
    }

    internal class RestTagJournalEntry
    {
        public RestVersion version { get; set; }
        public bool removing { get; set; }
        public string value { get; set; }
        public string time { get; set; }
    }

    internal class RestTagJournalList : RestList
    {
        public List<RestTagJournalEntry> tagjournals { get; set; } = new List<RestTagJournalEntry>();
    }

    [RestResource]
    internal class RestInterface
    {
        public static System.IO.DirectoryInfo Info { get; set; }

        public static string ToProperTimeStamp(DateTime dt, bool toUTC = true)
        {
            if (toUTC)
            {
                return ((dt.ToUniversalTime().Ticks - 621355968000000000m) / 10000000m).ToString("F6");
            }
            else
                return dt.Subtract(new DateTime(1970, 1, 1)).TotalSeconds.ToString();
        }

        [RestRoute(HttpMethod = Grapevine.Shared.HttpMethod.GET, PathInfo = "/versions")]
        public IHttpContext Versions(IHttpContext context)
        {
            int maxResults = 0;
            int startAt = 0;
            if (!int.TryParse(context.Request.QueryString["maxResults"], out maxResults))
            {
                maxResults = 50;
            }
            if (!int.TryParse(context.Request.QueryString["startAt"], out startAt))
            {
                startAt = 0;
            }
            string filterByBranchName = context.Request.QueryString["branch"] ?? string.Empty;

            RestVersionList restVersionList = new RestVersionList() { maxResults = maxResults, startAt = startAt };
            Area ws = Area.Load(Info, true, true);
            if (ws != null)
            {
                Branch branch = null;
                bool noResults = false;
                if (!string.IsNullOrWhiteSpace(filterByBranchName))
                {
                    branch = ws.Branches.Where(x => x.Name == filterByBranchName).FirstOrDefault();
                    noResults = branch == null;
                }

                if (!noResults)
                {
                    // ws.GetVersions should be in date descending order
                    // Getting one extra so we can see if we have got all entries
                    var versions = ws.GetVersions(branch, maxResults + startAt + 1).Skip(startAt).ToList();
                    restVersionList.isLast = versions.Count <= maxResults;
                    if (!restVersionList.isLast)
                    {
                        versions.RemoveAt(versions.Count - 1);
                    }
                    restVersionList.total = versions.Count;
                    foreach (var version in versions)
                    {
                        restVersionList.versions.Add(RestVersion.FromVersion(ws, version));
                    }

                }
            }

            SendResponse(context, JsonConvert.SerializeObject(restVersionList));

            return context;
        }

        [RestRoute(HttpMethod = Grapevine.Shared.HttpMethod.GET, PathInfo = "/branches")]
        public IHttpContext Branches(IHttpContext context)
        {
            int maxResults = 0;
            int startAt = 0;
            if (!int.TryParse(context.Request.QueryString["maxResults"], out maxResults))
            {
                maxResults = 50;
            }
            if (!int.TryParse(context.Request.QueryString["startAt"], out startAt))
            {
                startAt = 0;
            }

            RestBranchList restBranchList = new RestBranchList() { maxResults = maxResults, startAt = startAt };

            Area ws = Area.Load(Info, true, true);
            if (ws != null)
            {
                List<Branch> branches = ws.Branches.Skip(startAt).Take(maxResults + 1).ToList();
                restBranchList.isLast = branches.Count <= maxResults;
                if (!restBranchList.isLast)
                {
                    branches.RemoveAt(branches.Count - 1);
                }
                restBranchList.total = branches.Count;
                foreach (var branch in branches)
                {
                    restBranchList.branches.Add(new RestBranchEntry()
                    {
                        name = branch.Name,
                        id = branch.ID.ToString(),
                        parent = branch.Parent.HasValue ? branch.Parent.Value.ToString() : string.Empty,
                        deleted = branch.Terminus.HasValue ? "true" : "false"
                    });
                }
            }

            SendResponse(context, JsonConvert.SerializeObject(restBranchList));

            return context;
        }

        [RestRoute(HttpMethod = Grapevine.Shared.HttpMethod.GET, PathInfo = "/tagjournal")]
        public IHttpContext TagJournal(IHttpContext context)
        {
            int maxResults = 0;
            int startAt = 0;
            if (!int.TryParse(context.Request.QueryString["maxResults"], out maxResults))
            {
                maxResults = 50;
            }
            if (!int.TryParse(context.Request.QueryString["startAt"], out startAt))
            {
                startAt = 0;
            }

            RestTagJournalList restTagJournalList = new RestTagJournalList() { maxResults = maxResults, startAt = startAt };

            Area ws = Area.Load(Info, true, true);
            if (ws != null)
            {
                var tagJournalList = ws.GetTagJournalTimeOrder().Skip(startAt).Take(maxResults + 1).ToList();
                restTagJournalList.isLast = tagJournalList.Count <= maxResults;
                if (!restTagJournalList.isLast)
                {
                    tagJournalList.RemoveAt(tagJournalList.Count - 1);
                }
                restTagJournalList.total = tagJournalList.Count;
                foreach (var tagJournalEntry in tagJournalList)
                {
                    var restVersion = RestVersion.FromVersion(ws, ws.GetVersion(tagJournalEntry.Version));
                    // Some versions may not have replicated, soz.
                    if (restVersion == null)
                        continue;
                    restTagJournalList.tagjournals.Add(new RestTagJournalEntry()
                    {
                        version = restVersion,
                        removing = tagJournalEntry.Removing,
                        value = tagJournalEntry.Value,
                        time = tagJournalEntry.Time.ToProperTimeStamp(),
                    });
                }
            }

            SendResponse(context, JsonConvert.SerializeObject(restTagJournalList));

            return context;
        }

        // Default should be the last function in the class.
        // ORDER MATTERS
        [RestRoute]
        public IHttpContext Default(IHttpContext context)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("You are talking to the REST api for versionr.");
            sb.AppendLine("/versions for version list (aka log).");
            sb.AppendLine("  branch (string), filters to specific branch name");
            sb.AppendLine("/branches for branch entry list.");
            sb.AppendLine("/tagjournal for tag journal list.");
            sb.AppendLine("All lists return:");
            sb.AppendLine("  maxResults (integer), defaults to 50.");
            sb.AppendLine("  startAt (integer), defaults to 0.");
            sb.AppendLine("  total (integer), count of results returned.");
            sb.AppendLine("  isLast (bool), true if results contain the last result obtainable.");
            sb.AppendLine("All lists take:");
            sb.AppendLine("  maxResults, optionally specify desired entry count.");
            sb.AppendLine("  startAt, optionally specify desired start entry.");
            SendResponse(context, sb.ToString());
            return context;
        }

        private void SendResponse(IHttpContext context, string contents)
        {
            context.Response.AppendHeader("Access-Control-Allow-Origin", "*");
            context.Response.SendResponse(contents);
        }
    }

    internal class RestService
    {
        ServerConfig Config { get; set; }
        public RestService(ServerConfig config, System.IO.DirectoryInfo info)
        {
            Config = config;
            RestInterface.Info = info;
        }

        internal void Run()
        {
            using (var server = new RestServer())
            {
                server.Host = "+";
                server.Port = Config.RestService.Port.ToString();
                //server.LogToConsole();
                server.Start();
                Printer.PrintMessage($"Rest server started, bound to port #b#{server.Port}##.");
                while (true)
                {
                    System.Threading.Thread.Sleep(5000);
                }
            }
        }
    }
}
