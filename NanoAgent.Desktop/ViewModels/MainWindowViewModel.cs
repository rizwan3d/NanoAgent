using CommunityToolkit.Mvvm.Input;
using NanoAgent.Desktop.Services;

namespace NanoAgent.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private bool _isRefreshingSections;

    public MainWindowViewModel()
    {
        var settingsService = new SettingsService();
        var sectionHistoryService = new SectionHistoryService();

        Project = new ProjectViewModel(settingsService, sectionHistoryService);
        Chat = new ChatViewModel(new AgentRunner());
        StartNewSectionCommand = new AsyncRelayCommand(StartNewSectionAsync, CanStartNewSection);
        Chat.RunCompleted += async (_, _) => await RefreshProjectSectionsAsync(loadSelectedSection: false);
        Chat.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Chat.IsRunning))
            {
                StartNewSectionCommand.NotifyCanExecuteChanged();
            }
        };
        Project.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(Project.SelectedProject))
            {
                StartNewSectionCommand.NotifyCanExecuteChanged();
                await RefreshProjectSectionsAsync(loadSelectedSection: true);
            }
            else if (args.PropertyName == nameof(Project.SelectedSection) && !_isRefreshingSections)
            {
                Chat.SelectedSection = Project.SelectedSection;
                await Chat.LoadSessionAsync(Project.SelectedProject);
            }
        };

        if (Project.SelectedProject is not null)
        {
            _ = RefreshProjectSectionsAsync(loadSelectedSection: true);
        }
    }

    public ProjectViewModel Project { get; }

    public ChatViewModel Chat { get; }

    public IAsyncRelayCommand StartNewSectionCommand { get; }

    private bool CanStartNewSection()
    {
        return !Chat.IsRunning && Project.SelectedProject is not null;
    }

    private async Task StartNewSectionAsync()
    {
        if (Project.SelectedProject is null)
        {
            return;
        }

        Project.SelectedSection = null;
        Chat.SelectedSection = null;
        await Chat.LoadSessionAsync(Project.SelectedProject);
    }

    private async Task RefreshProjectSectionsAsync(bool loadSelectedSection)
    {
        _isRefreshingSections = true;
        try
        {
            await Project.RefreshSectionsAsync();
            Chat.SelectedSection = Project.SelectedSection;
        }
        finally
        {
            _isRefreshingSections = false;
        }

        if (loadSelectedSection)
        {
            await Chat.LoadSessionAsync(Project.SelectedProject);
        }
    }
}
