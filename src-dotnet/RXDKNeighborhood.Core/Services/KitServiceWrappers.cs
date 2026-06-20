namespace RXDKNeighborhood.Core.Services;

using RXDKNeighborhood.Core.Models;

public class FileClipboardService : Rxdk.Xbdm.KitServices.Services.FileClipboardService;

public interface IFileOperationHost : Rxdk.Xbdm.KitServices.Services.IFileOperationHost;

public class FileOperationsService : Rxdk.Xbdm.KitServices.Services.FileOperationsService
{
    public FileOperationsService(FileClipboardService clipboard) : base(clipboard)
    {
    }
}

public class PropertySession : Rxdk.Xbdm.KitServices.Services.PropertySession
{
    public PropertySession(
        Rxdk.Xbdm.Managed.XbdmConnection connection,
        Models.PropertyContext context,
        Models.SecurityEditorState? security)
        : base(connection, context, security)
    {
    }

    internal PropertySession(
        Rxdk.Xbdm.Managed.XbdmConnection connection,
        Rxdk.Xbdm.KitServices.Models.PropertyContext context,
        Rxdk.Xbdm.KitServices.Models.SecurityEditorState? security)
        : base(connection, context, security)
    {
    }
}

public class PropertiesService : Rxdk.Xbdm.KitServices.Services.PropertiesService
{
    public PropertySession OpenProperties(PropertyRequest request)
    {
        var session = base.OpenProperties(request);
        return new PropertySession(session.Connection, session.Context, session.Security);
    }
}

public class PropertyRequest : Rxdk.Xbdm.KitServices.Services.PropertyRequest
{
    public static PropertyRequest? FromSelection(string? console, string? currentPath, IReadOnlyList<FileSelectionItem> items) =>
        FromKitRequest(Rxdk.Xbdm.KitServices.Services.PropertyRequest.FromSelection(console, currentPath, items));

    public new static PropertyRequest? FromSelection(string? console, string? currentPath, IReadOnlyList<Rxdk.Xbdm.KitServices.Models.FileSelectionItem> items) =>
        FromKitRequest(Rxdk.Xbdm.KitServices.Services.PropertyRequest.FromSelection(console, currentPath, items));

    private static PropertyRequest? FromKitRequest(Rxdk.Xbdm.KitServices.Services.PropertyRequest? request)
    {
        if (request == null)
            return null;

        return new PropertyRequest
        {
            Kind = request.Kind,
            ConsoleName = request.ConsoleName,
            FolderDisplayPath = request.FolderDisplayPath,
            Items = request.Items,
        };
    }
}

public class SecurityService : Rxdk.Xbdm.KitServices.Services.SecurityService;

public class AddConsoleService : Rxdk.Xbdm.KitServices.Services.AddConsoleService;
