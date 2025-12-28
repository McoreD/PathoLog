using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PathoLog.Wpf.Services;
using PathoLog.Wpf.Dialogs;

namespace PathoLog.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<PatientSummary> Patients { get; } = new();
    public ObservableCollection<ReportSummary> Reports { get; } = new();
    public ObservableCollection<ResultRow> Results { get; } = new();
    public ObservableCollection<ReviewTaskRow> ReviewTasks { get; } = new();
    public ObservableCollection<MappingRow> Mappings { get; } = new();
    public ObservableCollection<TrendSeriesViewModel> Trends { get; } = new();
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings;
    private bool _isImporting;

    private PatientSummary? _selectedPatient;
    public PatientSummary? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetField(ref _selectedPatient, value))
            {
                LoadSampleReports();
            }
        }
    }

    private ReportSummary? _selectedReport;
    public ReportSummary? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
            {
                LoadSampleResults();
            }
        }
    }

    private string? _selectedFileName;
    public string? SelectedFileName
    {
        get => _selectedFileName;
        set
        {
            if (SetField(ref _selectedFileName, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _importStatus = "Ready to import";
    public string ImportStatus
    {
        get => _importStatus;
        set => SetField(ref _importStatus, value);
    }

    public ICommand SelectPdfCommand { get; }
    public ICommand ImportPdfCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand NewPatientCommand { get; }

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        SelectPdfCommand = new RelayCommand(_ => SelectPdf());
        ImportPdfCommand = new RelayCommand(async _ => await ImportPdfAsync(), _ => CanImport());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        NewPatientCommand = new RelayCommand(_ => CreatePatient());

        SeedSampleData();
    }

    private void SelectPdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select pathology PDF"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFileName = dialog.FileName;
            ImportStatus = "Ready to import";
        }
    }

    private bool CanImport() => !_isImporting && !string.IsNullOrWhiteSpace(SelectedFileName) && SelectedPatient is not null;

    private async Task ImportPdfAsync()
    {
        if (!CanImport())
        {
            return;
        }

        _isImporting = true;
        CommandManager.InvalidateRequerySuggested();

        var reportId = $"R-{DateTime.Now:yyyyMMddHHmmss}";
        var report = new ReportSummary(reportId, DateTime.Now.ToString("yyyy-MM-dd"), System.IO.Path.GetFileName(SelectedFileName));
        Reports.Insert(0, report);
        SelectedReport = report;
        Results.Clear();
        ImportStatus = $"Queued import for {System.IO.Path.GetFileName(SelectedFileName)}";
        Results.Add(new ResultRow("Uploaded PDF", "Pending parse", SelectedFileName, "Queued"));

        try
        {
            await Task.Delay(400);
            ImportStatus = "Extracting text...";

            await Task.Delay(500);
            ImportStatus = "Running AI parser...";

            await Task.Delay(600);
            ImportStatus = "Normalising results...";

            Results.Clear();
            Results.Add(new ResultRow("TSH", "2.1", "mIU/L", "Normal"));
            Results.Add(new ResultRow("FT4", "14.8", "pmol/L", "Normal"));
            Results.Add(new ResultRow("HbA1c", "6.1", "%", "Borderline"));

            ReviewTasks.Clear();
            ReviewTasks.Add(new ReviewTaskRow("results[2].unit", "Unit mismatch needs confirmation"));
            ReviewTasks.Add(new ReviewTaskRow("results[0].mapping", "Low confidence mapping"));

            ImportStatus = "Import completed – results ready for review";
        }
        finally
        {
            _isImporting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ExportCsv()
    {
        ImportStatus = "CSV export queued";
    }

    public void SaveSettings()
    {
        _settingsStore.Save(_settings);
    }

    public void ReloadSettings()
    {
        _settings = _settingsStore.Load();
        OnPropertyChanged(nameof(OpenAiApiKey));
        OnPropertyChanged(nameof(GeminiApiKey));
    }

    private void SeedSampleData()
    {
        Patients.Add(new PatientSummary("P-001", "Alex Morgan"));
        Patients.Add(new PatientSummary("P-002", "Sam Torres"));
        Patients.Add(new PatientSummary("P-003", "Jordan Lee"));
        SelectedPatient = Patients.FirstOrDefault();

        ReviewTasks.Add(new ReviewTaskRow("results[0].value_number", "Low confidence value"));
        ReviewTasks.Add(new ReviewTaskRow("report.panel_name", "Low confidence value"));

        Mappings.Add(new MappingRow("Thyroid Stimulating Hormone", "TSH", "UserConfirmed"));
        Mappings.Add(new MappingRow("Free T4", "FT4", "Deterministic"));

        Trends.Add(new TrendSeriesViewModel("TSH", new[] { 0.8, 1.2, 2.1, 1.7 }, new[] { 0.0, 1.0, 2.0, 3.0 }));
        Trends.Add(new TrendSeriesViewModel("HbA1c", new[] { 5.8, 6.2, 6.0, 5.9 }, new[] { 0.0, 1.0, 2.0, 3.0 }));
    }

    private void LoadSampleReports()
    {
        Reports.Clear();
        Results.Clear();

        if (SelectedPatient is null)
        {
            return;
        }

        Reports.Add(new ReportSummary("R-2025-01", "2025-01-12", "Thyroid Panel"));
        Reports.Add(new ReportSummary("R-2024-12", "2024-12-05", "Iron Studies"));
        SelectedReport = Reports.FirstOrDefault();
    }

    private void LoadSampleResults()
    {
        Results.Clear();

        if (SelectedReport is null)
        {
            return;
        }

        Results.Add(new ResultRow("TSH", "1.7", "mIU/L", "Normal"));
        Results.Add(new ResultRow("FT4", "14.2", "pmol/L", "Normal"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void CreatePatient()
    {
        var dialog = new NewPatientWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrWhiteSpace(dialog.PatientName))
        {
            var id = $"P-{DateTime.Now:yyyyMMddHHmmss}";
            var patient = new PatientSummary(id, dialog.PatientName!);
            Patients.Insert(0, patient);
            SelectedPatient = patient;
            ImportStatus = $"Created patient {dialog.PatientName}";
        }
    }

    public string? OpenAiApiKey
    {
        get => _settings.OpenAiApiKey;
        set
        {
            if (value == _settings.OpenAiApiKey) return;
            _settings.OpenAiApiKey = value;
            OnPropertyChanged();
        }
    }

    public string? GeminiApiKey
    {
        get => _settings.GeminiApiKey;
        set
        {
            if (value == _settings.GeminiApiKey) return;
            _settings.GeminiApiKey = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record PatientSummary(string Id, string DisplayName);

public sealed record ReportSummary(string Id, string ReportDate, string PanelName);

public sealed record ResultRow(string Analyte, string Value, string Unit, string Flag);

public sealed record ReviewTaskRow(string FieldPath, string Reason);

public sealed record MappingRow(string SourceName, string ShortCode, string Method);

public sealed class TrendSeriesViewModel
{
    public TrendSeriesViewModel(string analyteShortCode, IEnumerable<double> values, IEnumerable<double> indexes)
    {
        AnalyteShortCode = analyteShortCode;
        var pointCollection = new PointCollection();
        using var valueEnumerator = values.GetEnumerator();
        using var indexEnumerator = indexes.GetEnumerator();
        while (valueEnumerator.MoveNext() && indexEnumerator.MoveNext())
        {
            pointCollection.Add(new System.Windows.Point(indexEnumerator.Current * 60, 120 - valueEnumerator.Current * 10));
        }

        Points = pointCollection;
    }

    public string AnalyteShortCode { get; }
    public PointCollection Points { get; }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
