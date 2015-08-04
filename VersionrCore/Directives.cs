﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
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
                RegexPatterns = m_Patterns.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();
            }
        }
        public Ignores()
        {
        }
        [Newtonsoft.Json.JsonIgnore]
        public System.Text.RegularExpressions.Regex[] RegexPatterns { get; set; }
    }

	public class SvnCompatibility
	{
		public string[] SymlinkPatterns
		{
			set
			{
				Utilities.SvnIntegration.SymlinkPatterns = value.Select(x => new System.Text.RegularExpressions.Regex(x, System.Text.RegularExpressions.RegexOptions.Compiled)).ToArray();
			}
		}
	}

    public class Directives
    {
        public Ignores Ignore { get; set; }
        public Ignores Include { get; set; }
        public string DefaultCompression { get; set; }
		public SvnCompatibility Svn { get; set; }
        public Directives()
        {
            Ignore = new Ignores();
        }
    }
}
