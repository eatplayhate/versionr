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
                RegexPatterns = m_Patterns.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)).ToArray();
            }
        }
        public Ignores()
        {
        }
        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexPatterns { get; set; }

        internal void Merge(Ignores ignore)
        {
            if (ignore.Extensions != null)
                Extensions = Extensions.Concat(ignore.Extensions).ToArray();
            if (ignore.RegexPatterns != null)
                RegexPatterns = RegexPatterns.Concat(ignore.RegexPatterns).ToArray();
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
