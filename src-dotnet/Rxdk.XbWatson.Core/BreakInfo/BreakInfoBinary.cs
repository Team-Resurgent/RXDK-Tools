using System.Runtime.InteropServices;
using System.Text;
using Rxdk.XbWatson.Core;
using Rxdk.Xbdm;

namespace Rxdk.XbWatson.Core.BreakInfo;

public static class BreakInfoCollector
{
    public const int MaxThreads = 100;

    public static WatsonBreakInfo? Collect(
        IXbdmDebugConnection debug,
        string consoleName,
        uint threadId,
        uint eventType,
        uint eventCode,
        bool writeException,
        uint avAddress,
        string? ripText,
        Action<string>? logWarning)
    {
        var info = new WatsonBreakInfo
        {
            EventType = eventType,
            BrokenThreadId = threadId,
            EventCode = eventCode,
            WriteException = writeException,
            AvAddress = avAddress,
            RipText = ripText ?? string.Empty,
            XboxName = consoleName,
        };

        try
        {
            var xbe = debug.GetXbeInfo(string.Empty);
            info.AppName = xbe.LaunchPath;
            info.SystemTime = debug.GetSystemTime();
        }
        catch
        {
            return null;
        }

        try
        {
            var modules = debug.WalkLoadedModules();
            foreach (var mod in modules)
            {
                info.Modules.Add(new NativeModLoad
                {
                    Name = mod.Name,
                    BaseAddress = (uint)mod.BaseAddress,
                    Size = mod.Size,
                    TimeStamp = mod.TimeStamp,
                    CheckSum = mod.CheckSum,
                    Flags = mod.Flags,
                });
            }

            info.FirstSectionBase = ResolveFirstSectionBase(debug, info.AppName, modules);
        }
        catch
        {
            return null;
        }

        var threadIds = debug.GetThreadList();
        if (threadIds.Count > MaxThreads)
            logWarning?.Invoke("Warning: More than 100 threads running on the Xbox -- only the first 100 will be stored.\r\n");

        var count = Math.Min(threadIds.Count, MaxThreads);
        for (var i = 0; i < count; i++)
        {
            var tid = threadIds[i];
            var threadInfo = new WatsonThreadBreakInfo { ThreadId = tid };
            if (!TryCollectThread(debug, tid, threadInfo, logWarning))
                threadInfo.IsValid = false;
            info.Threads.Add(threadInfo);
        }

        return info;
    }

    private static bool TryCollectThread(
        IXbdmDebugConnection debug,
        uint threadId,
        WatsonThreadBreakInfo threadInfo,
        Action<string>? logWarning)
    {
        try
        {
            var context = new XbdmContext
            {
                ContextFlags = (uint)(
                    XbdmDebugConstants.ContextInteger |
                    XbdmDebugConstants.ContextControl |
                    XbdmDebugConstants.ContextFull |
                    XbdmDebugConstants.ContextFloatingPoint |
                    XbdmDebugConstants.ContextExtendedRegisters),
            };
            debug.GetThreadContext(threadId, ref context);
            threadInfo.Context = context;

            var ti = debug.GetThreadInfo(threadId);
            if (ti.TlsBase == 0)
            {
                threadInfo.IsValid = false;
                return false;
            }

            threadInfo.StackBase = context.Esp;
            threadInfo.StackSize = (uint)ti.TlsBase - context.Esp;
            if (threadInfo.StackSize == 0 || threadInfo.StackSize > 16 * 1024 * 1024)
            {
                threadInfo.IsValid = false;
                return false;
            }

            threadInfo.StackBytes = new byte[threadInfo.StackSize];
            var read = debug.GetMemory(context.Esp, threadInfo.StackBytes);
            if (read <= 0)
            {
                threadInfo.IsValid = false;
                return false;
            }

            threadInfo.IsValid = true;
            return true;
        }
        catch
        {
            logWarning?.Invoke($"Warning: Failed to get stack for thread '{threadId}'.  Thread skipped.\r\n");
            threadInfo.IsValid = false;
            return false;
        }
    }

