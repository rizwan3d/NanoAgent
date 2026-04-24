using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NanoAgent.Desktop.Models;
using NanoAgent.Desktop.Services;

namespace NanoAgent.Desktop.ViewModels;

public partial class ProjectViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly SectionHistoryService _sectionHistoryService;

    [ObservableProperty]
    private ProjectInfo? _selectedProject;

    [ObservableProperty]
    private WorkspaceSectionInfo? _selectedSection;

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
            return;
        }

        var name = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var project = new ProjectInfo(name, normalizedPath, DateTimeOffset.Now);

        Projects.Insert(0, project);
        SelectedProject = project;

        await _settingsService.SaveRecentProjectsAsync(Projects);
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        OnPropertyChanged(nameof(SelectedProjectName));
        OnPropertyChanged(nameof(SelectedProjectPath));
    }

    public async Task RefreshSectionsAsync()
    {
        string? selectedSectionId = SelectedSection?.SectionId;
        Sections.Clear();
        SelectedSection = null;

        if (SelectedProject is null)
        {
            OnPropertyChanged(nameof(SectionCountText));
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
    }

    private void LoadProjects()
    {
        foreach (var project in _settingsService.LoadRecentProjects())
        {
            Projects.Add(project);
        }

        SelectedProject = Projects.FirstOrDefault();
    }
}
