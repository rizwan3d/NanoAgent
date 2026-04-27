using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NanoAgent.Desktop.ViewModels;

namespace NanoAgent.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Project",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.Project.AddProjectAsync(path);
        }
    }

    private async void PromptTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!viewModel.Chat.ShouldHandlePromptKey(e.Key, e.KeyModifiers))
        {
            return;
        }

        e.Handled = true;

        await viewModel.Chat.HandlePromptKeyAsync(
            e.Key,
            e.KeyModifiers,
            viewModel.Project.SelectedProject);
    }
}
