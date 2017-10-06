using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
    public class JSONHelper
    {
        public static List<string> ReadStringList(JsonReader jr)
        {
            List<string> results = new List<string>();
            while (jr.Read())
            {
                if (jr.TokenType == JsonToken.String)
                    results.Add(jr.Value.ToString());
                else if (jr.TokenType == JsonToken.EndArray)
                    return results;
                else
                    throw new Exception();
            }
            throw new Exception();
        }
        public static string[] ReadStringArray(JsonReader jr)
        {
            return ReadStringList(jr).ToArray();
        }
    }
    public class Extern
    {
        public string Location { get; set; }
        public string Host { get; set; }
        public string PartialPath { get; set; }
        public string Branch { get; set; }
        public string Target { get; set; }
        public Extern()
        {
        }
        public Extern(JsonReader reader)
        {
            string currentProperty = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        currentProperty = reader.Value.ToString();
                        break;
                    case JsonToken.String:
                        if (currentProperty == "Location")
                            Location = reader.Value.ToString();
                        else if (currentProperty == "Host")
                            Host = reader.Value.ToString();
                        else if (currentProperty == "PartialPath")
                            PartialPath = reader.Value.ToString();
                        else if (currentProperty == "Branch")
                            Branch = reader.Value.ToString();
                        else if (currentProperty == "Target")
                            Target = reader.Value.ToString();
                        else
                            throw new Exception();
                        break;
                    case JsonToken.EndObject:
                        return;
                    default:
                        throw new Exception();
                }
            }
            throw new Exception();
        }
    }
    public class Ignores
    {
        private string[] m_Patterns;
        private string[] m_DirectoryPatterns;
        private string[] m_Directories;
        private string[] m_FilePatterns;
        private string[] m_Extensions;
        public string[] Extensions
        {
            get
            {
                return m_Extensions;
            }
            set
            {
                m_Extensions = value?.Select(x => x.ToLowerInvariant()).ToArray();
            }
        }
        public string[] Patterns
        {
            get
            {
                return m_Patterns;
            }
            set
            {
                m_Patterns = value;
                if (m_Patterns == null)
                    return;
                var regexes = m_Patterns.Select(x => new System.Text.RegularExpressions.Regex(x.ToLowerInvariant(), System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();
                if (RegexFilePatterns != null)
                    RegexFilePatterns = RegexFilePatterns.Concat(regexes).ToArray();
                else
                    RegexFilePatterns = regexes;
                if (RegexDirectoryPatterns != null)
                    RegexDirectoryPatterns = RegexDirectoryPatterns.Concat(regexes).ToArray();
                else
                    RegexDirectoryPatterns = regexes;
            }
        }
        public string[] Directories
        {
            get
            {
                return m_Directories;
            }
            set
            {
                m_Directories = value;
            }
        }
        public string[] FilePatterns
        {
            get
            {
                return m_FilePatterns;
            }
            set
            {
                m_FilePatterns = value;
                if (m_FilePatterns == null)
                    return;
                var regexes = m_FilePatterns.Select(x => new System.Text.RegularExpressions.Regex(x.ToLowerInvariant(), System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();

                if (RegexFilePatterns != null)
                    RegexFilePatterns = RegexFilePatterns.Concat(regexes).ToArray();
                else
                    RegexFilePatterns = regexes;
            }
        }
        public string[] DirectoryPatterns
        {
            get
            {
                return m_DirectoryPatterns;
            }
            set
            {
                m_DirectoryPatterns = value;
                if (m_DirectoryPatterns == null)
                    return;
                var regexes = m_DirectoryPatterns.Select(x => new System.Text.RegularExpressions.Regex(x.ToLowerInvariant(), System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();

                if (RegexDirectoryPatterns != null)
                    RegexDirectoryPatterns = RegexDirectoryPatterns.Concat(regexes).ToArray();
                else
                    RegexDirectoryPatterns = regexes;
            }
        }
        public Ignores()
        {
            m_Directories = new string[0];
        }

        public Ignores(JsonReader reader)
        {
            m_Directories = new string[0];
            string currentProperty = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        currentProperty = reader.Value.ToString();
                        break;
                    case JsonToken.StartArray:
                        if (currentProperty == "Patterns")
                            Patterns = JSONHelper.ReadStringArray(reader);
                        else if (currentProperty == "DirectoryPatterns")
                            DirectoryPatterns = JSONHelper.ReadStringArray(reader);
                        else if (currentProperty == "Directories")
                            Directories = JSONHelper.ReadStringArray(reader);
                        else if (currentProperty == "FilePatterns")
                            FilePatterns = JSONHelper.ReadStringArray(reader);
                        else if (currentProperty == "Extensions")
                            Extensions = JSONHelper.ReadStringArray(reader);
                        else
                            throw new Exception();
                        break;
                    case JsonToken.EndObject:
                        return;
                    default:
                        throw new Exception();
                }
            }
            throw new Exception();
        }

        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexFilePatterns { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexDirectoryPatterns { get; set; }

        private static T[] SafeMerge<T>(T[] a, T[] b)
        {
            if (a != null && b != null)
                return a.Concat(b).ToArray();
            else if (a != null)
                return a;
            else
                return b;
        }

        internal void Merge(Ignores ignore)
        {
            Extensions = SafeMerge(Extensions, ignore.Extensions);
            RegexFilePatterns = SafeMerge(RegexFilePatterns, ignore.RegexFilePatterns);
            RegexDirectoryPatterns = SafeMerge(RegexDirectoryPatterns, ignore.RegexDirectoryPatterns);
            Directories = SafeMerge(Directories, ignore.Directories);
        }
    }

	public class SvnCompatibility
	{
		public string[] SymlinkPatterns
		{
			set
			{
                var patterns = value.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();
                if (Utilities.SvnIntegration.SymlinkPatterns != null)
                    Utilities.SvnIntegration.SymlinkPatterns = Utilities.SvnIntegration.SymlinkPatterns.Concat(patterns).ToArray();
                else
                    Utilities.SvnIntegration.SymlinkPatterns = patterns;
            }
        }
        public SvnCompatibility()
        {

        }
        public SvnCompatibility(JsonReader reader)
        {
            string currentProperty = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        currentProperty = reader.Value.ToString();
                        break;
                    case JsonToken.StartArray:
                        if (currentProperty == "SymlinkPatterns")
                            SymlinkPatterns = JSONHelper.ReadStringArray(reader);
                        else
                            throw new Exception();
                        break;
                    case JsonToken.EndObject:
                        return;
                    default:
                        throw new Exception();
                }
            }
            throw new Exception();
        }
    }

    public class TagPreset
    {
        public string Tag { get; set; }
        public string Description { get; set; }
        public TagPreset(JsonReader reader)
        {
            string currentProperty = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        currentProperty = reader.Value.ToString();
                        break;
                    case JsonToken.String:
                        if (currentProperty == "Tag")
                            Tag = reader.Value.ToString();
                        else if (currentProperty == "Description")
                            Description = reader.Value.ToString();
                        else
                            throw new Exception();
                        break;
                    case JsonToken.EndObject:
                        return;
                    default:
                        throw new Exception();
                }
            }
            throw new Exception();
        }
    }

    public class Directives
    {
        public Ignores Ignore { get; set; }
        public Ignores Include { get; set; }
        public string DefaultCompression { get; set; }
        public string ExternalDiff { get; set; }
        public bool? NonBlockingDiff { get; set; }
        public bool? UseTortoiseMerge { get; set; }
        public string ExternalMerge { get; set; }
        public string ExternalMerge2Way { get; set; }
        public SvnCompatibility Svn { get; set; }
        public Dictionary<string, Extern> Externals { get; set; }
        private string m_UserName;
        public Newtonsoft.Json.Linq.JArray Hooks { get; set; }
        public Dictionary<string, Newtonsoft.Json.Linq.JToken> Tokens { get; set; } = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
        public string UserName
        {
            get { return (String.IsNullOrEmpty(m_UserName)) ? Environment.UserName : m_UserName; }
            set { m_UserName = value; }
        }
        public List<TagPreset> TagPresets { get; set; }
        public List<string> Sparse { get; set; } = new List<string>();
        public List<string> Excludes { get; set; } = new List<string>();

        public string ObjectStorePath = null;
        public Directives()
        {
            Ignore = new Ignores();
            Externals = new Dictionary<string, Extern>();
            TagPresets = new List<TagPreset>();
        }
        public Directives(JsonReader reader)
        {
            Ignore = new Ignores();
            Externals = new Dictionary<string, Extern>();
            string currentProperty = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        currentProperty = reader.Value.ToString();
                        break;
                    case JsonToken.String:
                        switch (currentProperty)
                        {
                            case "DefaultCompression":
                                DefaultCompression = reader.Value.ToString();
                                break;
                            case "ExternalMerge":
                                ExternalMerge = reader.Value.ToString();
                                break;
                            case "ExternalMerge2Way":
                                ExternalMerge2Way = reader.Value.ToString();
                                break;
                            case "ExternalDiff":
                                ExternalDiff = reader.Value.ToString();
                                break;
                            case "UserName":
                                UserName = reader.Value.ToString();
                                break;
                            case "ObjectStorePath":
                                ObjectStorePath = reader.Value.ToString();
                                break;
                            default:
                                Tokens[currentProperty] = Newtonsoft.Json.Linq.JToken.FromObject(reader.Value);
                                break;
                        }
                        break;
                    case JsonToken.Boolean:
                        if (currentProperty == "NonBlockingDiff")
                            NonBlockingDiff = System.Boolean.Parse(reader.Value.ToString());
                        else if (currentProperty == "UseTortoiseMerge")
                            UseTortoiseMerge = System.Boolean.Parse(reader.Value.ToString());
                        else
                            Tokens[currentProperty] = Newtonsoft.Json.Linq.JToken.FromObject(reader.Value);
                        break;
                    case JsonToken.EndObject:
                        return;
                    case JsonToken.StartObject:
                        if (currentProperty == "Ignore")
                            Ignore = new Ignores(reader);
                        else if (currentProperty == "Include")
                            Include = new Ignores(reader);
                        else if (currentProperty == "Svn")
                            Svn = new SvnCompatibility(reader);
                        else if (currentProperty == "Externals")
                        {
                            Externals = new Dictionary<string, Extern>();
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonToken.PropertyName)
                                    currentProperty = reader.Value.ToString();
                                else if (reader.TokenType == JsonToken.StartObject)
                                    Externals[currentProperty] = new Extern(reader);
                                else if (reader.TokenType == JsonToken.EndObject)
                                    break;
                                else
                                    throw new Exception();
                            }
                        }
                        else
                            Tokens[currentProperty] = Newtonsoft.Json.Linq.JObject.Load(reader);
                        break;
                    case JsonToken.StartArray:
                        if (currentProperty == "TagPresets")
                        {
                            TagPresets = new List<TagPreset>();
                            while(reader.Read())
                            {
                                if (reader.TokenType == JsonToken.StartObject)
                                {
                                    TagPreset tag = new TagPreset(reader);
                                    if (!String.IsNullOrEmpty(tag.Tag) &&
                                        tag.Tag[0] == '#' &&
                                        !tag.Tag.Contains(' ') &&
                                        !tag.Tag.Contains('\t'))
                                        TagPresets.Add(tag);
                                }
                                else if (reader.TokenType == JsonToken.EndArray)
                                    break;
                                else
                                    throw new Exception();
                            }
                        }
                        else if (currentProperty == "Sparse")
                        {
                            Sparse = JSONHelper.ReadStringList(reader);
                        }
                        else if (currentProperty == "Excludes")
                        {
                            Excludes = JSONHelper.ReadStringList(reader);
                        }
                        else if (currentProperty == "Hooks")
                        {
                            Hooks = Newtonsoft.Json.Linq.JArray.Load(reader);
                        }
                        else
                            Tokens[currentProperty] = Newtonsoft.Json.Linq.JArray.Load(reader);
                        break;
                    default:
                        throw new Exception("Unhandled setup in configuration file");
                        break;
                }
            }
        }
        public void Merge(Directives other)
        {
            if (Ignore != null && other.Ignore != null)
                Ignore.Merge(other.Ignore);
            else if (other.Ignore != null)
                Ignore = other.Ignore;

            if (other.UseTortoiseMerge != null)
                UseTortoiseMerge = other.UseTortoiseMerge;

            if (Include != null && other.Include != null)
                Include.Merge(other.Include);
            else if (other.Include != null)
                Include = other.Include;

            if (other.DefaultCompression != null)
                DefaultCompression = other.DefaultCompression;
            if (other.ExternalDiff != null)
                ExternalDiff = other.ExternalDiff;
            if (other.ExternalMerge != null)
                ExternalMerge = other.ExternalMerge;
            if (other.ExternalMerge2Way != null)
                ExternalMerge2Way = other.ExternalMerge2Way;
            if (other.NonBlockingDiff != null)
                NonBlockingDiff = other.NonBlockingDiff;
            if (other.m_UserName != null)
                m_UserName = other.m_UserName;
            if (!string.IsNullOrEmpty(other.ObjectStorePath))
                ObjectStorePath = other.ObjectStorePath;

            if (other.Externals != null)
            {
                if (Externals == null)
                    Externals = new Dictionary<string, Extern>();
                foreach (var x in other.Externals)
                    Externals[x.Key] = x.Value;
            }

            foreach (var x in other.Tokens)
            {
                Newtonsoft.Json.Linq.JToken prior;
                if (Tokens.TryGetValue(x.Key, out prior))
                {
                    if (prior.Type == Newtonsoft.Json.Linq.JTokenType.Array && x.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        Newtonsoft.Json.Linq.JArray array = prior as Newtonsoft.Json.Linq.JArray;
                        Newtonsoft.Json.Linq.JArray merge = x.Value as Newtonsoft.Json.Linq.JArray;
                        foreach (var y in merge)
                            array.Add(y);
                        continue;
                    }
                }
                Tokens[x.Key] = x.Value;
            }

            if (other.Hooks != null)
            {
                if (Hooks != null)
                {
                    foreach (var x in other.Hooks)
                        Hooks.Add(x);
                }
                else
                    Hooks = other.Hooks;
            }

            if (other.Sparse.Count > 0)
                Sparse = Sparse.Concat(other.Sparse).ToList();
            if (other.Excludes.Count > 0)
                Excludes = Excludes.Concat(other.Excludes).ToList();

            if (other.TagPresets != null)
            {
                if (TagPresets == null)
                    TagPresets = new List<TagPreset>();
                TagPresets.AddRange(other.TagPresets);
                TagPresets = TagPresets.Distinct().ToList();
            }
        }
    }
}
