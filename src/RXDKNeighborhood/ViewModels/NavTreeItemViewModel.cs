using System.Collections.ObjectModel;
using Avalonia.Media;
using RXDKNeighborhood.Core.Models;

namespace RXDKNeighborhood.ViewModels;

public sealed class NavTreeItemViewModel
{
    public required string Title { get; init; }
    public required NavigationNodeKind Kind { get; init; }
    public required string DisplayPath { get; init; }
    public string? ConsoleName { get; init; }
    public IImage? Icon { get; init; }
    public ObservableCollection<NavTreeItemViewModel> Children { get; } = new();
    public bool ChildrenLoaded { get; set; }
}
