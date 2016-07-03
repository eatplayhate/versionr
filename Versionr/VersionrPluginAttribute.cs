using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
	public class VersionrPluginAttribute : Attribute
	{
		public Type OptionsType { get; private set; }

		public VersionrPluginAttribute(Type optionsType)
		{
			OptionsType = optionsType;
		}
	}
}
