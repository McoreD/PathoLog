using System.Windows;
using PathoLog.Wpf.ViewModels;

namespace PathoLog.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Template is loaded via MainViewModel initialization.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Current.MainWindow?.DataContext is MainViewModel vm)
        {
            vm.SaveSettings();
            vm.SaveTemplate();
        }
        base.OnExit(e);
    }
}
