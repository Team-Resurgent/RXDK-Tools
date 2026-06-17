namespace Rxdk.Xbdm;

public static class XbdmDebugConstants
{
    public const int NotificationMask = 0xffffff;
    public const uint StopThread = 0x80000000;

    public const int DmNone = 0;
    public const int DmBreak = 1;
    public const int DmDebugStr = 2;
    public const int DmExec = 3;
    public const int DmSingleStep = 4;
    public const int DmModLoad = 5;
    public const int DmModUnload = 6;
    public const int DmCreateThread = 7;
    public const int DmDestroyThread = 8;
    public const int DmException = 9;
    public const int DmAssert = 12;
    public const int DmDataBreak = 13;
    public const int DmRip = 14;
    public const int DmSectionLoad = 16;
    public const int DmSectionUnload = 17;
    public const int DmFiber = 18;
    public const int DmNotifyMax = 18;

    public const int DmnExecStop = 0;
    public const int DmnExecStart = 1;
    public const int DmnExecReboot = 2;
    public const int DmnExecPending = 3;

    public const uint DmPersistent = 1;
    public const uint DmDebugSession = 2;
    public const uint DmAsyncSession = 4;

    public const uint DmbreakNone = 0;
    public const uint DmbreakWrite = 1;
    public const uint DmbreakReadWrite = 2;
    public const uint DmbreakExecute = 3;
    public const uint DmbreakFixed = 4;

    public const uint DmstopCreateThread = 1;
    public const uint DmstopFce = 2;
    public const uint DmstopDebugStr = 4;

    public const uint DmbootWait = 1;
    public const uint DmbootWarm = 2;
    public const uint DmbootNoDebug = 4;
    public const uint DmbootStop = 8;

    public const uint DmnModflagXbe = 0x0001;
    public const uint DmnModflagTls = 0x0002;

    public const uint DmExceptNoncontinuable = 1;
    public const uint DmExceptFirstChance = 2;

    public const int ContextI386 = 0x00010000;
    public const int ContextControl = ContextI386 | 0x00000001;
    public const int ContextInteger = ContextI386 | 0x00000002;
    public const int ContextSegments = ContextI386 | 0x00000004;
    public const int ContextFloatingPoint = ContextI386 | 0x00000008;
    public const int ContextDebugRegisters = ContextI386 | 0x00000010;
    public const int ContextExtendedRegisters = ContextI386 | 0x00000020;
    public const int ContextFull = ContextControl | ContextInteger | ContextSegments;

    public const int SizeOf80387Registers = 80;
    public const int MaximumSupportedExtension = 512;
}
