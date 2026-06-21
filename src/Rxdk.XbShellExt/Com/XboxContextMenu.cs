using System.Runtime.InteropServices;

using Rxdk.XbShellExt.Interop;

using Rxdk.XbShellExt.Shell;

using Rxdk.XbShellExt.Ui;

using Rxdk.XbNeighborhood.Core.Models;

using Rxdk.Xbdm.KitServices.Services;



namespace Rxdk.XbShellExt.Com;



internal sealed class XboxContextMenu : IContextMenu, IShellExtInit, IObjectWithSite

{

    private readonly string _folderPath;

    private readonly IReadOnlyList<string> _selectionPaths;

    private readonly nint _ownerHwnd;

    private readonly nint _folderPidl;

    private nint _site;



    public XboxContextMenu(string folderPath, IReadOnlyList<string> selectionPaths, nint ownerHwnd, nint folderPidl)

    {

        _folderPath = folderPath;

        _selectionPaths = selectionPaths;

        _ownerHwnd = ownerHwnd;

        _folderPidl = folderPidl;

    }



    public int Initialize(nint pidlFolder, nint pdtobj, nint hkeyProgId) => HResults.Ok;



    public int SetSite(nint pUnkSite)

    {

        _site = pUnkSite;

        return HResults.Ok;

    }



    public int GetSite(ref Guid riid, out nint ppvSite)

    {

        ppvSite = 0;

        if (_site == 0)

            return HResults.NoObject;



        return Marshal.QueryInterface(_site, in riid, out ppvSite);

    }



