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
                    if (!x.Action.Raise(hook))
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
                if (!x.TryGetValue("Event", out hookName) || hookName.Type != JTokenType.String)
                    throw new Exception("Missing/invalid hook \"Event\" property!");

                if (HookTypeMap.TryGetValue(hookName.ToString(), out hookType))
                {
                    HookListener listener = new HookListener()
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
