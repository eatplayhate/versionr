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
        public static string[] ReadStringArray(JsonReader jr)
        {
            List<string> results = new List<string>();
            while (jr.Read())
            {
                if (jr.TokenType == JsonToken.String)
                    results.Add(jr.Value.ToString());
                else if (jr.TokenType == JsonToken.EndArray)
                    return results.ToArray();
                else
                    throw new Exception();
            }
            throw new Exception();
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
                m_Extensions = value.Select(x => x.ToLowerInvariant()).ToArray();
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
        public string UserName
        {
            get { return (String.IsNullOrEmpty(m_UserName)) ? Environment.UserName : m_UserName; }
            set { m_UserName = value; }
        }

        public Directives()
        {
            Ignore = new Ignores();
            Externals = new Dictionary<string, Extern>();
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
                            default:
                                throw new Exception();
                        }
                        break;
                    case JsonToken.Boolean:
                        if (currentProperty == "NonBlockingDiff")
                            NonBlockingDiff = System.Boolean.Parse(reader.Value.ToString());
                        else if (currentProperty == "UseTortoiseMerge")
                            UseTortoiseMerge = System.Boolean.Parse(reader.Value.ToString());
                        else
                            throw new Exception();
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
                        break;
                    default:
                        throw new Exception();
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

            if (other.Externals != null)
            {
                if (Externals == null)
                    Externals = new Dictionary<string, Extern>();
                foreach (var x in other.Externals)
                    Externals[x.Key] = x.Value;
            }
        }
    }
}