    public int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)

    {

        var nextId = idCmdFirst;

        var defaultId = uint.MaxValue;



        if (ShouldOfferExplore())

        {

            NativeMenu.AppendMenu(hMenu, NativeMenu.MfString, nextId, "&Open");

            defaultId = nextId;

            nextId++;

        }



        if (_selectionPaths.Count > 0)

        {

            NativeMenu.AppendMenu(hMenu, NativeMenu.MfString, nextId, "&Properties");

            if (defaultId == uint.MaxValue)

                defaultId = nextId;

            nextId++;

        }



        if (IsAddConsoleSelection())

        {

            NativeMenu.AppendMenu(hMenu, NativeMenu.MfString, nextId, "Add &Xbox...");

            defaultId = nextId;

            nextId++;

        }



        if (IsConsoleOnlySelection())

        {

            NativeMenu.AppendMenu(hMenu, NativeMenu.MfString, nextId, "&Security");

            nextId++;

        }



        if (defaultId != uint.MaxValue)

            NativeMenu.SetMenuDefaultItem(hMenu, defaultId, false);



        return (int)(nextId - idCmdFirst);

    }



    public int InvokeCommand(ref CmInvokeCommandInfo pici)

    {

        var verb = ResolveVerb(pici.lpVerb);

        if (string.IsNullOrEmpty(verb) ||

            string.Equals(verb, "explore", StringComparison.OrdinalIgnoreCase) ||

            string.Equals(verb, "open", StringComparison.OrdinalIgnoreCase))

        {

            return InvokeExplore();

        }



        switch (verb.ToLowerInvariant())

        {

            case "properties":

                ShellUiHost.ShowProperties(_ownerHwnd, BuildPropertyRequest());

                break;

            case "addxbox":

            case "newconsole":

                ShellUiHost.RunAddConsoleWizard(_ownerHwnd);

                break;

            case "security":

                ShellUiHost.ShowProperties(_ownerHwnd, BuildSecurityRequest(), "Security");

                break;

        }



        return HResults.Ok;

    }



    public int GetCommandString(nuint idCmd, uint uType, nint pReserved, StringBuilder pszName, uint cchMax)

    {

        if (!TryGetVerbForOffset((uint)idCmd, out var verb))

            return HResults.InvalidArg;



        switch (uType)

        {

            case ShellConstants.GcsVerba:

                if (cchMax <= verb.Length)

                    return HResults.False;

                pszName.Clear();

                pszName.Append(verb);

                return HResults.Ok;



            case ShellConstants.GcsVerbw:

                if (cchMax <= verb.Length)

                    return HResults.False;

                pszName.Clear();

                pszName.Append(verb);

                return HResults.Ok;



            case ShellConstants.GcsValidateA:

            case ShellConstants.GcsValidateW:

                return HResults.Ok;



            default:

                return HResults.NotImpl;

        }

    }



    private int InvokeExplore()

    {

        if (IsAddConsoleSelection())

        {

            ShellUiHost.RunAddConsoleWizard(_ownerHwnd);

            return HResults.Ok;

        }



        if (_selectionPaths.Count != 1)

            return HResults.Ok;



        var item = XboxShellItemFactory.FromPath(_selectionPaths[0]);

        if (!item.IsDirectory)

            return HResults.Ok;



        var relativePidl = PidlHelper.CreateSimple(item.Segment);

        try

        {

            var browser = TryGetShellBrowser();

            if (browser != null)

            {

                return browser.BrowseObject(

                    relativePidl,

                    ShellBrowserFlags.SbspDefBrowser | ShellBrowserFlags.SbspOpenMode | ShellBrowserFlags.SbspRelative);

            }



            if (_folderPidl == 0)

                return HResults.Ok;



            var absolutePidl = PidlHelper.Concatenate(_folderPidl, relativePidl);

            try

            {

                return ShellExecuteHelper.OpenPidl(_ownerHwnd, absolutePidl)

                    ? HResults.Ok

                    : HResults.NoObject;

            }

            finally

            {

                PidlHelper.Free(absolutePidl);

            }

        }

        finally

        {

            PidlHelper.Free(relativePidl);

        }

    }



    private IShellBrowser? TryGetShellBrowser()

    {

        if (_site == 0)

            return null;



        try

        {

            var serviceProvider = (IShellServiceProvider)Marshal.GetObjectForIUnknown(_site);

            var serviceId = ShellServiceIds.ShellBrowser;

            var browserId = ShellBrowserGuids.IShellBrowser;

            if (serviceProvider.QueryService(ref serviceId, ref browserId, out var browserPtr) < 0 || browserPtr == 0)

                return null;



            try

            {

                return (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);

            }

            finally

            {

                Marshal.Release(browserPtr);

            }

        }

        catch

        {

            return null;

        }

    }



    private bool TryGetVerbForOffset(uint offset, out string verb)

    {

        var index = 0u;

        if (ShouldOfferExplore())

        {

            if (offset == index)

            {

                verb = "open";

                return true;

            }



            index++;

        }



        if (_selectionPaths.Count > 0)

        {

            if (offset == index)

            {

                verb = "properties";

                return true;

            }



            index++;

        }



        if (IsAddConsoleSelection())

        {

            if (offset == index)

            {

                verb = "open";

                return true;

            }



            index++;

        }



        if (IsConsoleOnlySelection())

        {

            if (offset == index)

            {

                verb = "security";

                return true;

            }

        }



        verb = string.Empty;

        return false;

    }



    private string ResolveVerb(nint lpVerb)

    {

        if (lpVerb == ShellConstants.CmInvokeDefaultCommand)

            return "open";



        if ((lpVerb.ToInt64() & unchecked((nint)0xFFFF0000)) == 0)

        {

            if (TryGetVerbForOffset((uint)lpVerb.ToInt64(), out var verb))

                return verb;



            return string.Empty;

        }



        return Marshal.PtrToStringAnsi(lpVerb) ?? string.Empty;

    }



    private bool ShouldOfferExplore()

    {

        if (_selectionPaths.Count != 1)

            return false;



        if (IsAddConsoleSelection())

            return true;



        return XboxShellItemFactory.FromPath(_selectionPaths[0]).IsDirectory;

    }



    private bool IsAddConsoleSelection() =>

        _selectionPaths.Count == 1 &&

        string.Equals(_selectionPaths[0], ShellConstants.AddConsoleSegment, StringComparison.Ordinal);



    private bool IsConsoleOnlySelection()

    {

        if (_selectionPaths.Count != 1)

            return false;



        return XboxShellItemFactory.FromPath(_selectionPaths[0]).Kind == XboxItemKind.Console;

    }



    private Rxdk.XbNeighborhood.Core.Services.PropertyRequest? BuildPropertyRequest() =>
        ShellPropertyRequestBuilder.BuildPropertyRequest(_folderPath, _selectionPaths);

    private Rxdk.XbNeighborhood.Core.Services.PropertyRequest? BuildSecurityRequest() =>
        _selectionPaths.Count == 1
            ? ShellPropertyRequestBuilder.BuildSecurityRequest(_selectionPaths[0])
            : null;

}



internal static class ShellExecuteHelper

{

    public static bool OpenPidl(nint ownerHwnd, nint absolutePidl)

    {

        var verb = Marshal.StringToHGlobalUni("open");

        try

        {

            var info = new ShellExecuteInfo

            {

                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),

                fMask = NativeMethods.SeeMaskIdList,

                hwnd = ownerHwnd,

                lpVerb = verb,

                nShow = 1,

                lpIDList = absolutePidl,

            };



            return NativeMethods.ShellExecuteExW(ref info);

        }

        finally

        {

            Marshal.FreeHGlobal(verb);

        }

    }

}



internal static class NativeMenu

{

    public const uint MfString = 0;



    [DllImport("user32.dll", CharSet = CharSet.Unicode)]

    public static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIdNewItem, string lpNewItem);



    [DllImport("user32.dll")]

    public static extern bool SetMenuDefaultItem(nint hMenu, uint uItem, bool fByPos);

}


