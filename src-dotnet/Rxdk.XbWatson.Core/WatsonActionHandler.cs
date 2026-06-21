using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbWatson.Core;

public static class WatsonActionHandler
{
    public static void HandleAssertChoice(IXbdmDebugConnection debug, uint threadId, WatsonAssertChoice choice)
    {
        switch (choice)
        {
            case WatsonAssertChoice.Reboot:
                debug.Reboot(XbdmDebugConstants.DmbootWarm);
                return;

            case WatsonAssertChoice.Break:
            case WatsonAssertChoice.Continue:
                var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextInteger };
                debug.GetThreadContext(threadId, ref context);
                context.Eax = choice == WatsonAssertChoice.Break ? (uint)'b' : (uint)'i';
                debug.SetThreadContext(threadId, ref context);
                if (choice == WatsonAssertChoice.Continue)
                {
                    debug.ContinueThread(threadId, exception: false);
                    debug.Go();
                }
                break;
        }
    }

    public static void HandleRipChoice(IXbdmDebugConnection debug, uint threadId, WatsonRipChoice choice)
    {
        switch (choice)
        {
            case WatsonRipChoice.Reboot:
                debug.Reboot(XbdmDebugConstants.DmbootWarm);
                break;
            case WatsonRipChoice.Break:
                var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextInteger };
                debug.GetThreadContext(threadId, ref context);
                debug.SetBreakpoint(context.Eip);
                break;
            case WatsonRipChoice.Continue:
                debug.ContinueThread(threadId, exception: false);
                debug.Go();
                break;
        }
    }

    public static void HandleExceptionChoice(IXbdmDebugConnection debug, uint threadId, WatsonExceptionChoice choice)
    {
        switch (choice)
        {
            case WatsonExceptionChoice.Reboot:
                debug.Reboot(XbdmDebugConstants.DmbootWarm);
                break;
            case WatsonExceptionChoice.Continue:
                debug.ContinueThread(threadId, exception: false);
                debug.Go();
                break;
        }
    }
}
