using RXDKNeighborhood.Core.Models;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace RXDKNeighborhood.Core.Services;

public sealed class PropertySession : IDisposable
{
    public XbdmConnection Connection { get; }
    public PropertyContext Context { get; }
    public SecurityEditorState Security { get; }

    public PropertySession(XbdmConnection connection, PropertyContext context, SecurityEditorState? security)
    {
        Connection = connection;
        Context = context;
        Security = security ?? new SecurityEditorState();
    }

    public void Dispose() => Connection.Dispose();
}

public sealed class PropertiesService
{
    public PropertySession OpenProperties(PropertyRequest request)
    {
        var conn = XbdmSession.Connect(request.ConsoleName);
        var security = request.Kind == PropertyTargetKind.Console ? LoadSecurity(conn) : null;
        var context = BuildContext(conn, request);
        return new PropertySession(conn, context, security);
    }

    private static PropertyContext BuildContext(XbdmConnection conn, PropertyRequest request)
    {
        return request.Kind switch
        {
            PropertyTargetKind.Console => BuildConsoleContext(conn, request),
            PropertyTargetKind.Drive => BuildDriveContext(conn, request),
            _ => BuildFileContext(conn, request),
        };
    }

    private static PropertyContext BuildConsoleContext(XbdmConnection conn, PropertyRequest request)
    {
        string name;
        try
        {
            name = conn.GetNameOfXbox(resolvable: false);
        }
        catch
        {
            name = request.ConsoleName;
        }

        string? runningTitle = null;
        try
        {
            runningTitle = conn.GetXbeLaunchPath();
        }
        catch
        {
            runningTitle = "Not available";
        }

        return new PropertyContext
        {
            Kind = PropertyTargetKind.Console,
            ConsoleName = request.ConsoleName,
            Caption = request.ConsoleName,
            ConsoleGeneral = new ConsoleGeneralInfo
            {
                Name = name,
                IpAddress = FormatOrNull(conn.TryResolveXboxAddress()),
                AltIpAddress = FormatOrNull(conn.TryGetAltAddress()),
                RunningTitle = runningTitle,
            },
        };
    }

    private static PropertyContext BuildDriveContext(XbdmConnection conn, PropertyRequest request)
    {
        var item = request.Items[0];
        var letter = item.WirePath.Length > 0 ? item.WirePath[0] : item.Name[0];
        var driveWire = $"{char.ToUpperInvariant(letter)}:\\";
        var (free, total) = conn.GetDiskFreeSpace(driveWire);
        var description = $"{char.ToUpperInvariant(letter)}: ({request.ConsoleName})";

        return new PropertyContext
        {
            Kind = PropertyTargetKind.Drive,
            ConsoleName = request.ConsoleName,
            Caption = description,
            Drive = new DriveGeneralInfo
            {
                Letter = letter,
                Description = description,
                DriveType = FormattingHelper.GetDriveTypeName(letter),
                TotalBytes = total,
                FreeBytes = free,
            },
        };
    }

    private static PropertyContext BuildFileContext(XbdmConnection conn, PropertyRequest request)
    {
        uint fileCount = 0;
        uint folderCount = 0;
        ulong totalSize = 0;
        uint validAttrs = XbdmConstants.AttrReadOnly | XbdmConstants.AttrHidden;
        uint attrs = 0;
        var variousTypes = false;
        string? typeName = null;
        DateTimeOffset? created = null;
        DateTimeOffset? modified = null;
        var attributesMerged = false;

        foreach (var item in request.Items)
        {
            var entry = conn.GetFileAttributes(item.WirePath);
            if ((entry.Attributes & XbdmConstants.AttrDirectory) != 0)
                folderCount++;
            else
            {
                fileCount++;
                totalSize += entry.Size;
            }

            var itemType = GetTypeName(item.Name, entry.Attributes);
            if (typeName == null)
                typeName = itemType;
            else if (itemType != typeName)
                variousTypes = true;

            if (!attributesMerged)
            {
                attrs = entry.Attributes;
                attributesMerged = true;
            }
            else
            {
                validAttrs &= ~(attrs ^ entry.Attributes);
                attrs |= entry.Attributes;
            }

            if (entry.ChangeTimeUnix > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(entry.ChangeTimeUnix);
                created ??= dt;
                modified = dt;
            }
        }

        if (request.Kind == PropertyTargetKind.Folder && request.Items.Count == 1)
        {
            CountFolderContents(conn, request.Items[0].WirePath, out fileCount, out folderCount, out totalSize);
            typeName = "Folder";
            variousTypes = false;
        }

        var displayName = request.Items.Count == 1
            ? request.Items[0].Name
            : $"{request.Items.Count} items selected";

        var locationPath = WirePathService.GetParentDisplayPath(request.FolderDisplayPath) ?? request.FolderDisplayPath;
        var location = FormattingHelper.BuildLocationString(locationPath, request.ConsoleName);
        var info = new FileGeneralInfo
        {
            Items = request.Items,
            DisplayName = displayName,
            TypeName = variousTypes ? "Various" : typeName,
            Location = location,
            TotalSize = totalSize,
            FileCount = fileCount,
            FolderCount = folderCount,
            Created = created,
            Modified = modified,
            Attributes = attrs,
            ValidAttributes = validAttrs,
            ReadOnly = TriState(attrs, validAttrs, XbdmConstants.AttrReadOnly),
            Hidden = TriState(attrs, validAttrs, XbdmConstants.AttrHidden),
        };

        return new PropertyContext
        {
            Kind = request.Kind,
            ConsoleName = request.ConsoleName,
            Caption = displayName,
            File = info,
        };
    }

