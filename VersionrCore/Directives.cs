using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
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
    }
    public class Ignores
    {
        private string[] m_Patterns;
        private string[] m_DirectoryPatterns;
        private string[] m_Directories;
        private string[] m_FilePatterns;
        public string[] Extensions { get; set; }
        public string[] Patterns
        {
            get
            {
                return m_Patterns;
            }
            set
            {
                m_Patterns = value;
                var regexes = m_Patterns.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)).ToArray();
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
                var regexes = m_FilePatterns.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)).ToArray();

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
                var regexes = m_DirectoryPatterns.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)).ToArray();

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
        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexFilePatterns { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexDirectoryPatterns { get; set; }

        internal void Merge(Ignores ignore)
        {
            if (ignore.Extensions != null)
                Extensions = Extensions.Concat(ignore.Extensions).ToArray();
            if (ignore.RegexFilePatterns != null)
                RegexFilePatterns = RegexFilePatterns.Concat(ignore.RegexFilePatterns).ToArray();
            if (ignore.RegexDirectoryPatterns != null)
                RegexDirectoryPatterns = RegexDirectoryPatterns.Concat(ignore.RegexDirectoryPatterns).ToArray();
            if (ignore.Directories != null)
                Directories = Directories.Concat(ignore.Directories).ToArray();
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
    }

    public class Directives
    {
        public Ignores Ignore { get; set; }
        public Ignores Include { get; set; }
        public string DefaultCompression { get; set; }
        public string ExternalDiff { get; set; }
        public bool? NonBlockingDiff { get; set; }
        public string ExternalMerge { get; set; }
        public string ExternalMerge2Way { get; set; }
        public SvnCompatibility Svn { get; set; }
        public Dictionary<string, Extern> Externals { get; set; }
        public Directives()
        {
            Ignore = new Ignores();
            Externals = new Dictionary<string, Extern>();
        }
        public void Merge(Directives other)
        {
            if (Ignore != null && other.Ignore != null)
                Ignore.Merge(other.Ignore);
            else if (other.Ignore != null)
                Ignore = other.Ignore;

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

            if (other.Externals != null)
            {
                if (Externals != null)
                    Externals = new Dictionary<string, Extern>();
                foreach (var x in other.Externals)
                    Externals[x.Key] = x.Value;
            }
        }
    }
}