    private static uint ResolveFirstSectionBase(
        IXbdmDebugConnection debug,
        string appLaunchPath,
        IReadOnlyList<XbdmModLoadNotification> modules)
    {
        var appFile = Path.GetFileNameWithoutExtension(appLaunchPath);
        if (string.IsNullOrEmpty(appFile))
            return 0;

        foreach (var mod in modules)
        {
            var modName = Path.GetFileNameWithoutExtension(mod.Name);
            if (!string.Equals(appFile, modName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sections = debug.WalkModuleSections(mod.Name);
            if (sections.Count == 0)
                return 0;
            return (uint)sections[0].BaseAddress;
        }

        return 0;
    }
}

public static class BreakInfoWriter
{
    private static readonly byte[] Signature = CreateSignature();

    private static byte[] CreateSignature()
    {
        var bytes = new byte[7];
        Encoding.ASCII.GetBytes("XBW1.0", 0, 6, bytes, 0);
        return bytes;
    }

    public static void Write(Stream stream, WatsonBreakInfo info)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Signature);

        WriteFixedString(writer, info.XboxName, 256);
        var sys = NativeSystemTime.FromDateTime(info.SystemTime);
        WriteStruct(writer, sys);
        WriteFixedString(writer, info.AppName, 260);

        writer.Write(info.EventType);
        writer.Write(info.BrokenThreadId);

        if (info.EventType == WatsonDialogIds.Rip)
        {
            WriteFixedString(writer, info.RipText, 1024);
        }
        else
        {
            writer.Write(info.EventCode);
            writer.Write(info.WriteException);
            writer.Write(info.AvAddress);
        }

        writer.Write((uint)info.Modules.Count);
        foreach (var mod in info.Modules)
            WriteStruct(writer, mod);

        writer.Write((uint)info.Threads.Count);
        foreach (var thread in info.Threads)
        {
            if (!thread.IsValid)
                continue;

            WriteStruct(writer, thread.Context);
            writer.Write(thread.ThreadId);
            writer.Write(thread.StackBase);
            writer.Write(thread.StackSize);
            if (thread.StackBytes != null)
                writer.Write(thread.StackBytes);
        }

        writer.Write(info.FirstSectionBase);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int size)
    {
        var bytes = new byte[size];
        var encoded = Encoding.ASCII.GetBytes(value ?? string.Empty);
        Array.Copy(encoded, bytes, Math.Min(encoded.Length, size - 1));
        writer.Write(bytes);
    }

    private static void WriteStruct<T>(BinaryWriter writer, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
            writer.Write(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

public static class BreakInfoReader
{
    public static WatsonBreakInfo? Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var sig = reader.ReadBytes(7);
        if (Encoding.ASCII.GetString(sig, 0, 6) != "XBW1.0")
            return null;

        var info = new WatsonBreakInfo
        {
            XboxName = ReadFixedString(reader, 256),
            SystemTime = ReadSystemTime(reader),
            AppName = ReadFixedString(reader, 260),
            EventType = reader.ReadUInt32(),
            BrokenThreadId = reader.ReadUInt32(),
        };

        if (info.EventType == WatsonDialogIds.Rip)
            info.RipText = ReadFixedString(reader, 1024);
        else
        {
            info.EventCode = reader.ReadUInt32();
            info.WriteException = reader.ReadBoolean();
            info.AvAddress = reader.ReadUInt32();
        }

        var moduleCount = reader.ReadUInt32();
        for (var i = 0; i < moduleCount; i++)
            info.Modules.Add(ReadStruct<NativeModLoad>(reader));

        var threadCount = reader.ReadUInt32();
        for (var i = 0; i < threadCount; i++)
        {
            var thread = new WatsonThreadBreakInfo
            {
                Context = ReadStruct<XbdmContext>(reader),
                ThreadId = reader.ReadUInt32(),
                StackBase = reader.ReadUInt32(),
                StackSize = reader.ReadUInt32(),
                IsValid = true,
            };
            thread.StackBytes = reader.ReadBytes((int)thread.StackSize);
            info.Threads.Add(thread);
        }

        info.FirstSectionBase = reader.ReadUInt32();
        return info;
    }

    private static string ReadFixedString(BinaryReader reader, int size)
    {
        var bytes = reader.ReadBytes(size);
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static DateTime ReadSystemTime(BinaryReader reader)
    {
        var sys = ReadStruct<NativeSystemTime>(reader);
        return new DateTime(sys.Year, sys.Month, sys.Day, sys.Hour, sys.Minute, sys.Second, sys.Milliseconds);
    }

    private static T ReadStruct<T>(BinaryReader reader) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = reader.ReadBytes(size);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, ptr, size);
            return Marshal.PtrToStructure<T>(ptr)!;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
