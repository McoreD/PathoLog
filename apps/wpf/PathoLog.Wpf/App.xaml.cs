using System.Windows;
using PathoLog.Wpf.ViewModels;

namespace PathoLog.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        if (Current.MainWindow?.DataContext is MainViewModel vm)
        {
            vm.SaveSettings();
        }
        base.OnExit(e);
    }
}
