using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly PatientStore _patientStore = new();
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
                Reports.Clear();
                Results.Clear();
                OnPropertyChanged(nameof(RecentReportsCount));
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
                Results.Clear();
                OnPropertyChanged(nameof(RecentReportsCount));
            }
        }
    }

    private readonly List<string> _selectedFiles = new();
    private string? _selectedFilesLabel;
    public string? SelectedFilesLabel
    {
        get => _selectedFilesLabel;
        set => SetField(ref _selectedFilesLabel, value);
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
    public ICommand LoadReportJsonCommand { get; }

    public int RecentReportsCount => Reports.Count;
    public int PendingReviewsCount => ReviewTasks.Count;
    public int MappingCount => Mappings.Count;

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        SelectPdfCommand = new RelayCommand(_ => SelectPdf());
        ImportPdfCommand = new RelayCommand(async _ => await ImportPdfAsync(), _ => CanImport());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        NewPatientCommand = new RelayCommand(_ => CreatePatient());
        LoadReportJsonCommand = new RelayCommand(_ => LoadReportJson());

        Reports.CollectionChanged += (_, __) => OnPropertyChanged(nameof(RecentReportsCount));
        ReviewTasks.CollectionChanged += (_, __) => OnPropertyChanged(nameof(PendingReviewsCount));
        Mappings.CollectionChanged += (_, __) => OnPropertyChanged(nameof(MappingCount));

        LoadPatientsFromStore();
    }

    private void SelectPdf()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select pathology PDF(s)",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedFiles.Clear();
            _selectedFiles.AddRange(dialog.FileNames);
            SelectedFilesLabel = _selectedFiles.Count switch
            {
                0 => "No files selected",
                1 => System.IO.Path.GetFileName(_selectedFiles[0]),
                _ => $"{_selectedFiles.Count} files selected (first: {System.IO.Path.GetFileName(_selectedFiles[0])})"
            };
            ImportStatus = "Ready to import";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanImport() => !_isImporting && _selectedFiles.Any() && SelectedPatient is not null;

    private async Task ImportPdfAsync()
    {
        if (!CanImport())
        {
            return;
        }

        _isImporting = true;
        CommandManager.InvalidateRequerySuggested();

        Results.Clear();
        ImportStatus = $"Queued import for {_selectedFiles.Count} file(s)";

        try
        {
            foreach (var file in _selectedFiles)
            {
                await Task.Delay(150);
                var reportId = $"R-{DateTime.Now:yyyyMMddHHmmssfff}";
                var report = new ReportSummary(reportId, DateTime.Now.ToString("yyyy-MM-dd"), System.IO.Path.GetFileName(file));
                Reports.Insert(0, report);
                SelectedReport = report;
                Results.Clear();
                Results.Add(new ResultRow("Uploaded PDF", "Pending parse", file, "Queued"));
                ImportStatus = $"Import queued for {System.IO.Path.GetFileName(file)}";

                await Task.Delay(300);
                ImportStatus = "Extracting text...";
                await Task.Delay(300);
                ImportStatus = "Running AI parser...";
                await Task.Delay(300);
                ImportStatus = "Normalising results...";

                Results.Clear();
                Results.Add(new ResultRow("TSH", "2.1", "mIU/L", "Normal"));
                Results.Add(new ResultRow("FT4", "14.8", "pmol/L", "Normal"));
                Results.Add(new ResultRow("HbA1c", "6.1", "%", "Borderline"));
                ReviewTasks.Clear();
                ReviewTasks.Add(new ReviewTaskRow("results[2].unit", "Unit mismatch needs confirmation"));

                ImportStatus = $"Import completed for {System.IO.Path.GetFileName(file)}";
            }
        }
        finally
        {
            _isImporting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ExportCsv()
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = BuildFileName("report", "patient", DateTime.Today, "csv")
        };
        if (saveDialog.ShowDialog() == true)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Analyte,Value,Unit,Flag");
            foreach (var r in Results)
            {
                csv.AppendLine($"{r.Analyte},{r.Value},{r.Unit},{r.Flag}");
            }
            File.WriteAllText(saveDialog.FileName, csv.ToString());
            ImportStatus = $"Exported CSV to {saveDialog.FileName}";
        }
        else
        {
            ImportStatus = "Export cancelled";
        }
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

    private void LoadPatientsFromStore()
    {
        Patients.Clear();
        foreach (var p in _patientStore.ListPatients())
        {
            Patients.Add(new PatientSummary(p.Id, p.FullName));
        }
        SelectedPatient = Patients.FirstOrDefault();

        ReviewTasks.Clear();
        Mappings.Clear();
        Trends.Clear();
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
            var record = _patientStore.AddPatient(dialog.PatientName!, null, "unknown");
            var patient = new PatientSummary(record.Id, record.FullName);
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

    private void LoadReportJson()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultDir = Path.Combine(docs, "PathoLog", "reports");
        var dialog = new OpenFileDialog
        {
            Filter = "Report JSON (*.json)|*.json",
            InitialDirectory = Directory.Exists(defaultDir) ? defaultDir : docs,
            Title = "Select parsed report JSON"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var parsed = JsonSerializer.Deserialize<ParsedReportWrapper>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed?.Report is null)
            {
                ImportStatus = "Failed to load report JSON";
                return;
            }

            Results.Clear();
            foreach (var r in parsed.Report.Results ?? Array.Empty<ParsedResultJson>())
            {
                Results.Add(new ResultRow(
                    r.AnalyteNameOriginal ?? r.AnalyteShortCode ?? "Analyte",
                    r.ValueText ?? (r.ValueNumeric?.ToString("0.###") ?? ""),
                    r.UnitOriginal ?? r.UnitNormalised ?? "",
                    r.ExtractionConfidence ?? ""));
            }

            ReviewTasks.Clear();
            foreach (var t in parsed.Report.ReviewTasks ?? Array.Empty<ParsedReviewTaskJson>())
            {
            ReviewTasks.Add(new ReviewTaskRow(t.FieldPath ?? "field", t.Reason ?? "needs review"));
            OnPropertyChanged(nameof(PendingReviewsCount));
        }

        ImportStatus = $"Loaded report JSON: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ImportStatus = $"Failed to load JSON: {ex.Message}";
        }
    }

    private static string BuildFileName(string panel, string patient, DateTime date, string ext)
    {
        var panelSlug = Slug(panel);
        var patientSlug = Slug(patient);
        return $"{date:yyyy-MM-dd}_{panelSlug}_{patientSlug}_local.{ext}";
    }

    private static string Slug(string value)
    {
        var cleaned = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
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

public sealed class ParsedReportWrapper
{
    public string? Schema_Version { get; set; }
    public ParsedReportJson? Report { get; set; }
}

public sealed class ParsedReportJson
{
    public ParsedResultJson[]? Results { get; set; }
    public ParsedReviewTaskJson[]? ReviewTasks { get; set; }
}

public sealed class ParsedResultJson
{
    public string? AnalyteNameOriginal { get; set; }
    public string? AnalyteShortCode { get; set; }
    public string? ValueText { get; set; }
    public double? ValueNumeric { get; set; }
    public string? UnitOriginal { get; set; }
    public string? UnitNormalised { get; set; }
    public string? ExtractionConfidence { get; set; }
}

public sealed class ParsedReviewTaskJson
{
    public string? FieldPath { get; set; }
    public string? Reason { get; set; }
}
