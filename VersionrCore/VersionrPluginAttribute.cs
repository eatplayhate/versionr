using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
	public class VersionrPluginAttribute : Attribute
	{
		public string Name { get; private set; }
		public Type OptionsType { get; private set; }

		public VersionrPluginAttribute(string name, Type optionsType = null)
		{
			Name = name;
			OptionsType = optionsType;
		}
	}
}
