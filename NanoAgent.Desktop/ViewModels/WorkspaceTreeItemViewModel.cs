using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NanoAgent.Desktop.Models;

namespace NanoAgent.Desktop.ViewModels;

public sealed partial class WorkspaceTreeItemViewModel : ViewModelBase
{
    private readonly Func<WorkspaceTreeItemViewModel, Task>? _removeSection;
    private readonly Func<WorkspaceTreeItemViewModel, Task>? _removeWorkspace;

    [ObservableProperty]
    private bool _isExpanded;

    private WorkspaceTreeItemViewModel(
        ProjectInfo? project,
        WorkspaceSectionInfo? section,
        Func<WorkspaceTreeItemViewModel, Task>? removeSection,
        Func<WorkspaceTreeItemViewModel, Task>? removeWorkspace)
    {
        Project = project;
        Section = section;
        _removeSection = removeSection;
        _removeWorkspace = removeWorkspace;
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => CanRemove);
        IsExpanded = IsWorkspace;
    }

    public ObservableCollection<WorkspaceTreeItemViewModel> Children { get; } = new();

    public bool CanRemove => IsWorkspace || IsSection;

    public bool CanRemoveWorkspace => IsWorkspace;

    public string DetailText => IsWorkspace
        ? Project?.Path ?? string.Empty
        : Section?.Subtitle ?? string.Empty;

    public bool HasChildren => Children.Count > 0;

    public bool IsSection => Section is not null;

    public bool IsWorkspace => Project is not null && Section is null;

    public string MetaText => IsWorkspace
        ? HasChildren ? $"{Children.Count} {(Children.Count == 1 ? "section" : "sections")}" : "No sections"
        : Section?.UpdatedText ?? string.Empty;

    public string Name => IsWorkspace
        ? Project?.Name ?? "Workspace"
        : Section?.Title ?? "Untitled section";

    public ProjectInfo? Project { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public IAsyncRelayCommand RemoveWorkspaceCommand => RemoveCommand;

    public WorkspaceSectionInfo? Section { get; }

    public static WorkspaceTreeItemViewModel CreateWorkspace(
        ProjectInfo project,
        IEnumerable<WorkspaceSectionInfo> sections,
        Func<WorkspaceTreeItemViewModel, Task> removeSection,
        Func<WorkspaceTreeItemViewModel, Task> removeWorkspace)
    {
        WorkspaceTreeItemViewModel item = new(project, section: null, removeSection: null, removeWorkspace);
        foreach (WorkspaceSectionInfo section in sections)
        {
            item.Children.Add(CreateSection(project, section, removeSection));
        }

        item.OnPropertyChanged(nameof(HasChildren));
        item.OnPropertyChanged(nameof(MetaText));
        return item;
    }

    private static WorkspaceTreeItemViewModel CreateSection(
        ProjectInfo project,
        WorkspaceSectionInfo section,
        Func<WorkspaceTreeItemViewModel, Task> removeSection)
    {
        return new WorkspaceTreeItemViewModel(project, section, removeSection, removeWorkspace: null);
    }

    private Task RemoveAsync()
    {
        if (IsSection)
        {
            return _removeSection?.Invoke(this) ?? Task.CompletedTask;
        }

        return _removeWorkspace?.Invoke(this) ?? Task.CompletedTask;
    }
}
