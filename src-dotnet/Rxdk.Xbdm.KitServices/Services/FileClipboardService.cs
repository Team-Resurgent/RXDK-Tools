using Rxdk.Xbdm.KitServices.Models;

namespace Rxdk.Xbdm.KitServices.Services;

public class FileClipboardService
{
    private FileClipboardOperation _operation;
    private string _consoleName = "";
    private string _folderPath = "";
    private readonly List<string> _names = new();

    public bool HasItems => _operation != FileClipboardOperation.None && _names.Count > 0;

    public FileClipboardOperation Operation => _operation;

    public string ConsoleName => _consoleName;

    public string FolderPath => _folderPath;

    public IReadOnlyList<string> Names => _names;

    public void Cut(FileSelection selection)
    {
        SetSelection(FileClipboardOperation.Cut, selection);
    }

    public void Copy(FileSelection selection)
    {
        SetSelection(FileClipboardOperation.Copy, selection);
    }

    public void Clear()
    {
        _operation = FileClipboardOperation.None;
        _consoleName = "";
        _folderPath = "";
        _names.Clear();
    }

    private void SetSelection(FileClipboardOperation operation, FileSelection selection)
    {
        if (selection.Items.Count == 0)
            return;

        _operation = operation;
        _consoleName = selection.ConsoleName;
        _folderPath = selection.FolderDisplayPath;
        _names.Clear();
        foreach (var item in selection.Items)
            _names.Add(item.Name);
    }
}
