using System.Windows;

namespace PathoLog.Wpf.Dialogs;

public partial class NewPatientWindow : Window
{
    public string? PatientName { get; private set; }

    public NewPatientWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        PatientName = NameBox.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
