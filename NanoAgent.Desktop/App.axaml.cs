using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NanoAgent.Desktop.ViewModels;
using NanoAgent.Desktop.Views;

namespace NanoAgent.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.ShutdownRequested += async (_, _) => await viewModel.DisposeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
