using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace NanoAgent.Desktop.Models;

public sealed record DesktopCommandSuggestion(
    string Command,
    string Usage,
    string Description,
    bool RequiresArgument,
    bool IsSelected,
    IRelayCommand SelectCommand)
{
    public IBrush Background => Brush.Parse(IsSelected ? "#E5E7EB" : "#121417");

    public IBrush BorderBrush => Brush.Parse(IsSelected ? "#F4F4F5" : "#30363D");

    public IBrush DescriptionForeground => Brush.Parse(IsSelected ? "#1F2937" : "#8B949E");

    public IBrush MarkerForeground => Brush.Parse(IsSelected ? "#09090B" : "#6B7280");

    public string SelectionMarker => IsSelected ? ">" : string.Empty;

    public IBrush UsageForeground => Brush.Parse(IsSelected ? "#09090B" : "#F4F4F5");
}

public sealed record DesktopCommandSuggestionDescriptor(
    string Command,
    string Usage,
    string Description,
    bool RequiresArgument);
