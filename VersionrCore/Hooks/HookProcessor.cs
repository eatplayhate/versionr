using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Versionr.Hooks
{
    public class HookProcessor
    {
        public List<HookListener> InstalledHooks { get; private set; } = new List<HookListener>();
        public HookProcessor(Newtonsoft.Json.Linq.JArray hooks)
        {
            if (hooks != null && hooks.Count > 0)
            {
                foreach (var x in hooks)
                {
                    HookListener listener = ConstructListener(x as JObject);
                    if (listener != null)
                        InstalledHooks.Add(listener);
                }
            }
        }
        public bool Raise(IHook hook)
        {
            foreach (var x in InstalledHooks)
            {
                if (x.EventType.IsAssignableFrom(hook.GetType()))
                {
                    if (!x.Raise(hook))
                        return false;
                }
            }
            return true;
        }

        private Type[] FindSubclassesOf(Type baseType)
        {
            List<Type> t = new List<Type>();
            foreach (var x in System.Reflection.Assembly.GetExecutingAssembly().DefinedTypes.Concat(PluginCache.Plugins.SelectMany(x => x.Assembly.DefinedTypes)))
            {
                if (baseType.IsInterface)
                {
                    if (x.GetInterface(baseType.Name) != null)
                        t.Add(x);
                }
                else
                {
                    if (x.IsSubclassOf(baseType))
                        t.Add(x);
                }
            }
            return t.ToArray();
        }

        Type[] m_HookActionTypes;
        private Type[] HookActionTypes
        {
            get
            {
                if (m_HookActionTypes == null)
                    m_HookActionTypes = FindSubclassesOf(typeof(IHookAction));

                return m_HookActionTypes;
            }
        }

        Type[] m_HookFilterTypes;
        private Type[] HookFilterTypes
        {
            get
            {
                if (m_HookFilterTypes == null)
                    m_HookFilterTypes = FindSubclassesOf(typeof(IHookFilter));

                return m_HookFilterTypes;
            }
        }

        Type[] m_HookTypes;
        private Type[] HookTypes
        {
            get
            {
                if (m_HookTypes == null)
                    m_HookTypes = FindSubclassesOf(typeof(IHook));

                return m_HookTypes;
            }
        }

        Dictionary<string, Type> m_HookFilterTypeMap;
        private Dictionary<string, Type> HookFilterTypeMap
        {
            get
            {
                if (m_HookFilterTypeMap == null)
                {
                    m_HookFilterTypeMap = new Dictionary<string, Type>();
                    foreach (var x in HookFilterTypes)
                    {
                        var eventNameProperty = x.GetProperty("FilterName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, typeof(string), new Type[0], null);
                        if (eventNameProperty != null)
                            m_HookFilterTypeMap[eventNameProperty.GetMethod.Invoke(null, null) as string] = x;
                        m_HookFilterTypeMap[x.FullName] = x;
                        m_HookFilterTypeMap[x.Name] = x;
                    }
                }
                return m_HookFilterTypeMap;
            }
        }

        Dictionary<string, Type> m_HookTypeMap;
        private Dictionary<string, Type> HookTypeMap
        {
            get
            {
                if (m_HookTypeMap == null)
                {
                    m_HookTypeMap = new Dictionary<string, Type>();
                    foreach (var x in HookTypes)
                    {
                        var eventNameProperty = x.GetProperty("EventName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, typeof(string), new Type[0], null);
                        if (eventNameProperty != null)
                            m_HookTypeMap[eventNameProperty.GetMethod.Invoke(null, null) as string] = x;
                        m_HookTypeMap[x.FullName] = x;
                        m_HookTypeMap[x.Name] = x;
                    }
                }
                return m_HookTypeMap;
            }
        }

        Dictionary<string, Type> m_HookActionTypeMap;
        private Dictionary<string, Type> HookActionTypeMap
        {
            get
            {
                if (m_HookActionTypeMap == null)
                {
                    m_HookActionTypeMap = new Dictionary<string, Type>();
                    foreach (var x in HookActionTypes)
                    {
                        var actionNameProperty = x.GetProperty("ActionName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, typeof(string), new Type[0], null);
                        if (actionNameProperty != null)
                            m_HookActionTypeMap[actionNameProperty.GetMethod.Invoke(null, null) as string] = x;
                        m_HookActionTypeMap[x.FullName] = x;
                        m_HookActionTypeMap[x.Name] = x;
                    }
                }
                return m_HookActionTypeMap;
            }
        }

        private HookListener ConstructListener(JObject x)
        {
            if (x == null)
                return null;
            try
            {
                Type hookType;
                JToken hookName;
                JToken filters;
                if (!x.TryGetValue("Event", out hookName) || hookName.Type != JTokenType.String)
                    throw new Exception("Missing/invalid hook \"Event\" property!");

                if (HookTypeMap.TryGetValue(hookName.ToString(), out hookType))
                {
                    x.TryGetValue("Filters", out filters);
                    List<KeyValuePair<string, IHookFilter>> filterList = ConstructFilters(hookName.ToString(), filters);

                    HookListener listener = new HookListener(filterList)
                    {
                        Event = hookName.ToString(),
                        EventType = hookType
                    };
                    listener.Action = ConstructHookAction(x.GetValue("Action") as JObject);
                    return listener;
                }
                else
                    throw new Exception(string.Format("Couldn't find hook type \"{0}\"!", hookName));
            }
            catch (Exception e)
            {
                Printer.PrintWarning("Error installing hook - exception: {0}\n\nHook code:\n{1}", e, x);
            }
            return null;
        }

        private List<KeyValuePair<string, IHookFilter>> ConstructFilters(string hookName, JToken filters)
        {
            List<KeyValuePair<string, IHookFilter>> results = null;
            if (filters != null && filters.Type == JTokenType.Array)
            {
                results = new List<KeyValuePair<string, IHookFilter>>();
                JArray filterArray = filters as JArray;
                foreach (var x in filterArray)
                {
                    try
                    {
                        if (x.Type != JTokenType.Object)
                            throw new Exception("Filter object is not a compound object type.");
                        string name = null;
                        string filterTypeName = null;
                        JObject filterTypeArguments = null;
                        foreach (var field in (x as JObject))
                        {
                            if (field.Key == "Name")
                            {
                                if (field.Value.Type != JTokenType.String)
                                    throw new Exception("Filter \"Name\" should be a string.");
                                name = field.Value.ToString();
                            }
                            else if (filterTypeName != null)
                                throw new Exception("Filter objects should have only a name and a single object.");
                            else
                            {
                                filterTypeName = field.Key;
                                if (field.Value.Type != JTokenType.Object)
                                    throw new Exception("Filter parameter object is not a compound object.");
                                filterTypeArguments = field.Value as JObject;
                            }
                        }
                        if (filterTypeName == null || name == null || filterTypeArguments == null)
                            throw new Exception("Filter is missing one or more of \"Name\" or the object type to install.");

                        Type filterType;
                        if (HookFilterTypeMap.TryGetValue(filterTypeName.ToString(), out filterType))
                        {
                            results.Add(new KeyValuePair<string, IHookFilter>(name, Activator.CreateInstance(filterType, filterTypeArguments) as IHookFilter));
                        }
                        else
                            throw new Exception(string.Format("Couldn't find hook filter type \"{0}\"!", filterTypeName));
                    }
                    catch (Exception e)
                    {
                        Printer.PrintWarning("Error installing hook for filter: {0} - {1}", hookName, e.ToString());
                    }
                }
            }
            else if (filters != null)
                Printer.PrintWarning("Unable to add filters to hook {0} - wrong format for filter array!", hookName);
            return results;
        }

        private IHookAction ConstructHookAction(JObject action)
        {
            if (action == null || !action.HasValues)
                throw new ArgumentNullException("action");
            Type actionType;
            JToken actionName = action.Children().First();
            if (actionName.Type != JTokenType.Property)
                throw new Exception("Action type is malformed!");
            JToken actionObject = (actionName as JProperty).Value;
            if (actionObject.Type != JTokenType.Object)
                throw new Exception("Action object is malformed!");

           if (!HookActionTypeMap.TryGetValue((actionName as JProperty).Name.ToString(), out actionType))
                throw new Exception(string.Format("Couldn't find a hook action with name \"{0}\"!", (actionName as JProperty).Name));

            return Activator.CreateInstance(actionType, actionObject as JObject) as IHookAction;
        }
    }
}
