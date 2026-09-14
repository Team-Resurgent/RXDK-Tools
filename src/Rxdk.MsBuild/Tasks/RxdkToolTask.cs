using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Rxdk.MsBuild.Tasks
{
    public abstract class RxdkToolTask : TrackedVCToolTask
    {
        protected RxdkToolTask()
            : base(new ResourceManager("Microsoft.Build.CPPTasks.Strings", Assembly.GetAssembly(typeof(TrackedVCToolTask))))
        {
        }
        protected override ArrayList SwitchOrderList => switchOrderList;
        protected ArrayList switchOrderList;

        protected override string TrackerIntermediateDirectory => TrackerLogDirectory ?? "";

        public virtual string TrackerLogDirectory
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Directory)
                    {
                        DisplayName = "Tracker Log Directory",
                        Description = "Tracker Log Directory.",
                    },
                    value
                );
            }
        }

        public string GetRXDKRoot()
        {
            var root = Environment.GetEnvironmentVariable("RXDK");
            if (string.IsNullOrEmpty(root))
            {
                FatalError("The RXDK environment variable is not set, did install correctly?");
                return null;
            }

            return root;
        }

        protected void FatalError(string msg)
        {
            PrintMessage(
                new MessageStruct()
                {
                    Text = msg,
                    Category = "fatal error"
                },
                MessageImportance.High
            );
            Cancel();
        }

        protected override string GenerateFullPathToTool()
        {
            if (Path.IsPathRooted(ToolName))
            {
                return ToolName;
            }

            var rxdk = GetRXDKRoot();
            return $"{rxdk}\\tools\\{ToolName}";
        }

        protected string ReadSwitchMap(string propertyName, IDictionary<string, string> switchMap, string value)
        {
#if DEBUG
            // values dont matter for a dump
            if (beingDumped && !switchMap.ContainsKey(value))
            {
                return "";
            }
#endif

            return ReadSwitchMap(propertyName, switchMap.Select(kv => new[] { kv.Key, kv.Value }).ToArray(), value);
        }

        protected string JoinSwitches(string[] switches)
        {
            return string.Join(" ", switches);
        }

        /// <summary>
        /// Get a property's value, or null if it's not set
        /// </summary>
        private object PropertyOrNull(string name)
        {
            // return nothing if the property is unset
            if (!IsPropertySet(name))
            {
                return null;
            }

            // get the switch
            var toolSwitch = ActiveToolSwitches[name];
            switch (toolSwitch.Type)
            {
                case ToolSwitchType.Boolean:
                    return toolSwitch.BooleanValue;
                case ToolSwitchType.String:
                case ToolSwitchType.File:
                case ToolSwitchType.Directory:
                    return toolSwitch.Value;
                case ToolSwitchType.StringArray:
                case ToolSwitchType.StringPathArray:
                    return toolSwitch.StringList;
                case ToolSwitchType.ITaskItem:
                    return toolSwitch.TaskItem;
                case ToolSwitchType.ITaskItemArray:
                    return toolSwitch.TaskItemArray;
                case ToolSwitchType.Integer:
                    return toolSwitch.Number;
            }

            return null;
        }

        /// <summary>
        /// Get a property as a certain type
        /// </summary>
        protected T PropertyOrNull<T>([CallerMemberName] string name = null)
        {
            return (T)PropertyOrNull(name);
        }

        protected void UpdateSwitch(ToolSwitch toolSwitch, object value = null, [CallerMemberName] string name = null)
        {
            // wont even get emitted anyway; skip a bad cast
            if (toolSwitch.Type == ToolSwitchType.Integer && !toolSwitch.IsValid)
            {
                return;
            }

            // set name and value
            toolSwitch.Name = name;
            // set the right field based on type
            switch (toolSwitch.Type)
            {
                case ToolSwitchType.Boolean:
                    toolSwitch.BooleanValue = (bool)value;
                    break;
                case ToolSwitchType.String:
                case ToolSwitchType.File:
                default:
                    toolSwitch.Value = (string)value;
                    break;
                case ToolSwitchType.Directory:
                    toolSwitch.Value = EnsureTrailingSlash((string)value);
                    break;
                case ToolSwitchType.StringArray:
                case ToolSwitchType.StringPathArray:
                    toolSwitch.StringList = (string[])value;
                    break;
                case ToolSwitchType.ITaskItem:
                    toolSwitch.TaskItem = (ITaskItem)value;
                    break;
                case ToolSwitchType.ITaskItemArray:
                    toolSwitch.TaskItemArray = (ITaskItem[])value;
                    break;
                case ToolSwitchType.Integer:
                    toolSwitch.Number = (int)value;
                    break;
                case ToolSwitchType.AlwaysAppend:
                    break;
            }

            // replace the switch and add it to the active values
            ActiveToolSwitches[name] = toolSwitch;
            AddActiveSwitchToolValue(toolSwitch);

#if DEBUG
            // dont do a repeat dump
            if (beingDumped && !toolSwitch.MultipleValues)
            {
                DumpLangProperty(toolSwitch, new Dictionary<string, string> { });
                return;
            }
#endif
        }

        protected void UpdateSwitch(ToolSwitch toolSwitch, Dictionary<string, string> switchMap, string value, [CallerMemberName] string name = null)
        {
            // set switch value and indicate that it's a multivalue
            toolSwitch.SwitchValue = ReadSwitchMap(name, switchMap, value);
            toolSwitch.MultipleValues = true;

            UpdateSwitch(toolSwitch, value, name);

#if DEBUG
            // dump specially with the switch map
            if (beingDumped)
            {
                DumpLangProperty(toolSwitch, switchMap);
            }
#endif
        }

        protected override void GenerateCommandsAccordingToType(CommandLineBuilder builder, ToolSwitch toolSwitch, CommandLineFormat format = CommandLineFormat.ForBuildLog, EscapeFormat escapeFormat = EscapeFormat.Default)
        {
            try
            {
                // whole override is because the base handles these in a different way than is useful
                if (toolSwitch.Type == ToolSwitchType.ITaskItem && !string.IsNullOrEmpty(toolSwitch.SwitchValue))
                {
                    if (!string.IsNullOrEmpty(toolSwitch.TaskItem.ItemSpec))
                    {
                        builder.AppendSwitchIfNotNull(toolSwitch.SwitchValue, Environment.ExpandEnvironmentVariables(toolSwitch.TaskItem.ItemSpec + toolSwitch.Separator));
                        return;
                    }
                }

                base.GenerateCommandsAccordingToType(builder, toolSwitch, format, escapeFormat);
            }
            catch (Exception ex)
            {
                base.Log.LogErrorFromResources("GenerateCommandLineError", toolSwitch.Name, toolSwitch.ValueAsString, ex.Message);
                //ex.RethrowIfCritical();
            }
        }

