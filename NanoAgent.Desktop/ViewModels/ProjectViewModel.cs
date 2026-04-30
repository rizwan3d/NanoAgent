using CommunityToolkit.Mvvm.ComponentModel;
using NanoAgent.Desktop.Models;
using NanoAgent.Desktop.Services;
using System.Collections.ObjectModel;

namespace NanoAgent.Desktop.ViewModels;

public partial class ProjectViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly SectionHistoryService _sectionHistoryService;
    private bool _isApplyingTreeSelection;
    private string? _pendingTreeSectionId;

    [ObservableProperty]
    private ProjectInfo? _selectedProject;

    [ObservableProperty]
    private WorkspaceSectionInfo? _selectedSection;

    [ObservableProperty]
    private WorkspaceTreeItemViewModel? _selectedTreeItem;

    public ProjectViewModel(
        SettingsService settingsService,
        SectionHistoryService sectionHistoryService)
    {
        _settingsService = settingsService;
        _sectionHistoryService = sectionHistoryService;
        LoadProjects();
    }

    public ObservableCollection<ProjectInfo> Projects { get; } = new();

    public ObservableCollection<WorkspaceSectionInfo> Sections { get; } = new();

    public ObservableCollection<WorkspaceTreeItemViewModel> WorkspaceTreeItems { get; } = new();

    public string SectionCountText => Sections.Count == 0
        ? "0"
        : Sections.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string SelectedProjectName => SelectedProject?.Name ?? "No project open";

    public string SelectedProjectPath => SelectedProject?.Path ?? "No folder selected";

    public async Task AddProjectAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(path);
        var existing = Projects.FirstOrDefault(project => string.Equals(project.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedProject = existing;
            await RefreshWorkspaceTreeAsync();
            return;
        }

        var name = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var project = new ProjectInfo(name, normalizedPath, DateTimeOffset.Now);

        Projects.Insert(0, project);
        SelectedProject = project;

        await _settingsService.SaveRecentProjectsAsync(Projects);
        await RefreshWorkspaceTreeAsync();
    }

    public async Task RemoveWorkspaceAsync(WorkspaceTreeItemViewModel? item)
    {
        ProjectInfo? project = item?.Project;
        if (project is null)
        {
            return;
        }

        ProjectInfo? existing = Projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, project.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        bool removedSelectedProject = SelectedProject is not null &&
            string.Equals(SelectedProject.Path, existing.Path, StringComparison.OrdinalIgnoreCase);

        Projects.Remove(existing);
        await _settingsService.SaveRecentProjectsAsync(Projects);

        if (removedSelectedProject)
        {
            SelectedProject = Projects.FirstOrDefault();
            SelectedSection = null;
            Sections.Clear();
            OnPropertyChanged(nameof(SectionCountText));
        }

        await RefreshWorkspaceTreeAsync();
    }

    public async Task RemoveSectionAsync(WorkspaceTreeItemViewModel? item)
    {
        ProjectInfo? project = item?.Project;
        WorkspaceSectionInfo? section = item?.Section;
        if (project is null || section is null)
        {
            return;
        }

        bool removed = await _sectionHistoryService.DeleteSectionAsync(
            project.Path,
            section.SectionId);
        if (!removed)
        {
            return;
        }

        bool isSelectedProject = SelectedProject is not null &&
            string.Equals(SelectedProject.Path, project.Path, StringComparison.OrdinalIgnoreCase);
        if (isSelectedProject)
        {
            WorkspaceSectionInfo? existingSection = Sections.FirstOrDefault(candidate =>
                string.Equals(candidate.SectionId, section.SectionId, StringComparison.OrdinalIgnoreCase));
            bool removedSelectedSection = SelectedSection is not null &&
                string.Equals(SelectedSection.SectionId, section.SectionId, StringComparison.OrdinalIgnoreCase);

            if (existingSection is not null)
            {
                Sections.Remove(existingSection);
            }

            if (removedSelectedSection)
            {
                _pendingTreeSectionId = null;
                SelectedSection = Sections.FirstOrDefault();
            }

            OnPropertyChanged(nameof(SectionCountText));
        }

        await RefreshWorkspaceTreeAsync();
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        OnPropertyChanged(nameof(SelectedProjectName));
        OnPropertyChanged(nameof(SelectedProjectPath));
    }

    partial void OnSelectedTreeItemChanged(WorkspaceTreeItemViewModel? value)
    {
        if (_isApplyingTreeSelection || value is null)
        {
            return;
        }

        ProjectInfo? project = value.Project;
        if (project is null)
        {
            return;
        }

        bool projectChanged = SelectedProject is null ||
            !string.Equals(SelectedProject.Path, project.Path, StringComparison.OrdinalIgnoreCase);

        if (value.Section is not null)
        {
            _pendingTreeSectionId = value.Section.SectionId;
        }
        else
        {
            _pendingTreeSectionId = null;
        }

        if (projectChanged)
        {
            SelectedProject = project;
            return;
        }

        if (value.Section is not null)
        {
            SelectedSection = Sections.FirstOrDefault(section =>
                string.Equals(section.SectionId, value.Section.SectionId, StringComparison.OrdinalIgnoreCase)) ??
                value.Section;
        }
    }

    public async Task RefreshSectionsAsync()
    {
        string? selectedSectionId = _pendingTreeSectionId ?? SelectedSection?.SectionId;
        _pendingTreeSectionId = null;
        Sections.Clear();
        SelectedSection = null;

        if (SelectedProject is null)
        {
            OnPropertyChanged(nameof(SectionCountText));
            await RefreshWorkspaceTreeAsync();
            return;
        }

        IReadOnlyList<WorkspaceSectionInfo> sections = await _sectionHistoryService.ListSectionsAsync(
            SelectedProject.Path);

        foreach (WorkspaceSectionInfo section in sections)
        {
            Sections.Add(section);
        }

        SelectedSection = Sections.FirstOrDefault(section =>
            string.Equals(section.SectionId, selectedSectionId, StringComparison.OrdinalIgnoreCase)) ??
            Sections.FirstOrDefault();
        OnPropertyChanged(nameof(SectionCountText));
        await RefreshWorkspaceTreeAsync();
    }

    public async Task RefreshWorkspaceTreeAsync()
    {
        string? selectedProjectPath = SelectedProject?.Path;
        string? selectedSectionId = SelectedSection?.SectionId;

        WorkspaceTreeItems.Clear();
        foreach (ProjectInfo project in Projects)
        {
            IReadOnlyList<WorkspaceSectionInfo> sections = await _sectionHistoryService.ListSectionsAsync(project.Path);
            WorkspaceTreeItems.Add(WorkspaceTreeItemViewModel.CreateWorkspace(
                project,
                sections,
                RemoveSectionAsync,
                RemoveWorkspaceAsync));
        }

        SetSelectedTreeItem(selectedProjectPath, selectedSectionId);
    }

    private void LoadProjects()
    {
        foreach (var project in _settingsService.LoadRecentProjects())
        {
            Projects.Add(project);
        }

        SelectedProject = Projects.FirstOrDefault();
    }

    private void SetSelectedTreeItem(
        string? selectedProjectPath,
        string? selectedSectionId)
    {
        WorkspaceTreeItemViewModel? selectedItem = null;
        foreach (WorkspaceTreeItemViewModel workspace in WorkspaceTreeItems)
        {
            if (workspace.Project is null ||
                string.IsNullOrWhiteSpace(selectedProjectPath) ||
                !string.Equals(workspace.Project.Path, selectedProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selectedItem = workspace;
            if (!string.IsNullOrWhiteSpace(selectedSectionId))
            {
                selectedItem = workspace.Children.FirstOrDefault(section =>
                    section.Section is not null &&
                    string.Equals(section.Section.SectionId, selectedSectionId, StringComparison.OrdinalIgnoreCase)) ??
                    selectedItem;
            }

            break;
        }

        _isApplyingTreeSelection = true;
        try
        {
            SelectedTreeItem = selectedItem;
        }
        finally
        {
            _isApplyingTreeSelection = false;
        }
    }
}
