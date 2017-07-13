using Newtonsoft.Json;
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Versionr.Utilities
{
    public static class DirectivesUtils
    {
        public static string GetVRMetaPath(Area area)
        {
            return Path.Combine(area.Root.FullName, ".vrmeta");
        }

        public static string GetGlobalVRUserPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vruser");
        }

        public static string GetVRUserPath(Area area)
        {
            return Path.Combine(area.Root.FullName, ".vruser");
        }

        public static Directives LoadVRMeta(Area area)
        {
            string error;
            return LoadVRMeta(area, out error);
        }
        
        public static Directives LoadVRMeta(Area area, out string error)
        {
            return LoadDirectives(GetVRMetaPath(area), "Versionr", out error);
        }

        public static Directives LoadGlobalVRUser()
        {
            string error;
            return LoadGlobalVRUser(out error);
        }

        public static Directives LoadGlobalVRUser(out string error)
        {
            return LoadDirectives(GetGlobalVRUserPath(), "Versionr", out error);
        }

        public static Directives LoadVRUser(Area area)
        {
            string error;
            return LoadVRUser(area, out error);
        }
        
        public static Directives LoadVRUser(Area area, out string error)
        {
            return LoadDirectives(GetVRUserPath(area), "Versionr", out error);
        }

        public static bool WriteVRMeta(Area area, Directives directives)
        {
            return WriteDirectives(directives, GetVRMetaPath(area));
        }

        public static bool WriteGlobalVRUser(Directives directives)
        {
            return WriteDirectives(directives, GetGlobalVRUserPath());
        }

        public static bool WriteVRUser(Area area, Directives directives)
        {
            return WriteDirectives(directives, GetVRUserPath(area));
        }

        public static Directives LoadDirectives(string path, string configName, out string error)
        {
            Directives directives = null;
            error = null;
            
            FileInfo info = new FileInfo(path);
            if (info.Exists)
            {
                string data = string.Empty;
                using (var sr = info.OpenText())
                {
                    try
                    {
                        var jr = new JsonTextReader(sr);
                        while (jr.Read())
                        {
                            if (jr.TokenType == JsonToken.PropertyName)
                            {
                                if (jr.Value.ToString() == configName)
                                {
                                    jr.Read();
                                    directives = new Directives(jr);
                                    break;
                                }
                                else
                                    jr.Skip();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Printer.PrintError(String.Format("#x#Error:## {0} is malformed!", info.Name));
                        Printer.PrintMessage(e.ToString());
                        error = String.Format("Settings file is malformed {0}\n{1}", info.FullName, e.ToString());
                        return null;
                    }
                }
            }
            else
            {
                error = String.Format("Settings file not found: {0}", info.FullName);
            }

            return directives;
        }

        public static T LoadDirectives<T>(string path, string configName, out string error)
            where T : class
        {
            T directives = default(T);
            error = null;
            
            FileInfo info = new FileInfo(path);
            if (info.Exists)
            {
                string data = string.Empty;
                using (var sr = info.OpenText())
                {
                    data = sr.ReadToEnd();
                }
                try
                {
                    JObject configuration = JObject.Parse(data);
                    var element = configuration[configName];
                    if (element != null)
                    {
                        if (typeof(T) == typeof(JObject))
                            return configuration as T;
                        directives = JsonConvert.DeserializeObject<T>(element.ToString());
                    }
                    else
                        error = String.Format("\"{0}\" element not found in {1}", configName, info.FullName);
                }
                catch (Exception e)
                {
                    Printer.PrintError(String.Format("#x#Error:## {0} is malformed!", info.Name));
                    Printer.PrintMessage(e.ToString());
                    error = String.Format("Settings file is malformed {0}\n{1}", info.FullName, e.ToString());
                }
            }
            else
            {
                error = String.Format("Settings file not found: {0}", info.FullName);
            }

            return directives;
        }

        public static bool WriteDirectives<T>(T directives, string path)
        {
            bool success = false;

            try
            {
                JsonSerializerSettings settings = new JsonSerializerSettings()
                {
                    Formatting = Formatting.Indented,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                string element = JsonConvert.SerializeObject(directives, typeof(T), settings);
                string config = String.Format("{{\n\t\"Versionr\" :\n{0}\n}}", element);

                using (StreamWriter file = File.CreateText(path))
                {
                    file.Write(config);
                    success = true;
                }
            }
            catch (Exception e)
            {
                Printer.PrintError(String.Format("#x#Error:## Failed to write to {0}", path));
                Printer.PrintMessage(e.ToString());
            }

            return success;
        }
    }
}