#if DEBUG
        /// <summary>
        /// Custom XML printer to match MSBuild stuff more closely
        /// </summary>
        /// <param name="name">Element name</param>
        /// <param name="attributes">Element attributes</param>
        /// <param name="printBody">An optional function that prints a body</param>
        /// <param name="initialPad">How far to indent the element</param>
        protected static void PrintXmlElement(string name, Dictionary<string, string> attributes, Action<int> printBody = null, int initialPad = 0)
        {

            var indent = new string(' ', initialPad);
            var start = $"{indent}<{name} ";
            Console.Write(start);
            var pad = new string(' ', start.Length);
            bool first = true;
            foreach (var kv in attributes)
            {
                var currentPad = first ? "" : $"\n{pad}";
                Console.Write($"{currentPad}{kv.Key}=\"{kv.Value}\"");
                first = false;
            }

            if (printBody != null)
            {
                Console.WriteLine(" >");
                printBody.Invoke(initialPad + 4);
                Console.WriteLine($"{indent}</{name}>");
            }
            else
            {
                Console.WriteLine(" />");
            }
        }

        /// <summary>
        /// Dump an XML fragment to expedite writing .targets files
        /// </summary>
        public static void DumpTargetsFragment<T>(string parent = null)
            where T : RxdkToolTask, new()
        {
            var temp = new T();
            temp.beingDumped = true;

            var attribs = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(parent)) { attribs["Condition"] = $"'@({parent}) != ''"; }
            foreach (string prop in temp.switchOrderList)
            {
                attribs[prop] = !string.IsNullOrEmpty(parent) ? $"%({parent}.{prop})" : "";
            }
            PrintXmlElement(typeof(T).Name, attribs);
        }

        public struct LangFragmentSettings
        {
            public string RuleName { get; set; }
            public string RuleDisplayName { get; set; }
            public string SwitchPrefix => "-";
        }

        LangFragmentSettings dumpSettings;
        private bool beingDumped = false;
        private int indent = 0;

        protected string RemoveSwitchPrefix(string switchValue)
        {
            if (switchValue.StartsWith(dumpSettings.SwitchPrefix))
            {
                return switchValue.Remove(0, dumpSettings.SwitchPrefix.Length);
            }
            return switchValue;
        }

        protected void DumpLangProperty(ToolSwitch toolSwitch, Dictionary<string, string> switchMap)
        {
            var attribs = new Dictionary<string, string>
                {
                    {"Name", toolSwitch.Name},
                    {"DisplayName", toolSwitch.DisplayName},
                    {"Description", toolSwitch.Description},
                };
            var type = "String";
            Action<int> printBody = null;
            if (toolSwitch.MultipleValues)
            {
                type = "Enum";
                printBody = (int pad) =>
                {
                    var valueAttribs = new Dictionary<string, string>();
                    foreach (var kv in switchMap)
                    {
                        valueAttribs["Name"] = kv.Key;
                        var switchValue = RemoveSwitchPrefix(kv.Value);
                        if (switchValue.Length > 0)
                        {
                            valueAttribs["Switch"] = switchValue;
                        }
                        PrintXmlElement("EnumValue", valueAttribs, initialPad: pad);
                    }
                };
            }
            else
            {
                var switchValue = RemoveSwitchPrefix(toolSwitch.SwitchValue);
                if (switchValue.Length > 0)
                {
                    attribs["Switch"] = switchValue;
                }
                switch (toolSwitch.Type)
                {
                    case ToolSwitchType.Boolean:
                        type = "Bool";
                        break;
                    case ToolSwitchType.StringArray:
                        type = "StringList";
                        break;
                }
            }

            PrintXmlElement($"{type}Property", attribs, printBody, indent);
        }

        /// <summary>
        /// Dump an XML scaffold for <LangID>/<task>.xml files
        /// </summary>
        public static void DumpLangScaffold<T>(LangFragmentSettings settings)
            where T : RxdkToolTask, new()
        {
            var temp = new T();
            temp.beingDumped = true;
            temp.dumpSettings = settings;

            Console.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            PrintXmlElement("Rule", new Dictionary<string, string>()
                {
                    {"Name", settings.RuleName},
                    {"DisplayName", settings.RuleDisplayName},
                    {"SwitchPrefix", settings.SwitchPrefix},
                    {"PageTemplate", "tool"},
                    {"xmlns", "http://schemas.microsoft.com/build/2009/properties"},
                    {"xmlns:x", "http://schemas.microsoft.com/winfx/2006/xaml"},
                    {"xmlns:sys","clr-namespace:System;assembly=mscorlib" },
                },
                (int pad) =>
                {
                    temp.indent = pad;
                    foreach (string propertyName in temp.switchOrderList)
                    {
                        var property = temp.GetType().GetProperty(propertyName);
                        if (property != null)
                        {
                            // trigger a call to UpdateSwitch, which calls DumpLangFragment because beingDumped is true
                            //
                            // i admit this a jank way to do it, ideally in the future it will be the other way around
                            // and the classes can be generated from the lang file. i just wanted to get it working.
                            // this is also only to accelerate something i could hand-type anyway.
                            var type = property.PropertyType;
                            object tempObj = null;
                            if (type == typeof(string))
                            {
                                tempObj = "";
                            }
                            else if (type.IsArray)
                            {
                                tempObj = new string[1];
                            }
                            else
                            {
                                tempObj = Activator.CreateInstance(type);
                            }

                            try
                            {
                                property.SetValue(temp, tempObj);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                    }
                }
            );
        }
#endif
    }
}