    private static bool? TriState(uint attrs, uint valid, uint flag)
    {
        if ((valid & flag) == 0)
            return null;
        return (attrs & flag) != 0;
    }

    private static string GetTypeName(string name, uint attributes) =>
        (attributes & XbdmConstants.AttrDirectory) != 0 ? "Folder" : Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".xbe" => "Xbox executable",
            ".xbx" => "Xbox media",
            _ => "File",
        };

    private static void CountFolderContents(XbdmConnection conn, string wirePath, out uint files, out uint folders, out ulong size)
    {
        files = 0;
        folders = 0;
        size = 0;
        foreach (var entry in conn.ListDirectory(wirePath))
        {
            var child = $"{wirePath.TrimEnd('\\')}\\{entry.Name}";
            if ((entry.Attributes & XbdmConstants.AttrDirectory) != 0)
            {
                folders++;
                CountFolderContents(conn, child, out var cf, out var cfd, out var cs);
                files += cf;
                folders += cfd;
                size += cs;
            }
            else
            {
                files++;
                size += entry.Size;
            }
        }
    }

    private static string? FormatOrNull(uint? address) =>
        address.HasValue ? FormattingHelper.FormatIpAddress(address.Value) : null;

    public void ApplyFileAttributes(XbdmConnection conn, FileGeneralInfo file)
    {
        if (file.Items.Count == 0)
            return;

        uint? readOnly = file.ReadOnly == true ? XbdmConstants.AttrReadOnly : file.ReadOnly == false ? 0u : null;
        uint? hidden = file.Hidden == true ? XbdmConstants.AttrHidden : file.Hidden == false ? 0u : null;

        foreach (var item in file.Items)
        {
            var entry = conn.GetFileAttributes(item.WirePath);
            var attrs = entry.Attributes & ~(XbdmConstants.AttrReadOnly | XbdmConstants.AttrHidden);
            if (readOnly.HasValue)
                attrs = (attrs & ~XbdmConstants.AttrReadOnly) | readOnly.Value;
            if (hidden.HasValue)
                attrs = (attrs & ~XbdmConstants.AttrHidden) | hidden.Value;
            conn.SetFileAttributes(item.WirePath, attrs);
        }
    }

    public SecurityEditorState LoadSecurity(XbdmConnection conn)
    {
        var state = new SecurityEditorState
        {
            IsLocked = conn.IsSecurityEnabled(),
            SupportsUserPriv = conn.SupportsUserPrivileges(),
        };

        if (!state.IsLocked)
            return state;

        try
        {
            state.CurrentAccess = conn.GetUserAccess();
        }
        catch
        {
            state.CurrentAccess = 0;
        }

        if ((state.CurrentAccess & XbdmConstants.PrivManage) != 0)
        {
            EnterManageMode(conn, state);
            return state;
        }

        // PC-user manage without GETUSERPRIV ME (some locked sessions only expose USERLIST).
        try
        {
            var users = conn.ListUsers();
            if (users.Count > 0)
                EnterManageMode(conn, state, users);
        }
        catch
        {
        }

        return state;
    }

    private static void EnterManageMode(
        XbdmConnection conn,
        SecurityEditorState state,
        IReadOnlyList<XbdmUser>? users = null)
    {
        state.ManageMode = true;
        users ??= conn.ListUsers();
        foreach (var user in users)
        {
            state.Users.Add(new SecurityUserEntry
            {
                UserName = user.UserName,
                OriginalAccess = user.AccessPrivileges,
                NewAccess = user.AccessPrivileges,
            });
        }

        if (state.Users.Count > 0)
            state.SelectedUserName = state.Users[0].UserName;
    }
}

public sealed class PropertyRequest
{
    public required PropertyTargetKind Kind { get; init; }
    public required string ConsoleName { get; init; }
    public required string FolderDisplayPath { get; init; }
    public required IReadOnlyList<FileSelectionItem> Items { get; init; }

    public static PropertyRequest? FromSelection(string? console, string? currentPath, IReadOnlyList<FileSelectionItem> items)
    {
        if (string.IsNullOrWhiteSpace(console))
            return null;

        if (items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(currentPath) || WirePathService.IsConsoleRoot(currentPath, console))
            {
                return new PropertyRequest
                {
                    Kind = PropertyTargetKind.Console,
                    ConsoleName = console,
                    FolderDisplayPath = console,
                    Items = Array.Empty<FileSelectionItem>(),
                };
            }
        }

        if (items.Count == 0)
            return null;

        var folderPath = WirePathService.IsDriveListing(currentPath, console) ? console : currentPath!;
        var allDrives = items.All(i => i.Name.EndsWith(':'));
        if (items.Count == 1 && allDrives)
        {
            return new PropertyRequest
            {
                Kind = PropertyTargetKind.Drive,
                ConsoleName = console,
                FolderDisplayPath = folderPath,
                Items = items,
            };
        }

        var kind = items.Count > 1
            ? PropertyTargetKind.MultiFile
            : items[0].IsDirectory ? PropertyTargetKind.Folder : PropertyTargetKind.File;

        return new PropertyRequest
        {
            Kind = kind,
            ConsoleName = console,
            FolderDisplayPath = folderPath,
            Items = items,
        };
    }
}
