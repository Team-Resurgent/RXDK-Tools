using System.Runtime.InteropServices;
using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct XbdmFloatExtendedSave
{
    public ushort ControlWord;
    public ushort StatusWord;
    public ushort TagWord;
    public ushort ErrorOpcode;
    public uint ErrorOffset;
    public uint ErrorSelector;
    public uint DataOffset;
    public uint DataSelector;
    public uint MxCsr;
    public uint Reserved2;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    public byte[] RegisterArea;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    public byte[] XmmRegisterArea;

    public XbdmFloatExtendedSave()
    {
        RegisterArea = new byte[128];
        XmmRegisterArea = new byte[128];
    }
}

internal static class XbdmExtendedContext
{
    private static readonly int ExtendedSize = Marshal.SizeOf<XbdmFloatExtendedSave>();

    internal static bool TryGetExtendedContext(XbdmProtocolSession session, uint threadId, out XbdmFloatExtendedSave xfs)
    {
        xfs = new XbdmFloatExtendedSave();
        var (hr, _) = session.SendCommandRaw($"GETEXTCONTEXT THREAD={threadId}");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            return false;

        var cb = session.ReceiveUInt32();
        session.BeginPendingBinary(cb);
        if (cb < (uint)ExtendedSize)
        {
            var partial = new byte[cb];
            session.ReceiveBinary(partial);
            return false;
        }

        var buffer = new byte[ExtendedSize];
        session.ReceiveBinary(buffer);
        xfs = BytesToStruct<XbdmFloatExtendedSave>(buffer);
        if (cb > (uint)ExtendedSize)
            session.DrainPendingBinary();
        return true;
    }

    internal static void ApplyExtendedToContext(ref XbdmContext context, XbdmFloatExtendedSave xfs, uint cr0Npx)
    {
        if ((context.ContextFlags & XbdmDebugConstants.ContextFloatingPoint) == XbdmDebugConstants.ContextFloatingPoint)
        {
            context.FloatSave.Cr0NpxState = cr0Npx;
            context.FloatSave.ControlWord = xfs.ControlWord;
            context.FloatSave.StatusWord = xfs.StatusWord;
            context.FloatSave.TagWord = xfs.TagWord;
            context.FloatSave.ErrorOffset = xfs.ErrorOffset;
            context.FloatSave.ErrorSelector = xfs.ErrorSelector;
            context.FloatSave.DataOffset = xfs.DataOffset;
            context.FloatSave.DataSelector = xfs.DataSelector;

            for (var i = 0; i < 8; i++)
                xfs.RegisterArea.AsSpan(i * 16, 10).CopyTo(context.FloatSave.RegisterArea.AsSpan(i * 10, 10));
        }

        var bytes = StructToBytes(xfs);
        bytes.CopyTo(context.ExtendedRegisters.AsSpan(0, bytes.Length));
        context.ContextFlags |= XbdmDebugConstants.ContextExtendedRegisters;
    }

    internal static void SetExtendedContext(XbdmProtocolSession session, uint threadId, ref XbdmContext context)
    {
        XbdmFloatExtendedSave xfs;
        var wantExtended = (context.ContextFlags & XbdmDebugConstants.ContextExtendedRegisters) ==
                           XbdmDebugConstants.ContextExtendedRegisters;
        var wantFp = (context.ContextFlags & XbdmDebugConstants.ContextFloatingPoint) ==
                     XbdmDebugConstants.ContextFloatingPoint;

        if (wantExtended)
        {
            xfs = BytesToStruct<XbdmFloatExtendedSave>(context.ExtendedRegisters.AsSpan(0, ExtendedSize));
        }
        else if (wantFp && TryGetExtendedContext(session, threadId, out xfs))
        {
            // Start from the console's full extended register set.
        }
        else
        {
            xfs = new XbdmFloatExtendedSave();
        }

        var command = new StringBuilder($"SETCONTEXT THREAD={threadId}");
        AppendIntegerAndControl(command, context);

        if (wantFp || wantExtended)
        {
            if (wantFp)
            {
                command.Append($" Cr0NpxState=0x{context.FloatSave.Cr0NpxState:x8}");
                xfs.ControlWord = (ushort)context.FloatSave.ControlWord;
                xfs.StatusWord = (ushort)context.FloatSave.StatusWord;
                xfs.TagWord = (ushort)context.FloatSave.TagWord;
                xfs.ErrorOffset = context.FloatSave.ErrorOffset;
                xfs.ErrorSelector = context.FloatSave.ErrorSelector;
                xfs.DataOffset = context.FloatSave.DataOffset;
                xfs.DataSelector = context.FloatSave.DataSelector;
                for (var i = 0; i < 8; i++)
                    context.FloatSave.RegisterArea.AsSpan(i * 10, 10).CopyTo(xfs.RegisterArea.AsSpan(i * 16, 10));
            }

            command.Append($" ext={ExtendedSize}");
            var (hr, _) = session.SendCommandRaw(command.ToString());
            if (hr != XbdmHResults.ReadyForBin)
                throw XbdmException.FromHResult("SETCONTEXT failed.", hr);

            session.SendBinary(StructToBytes(xfs));
            session.ReceiveStatusOrThrow("SETCONTEXT failed.");
            return;
        }

        var (statusHr, line) = session.SendCommandRaw(command.ToString());
        if (!XbdmProtocol.IsCommandSuccess(statusHr))
            throw XbdmException.FromHResult("SETCONTEXT failed.", statusHr, line);
    }

    private static void AppendIntegerAndControl(StringBuilder command, XbdmContext context)
    {
        if ((context.ContextFlags & XbdmDebugConstants.ContextControl) != 0)
        {
            command.Append(
                $" ESP=0x{context.Esp:x8} EBP=0x{context.Ebp:x8} EIP=0x{context.Eip:x8} EFLAGS=0x{context.EFlags:x8}");
        }

        if ((context.ContextFlags & XbdmDebugConstants.ContextInteger) != 0)
        {
            command.Append(
                $" EAX=0x{context.Eax:x8} EBX=0x{context.Ebx:x8} ECX=0x{context.Ecx:x8} EDX=0x{context.Edx:x8} ESI=0x{context.Esi:x8} EDI=0x{context.Edi:x8}");
        }
    }

    private static T BytesToStruct<T>(ReadOnlySpan<byte> bytes) where T : struct
    {
        var handle = GCHandle.Alloc(bytes.ToArray(), GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject())!;
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte[] StructToBytes<T>(T value) where T : struct
    {
        var bytes = new byte[Marshal.SizeOf<T>()];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            return bytes;
        }
        finally
        {
            handle.Free();
        }
    }
}
