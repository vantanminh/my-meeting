using System.IO;
using System.Windows;
using MeetingAssistant.Services;
using MeetingAssistant.ViewModels;

namespace MeetingAssistant;

public partial class App : System.Windows.Application
{
    public AppServices Services { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MeetingProcessingLog.UseDirectory(Path.Combine(AppPaths.RootDirectory, "logs"));
        try
        {
            var viewModel = new MainViewModel(Services);
            var window = new MainWindow(viewModel, Services.HotkeyService, Services.TrayService);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "startup-error.log"), exception.ToString());
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.Dispose();
        base.OnExit(e);
    }
}
