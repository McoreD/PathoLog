using System.Windows;
using PathoLog.Wpf.ViewModels;

namespace PathoLog.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
