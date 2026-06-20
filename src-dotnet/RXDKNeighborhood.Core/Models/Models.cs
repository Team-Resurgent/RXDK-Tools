namespace RXDKNeighborhood.Core.Models;

public class ConsoleInfo : Rxdk.Xbdm.KitServices.Models.ConsoleInfo;

public class ConsoleRegistryData : Rxdk.Xbdm.KitServices.Models.ConsoleRegistryData;

public class FileEntryModel : Rxdk.Xbdm.KitServices.Models.FileEntryModel;

public enum FileClipboardOperation
{
    None = Rxdk.Xbdm.KitServices.Models.FileClipboardOperation.None,
    Cut = Rxdk.Xbdm.KitServices.Models.FileClipboardOperation.Cut,
    Copy = Rxdk.Xbdm.KitServices.Models.FileClipboardOperation.Copy,
}

public class FileSelection : Rxdk.Xbdm.KitServices.Models.FileSelection;

public class FileSelectionItem : Rxdk.Xbdm.KitServices.Models.FileSelectionItem;

public enum NavigationNodeKind
{
    Root = Rxdk.Xbdm.KitServices.Models.NavigationNodeKind.Root,
    Console = Rxdk.Xbdm.KitServices.Models.NavigationNodeKind.Console,
    Drive = Rxdk.Xbdm.KitServices.Models.NavigationNodeKind.Drive,
    Folder = Rxdk.Xbdm.KitServices.Models.NavigationNodeKind.Folder,
}

public class NavigationNode : Rxdk.Xbdm.KitServices.Models.NavigationNode;
