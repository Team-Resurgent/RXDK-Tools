using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class XbdmTimeCorrection
{
    private const ulong HalfHour = 4UL * 0x80000000UL + 820130816UL; // liHalfHour from filexfer.c
    private const ulong Hour = 8UL * 0x80000000UL + 1640261632UL;

    internal static void Ensure(XbdmSci sci)
    {
        if (sci.GotTimeCorrection)
            return;

        try
        {
            var remote = QuerySystemFileTime(sci);
            var local = (ulong)DateTime.UtcNow.ToFileTimeUtc();
            ApplyClockSkew(sci, local, remote);
            sci.GotTimeCorrection = true;
        }
        catch (XbdmException ex) when (ex.HResult == XbdmHResults.ClockNotSet)
        {
            TrySetSystemTime(sci);
            try
            {
                var remote = QuerySystemFileTime(sci);
                var local = (ulong)DateTime.UtcNow.ToFileTimeUtc();
                ApplyClockSkew(sci, local, remote);
                sci.GotTimeCorrection = true;
            }
            catch (XbdmException retry) when (retry.HResult == XbdmHResults.ClockNotSet)
            {
                sci.GotTimeCorrection = true;
                sci.BadSysTime = true;
            }
        }
    }

    internal static void CorrectFromConsole(XbdmSci sci, ref uint high, ref uint low)
    {
        Correct(sci, ref high, ref low, toRemote: false);
    }

    internal static void CorrectToConsole(XbdmSci sci, ref uint high, ref uint low)
    {
        Correct(sci, ref high, ref low, toRemote: true);
    }

    private static void Correct(XbdmSci sci, ref uint high, ref uint low, bool toRemote)
    {
        if (high == 0 && low == 0)
            return;

        Ensure(sci);
        if (!sci.GotTimeCorrection || sci.BadSysTime)
        {
            high = 0;
            low = 0;
            return;
        }

        var fileTime = ((ulong)high << 32) | low;
        var add = sci.AddDiff == toRemote;
        if (add)
            fileTime += sci.TimeDiff;
        else
            fileTime -= sci.TimeDiff;

        high = (uint)(fileTime >> 32);
        low = (uint)fileTime;
    }

    private static ulong QuerySystemFileTime(XbdmSci sci) =>
        sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw("SYSTIME");
            if (hr == XbdmHResults.ClockNotSet)
                throw XbdmException.FromHResult("Console clock is not set.", hr, line);
            if (!XbdmProtocol.IsCommandSuccess(hr))
                throw XbdmException.FromHResult("SYSTIME failed.", hr, line);

            if (!XbdmParamParser.TryGetDwParam(line, "high", out var high) ||
                !XbdmParamParser.TryGetDwParam(line, "low", out var low))
            {
                throw XbdmException.FromHResult("SYSTIME response was invalid.", XbdmHResults.FileError, line);
            }

            return ((ulong)high << 32) | low;
        });

    private static void TrySetSystemTime(XbdmSci sci)
    {
        var ft = DateTime.UtcNow.ToFileTimeUtc();
        var high = (uint)(ft >> 32);
        var low = (uint)ft;
        sci.WithSession(session =>
        {
            session.SendCommand($"setsystime clockhi=0x{high:x8} clocklo=0x{low:x8}");
        });
    }

    private static void ApplyClockSkew(XbdmSci sci, ulong local, ulong remote)
    {
        ulong diff;
        if (local > remote)
        {
            sci.AddDiff = false;
            diff = local - remote;
        }
        else
        {
            sci.AddDiff = true;
            diff = remote - local;
        }

        if (diff < HalfHour)
        {
            sci.TimeDiff = 0;
            return;
        }

        diff = (diff + HalfHour) / Hour * Hour;
        sci.TimeDiff = diff;
    }
}
