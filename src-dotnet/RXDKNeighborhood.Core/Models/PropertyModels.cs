namespace RXDKNeighborhood.Core.Models;

public enum PropertyTargetKind
{
    Console = Rxdk.Xbdm.KitServices.Models.PropertyTargetKind.Console,
    Drive = Rxdk.Xbdm.KitServices.Models.PropertyTargetKind.Drive,
    File = Rxdk.Xbdm.KitServices.Models.PropertyTargetKind.File,
    Folder = Rxdk.Xbdm.KitServices.Models.PropertyTargetKind.Folder,
    MultiFile = Rxdk.Xbdm.KitServices.Models.PropertyTargetKind.MultiFile,
}

public class PropertyContext : Rxdk.Xbdm.KitServices.Models.PropertyContext;

public class ConsoleGeneralInfo : Rxdk.Xbdm.KitServices.Models.ConsoleGeneralInfo;

public class DriveGeneralInfo : Rxdk.Xbdm.KitServices.Models.DriveGeneralInfo;

public class FileGeneralInfo : Rxdk.Xbdm.KitServices.Models.FileGeneralInfo;

public class SecurityUserEntry : Rxdk.Xbdm.KitServices.Models.SecurityUserEntry;

public class SecurityEditorState : Rxdk.Xbdm.KitServices.Models.SecurityEditorState;
