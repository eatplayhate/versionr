using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Versionr
{
	public static class PluginCache
	{
		private static List<Plugin> s_Plugins;
		private static Dictionary<Type, List<object>> s_Implementations = new Dictionary<Type, List<object>>();
		
		public static IReadOnlyList<Plugin> Plugins
		{
			get
			{
				if (s_Plugins == null)
				{
					Printer.PrintDiagnostics("Initializing plugins");
					s_Plugins = new List<Plugin>();
					string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
					string pluginDirectory = baseDirectory;
					foreach (string filename in Directory.GetFiles(pluginDirectory, "*.dll"))
					{
						Assembly assembly = null;
						VersionrPluginAttribute pluginAttribute = null;
						try
						{
							assembly = Assembly.LoadFile(Path.Combine(pluginDirectory, filename));
							pluginAttribute = assembly.GetCustomAttribute<VersionrPluginAttribute>();
						}
						catch { }

						if (pluginAttribute != null)
						{
							Printer.PrintDiagnostics("Loaded plugin {0} at {1}", pluginAttribute.Name, assembly.FullName);
							s_Plugins.Add(new Plugin()
							{
								Assembly = assembly,
								Attributes = pluginAttribute
							});
						}
					}
				}

				return s_Plugins;
			}
		}

		/// <summary>
		/// Get a list of implementations of the given type from all plugins and the current assembly.
		/// </summary>
		public static IEnumerable<T> GetImplementations<T>()
		{
			Type key = typeof(T);
			List<object> implementations;
			if (s_Implementations.TryGetValue(key, out implementations))
				return implementations.Cast<T>();

			s_Implementations[key] = implementations = new List<object>();
			foreach (var plugin in Plugins)
			{
                try
                {
                    foreach (var type in plugin.Assembly.GetTypes())
                    {
                        if (!type.IsAbstract && !type.IsInterface && key.IsAssignableFrom(type))
                        {
                            var instance = Activator.CreateInstance(type);
                            implementations.Add(instance);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
			}
			return implementations.Cast<T>();
		}
	}
}
