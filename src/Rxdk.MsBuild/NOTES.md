# MSBuild C++ integration notes

I've done some reverse engineering and playing around, and I now understand fairly well
how to add new C++ toolchains and platforms to MSBuild in a clean way.

## Tasks

[Tasks](https://learn.microsoft.com/en-us/visualstudio/msbuild/task-writing?view=visualstudio)
are how MSBuild knows to build things. Think of them kind of like rules in Make. They're
typically written in C# with some XML files (`.props` and `.targets`) to glue the build
process together.

For C++ (referred to as VC from now on) tasks, you want to write a C# class that implements
`TrackedVCToolTask`. For this integration, I've written `RxdkToolTask` (for general tasks)
and `ZigToolTask` (for Zig subcommands like `cc` or `ld`), and you should use those instead.
They make writing tasks a decent bit simpler, at least compared to what the raw decompiled
version looks like. The tasks in this are largely based on reverse engineered Clang and Linux
remote build tasks.

### C# code

Tasks look something like this:
```csharp
using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;

namespace Rxdk.MsBuild.Tasks
{
    public class MyCoolTask : RxdkToolTask
    {
        public MyCoolTask()
        {
            // here, you list your properties
            switchOrderList = new ArrayList()
            {
                "MySwitch1",
                "MySwitch2"
            };

            // you can also add a regex for matching errors
            // errorListRegexList.Add(new Regex("blah"));
        }

        // this may depend on your particular task
        protected override string TrackerIntermediateDirectory => TrackerLogDirectory;
        protected override ArrayList SwitchOrderList => switchOrderList;
        protected ArrayList switchOrderList;

        // this is used as a fallback when the executable name isn't overriden
        protected override string ToolName = "mycoolthing.exe";

        // now, you define the properties you listed in the constructor like so
        // (I'm still looking for ways to simplify this further)

        public virtual bool MySwitch1
        {
            get => PropertyOrNull<bool>();
            set
            {
                // UpdateSwitch does a lot of heavy lifting to make ToolSwitch more straightforward to use
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        // I'm genuinely not sure if these are used
                        DisplayName = "My Switch 1",
                        Description = "This does a thing.",

                        // for simple switches, you can use SwitchValue like this
                        SwitchValue = "--my-switch-1"

                        // you may also want a reverse switch
                        ReverseSwitchValue = "--no-my-switch-1"
                    },
                    value // pass the value from the setter in
                );
            }
        }

        public virtual string MySwitch2
        {
            get => PropertyOrNull<string>();
            set
            {
                // this is a more complex switch that changes depending on the string value
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "My Switch 2",
                        Description = "This does some other thing.",
                    },
                    // here, UpdateSwitch maps the value given to one of these
                    // it also sets MultipleValues to true in the ToolSwitch
                    new Dictionary<string, string>
                    {
                        { "Foo", "--my-switch-2=foo" },
                        { "Bar", "--my-switch-2=bar" },
                    },
                    value
                );
            }
        }
    }
}
```

## `.props` and `.targets` files

These tell MSBuild how to interact with your task. `.props` files generally define variables,
while `.targets` files decide how things get used. For this project, they were based on the ones
used for Clang, since the parameters are similar and they just map things from the `ClCompile` task
onto Clang.

### `.props` files

In `PropertyGroup` tags, they define general variables. In `ItemDefinitionGroup` tags, they
seem to define the default properties for tasks.

### `.targets` files

These define targets with the `Target` tag. Inside of those, they invoke tasks. They can also
load tasks with `UsingTask`. In the case of the ones for this integration, 

## `ToolSwitch`

`ToolSwitch` is a class that's part of the VC build system. It simplifies (probably?)
passing switches to tools, but without docs it's a bit confusing. They seem to be used
in generating command lines and response files for tools.

### Multi-value switches

These are used for things like `CPPLanguageStandard` to map a string to one of several correct
switches. These work by setting `MultipleValues` to true in your `ToolSwitch`, and setting
`SwitchValue` based on `ReadSwitchMap`. The version of `UpdateSwitch` taking a `Dictionary`
in `RxdkToolTask` handles this for you.

### `ToolSwitchType`

Depending on `Type`, one of the following fields stores the actual value of the switch:

- `Value` is used for `String`, `File`, and `Directory`
- `BooleanValue` is used for `Boolean`
- `StringList` is used for `StringArray` and `StringPathArray`
- `TaskItem` is used for `ITaskItem`
- `TaskItemArray` is used for `ITaskItemArray`
- `Number` is used for `Integer`

You can probably just use whichever one seems right, presumably they do some validation
depending on the type.

### Other notes

- For arrays, you can set `Separator` to control how the items are put in the command line
- I still haven't looked at what every property does, or very much at how they're used and
  interpreted outside of writing tasks

## Helpers

I wrote a few things that make tasks easier to write.

- `RxdkToolTask` has things like `PropertyOrNull`, `UpdateSwitch`, and `DumpXmlFragment`
- `ZigToolTask` has anything common to all Zig-based tasks, like `ZigCompile` and `ZigLd`
- `Generate-Targets.ps1` calls `DumpXmlFragment` for the given task to produce a scaffold
  you can base the task's definition in the `.targets` file on
