using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
                var parsedResults = new List<ResultRow>
                {
                    new ResultRow("TSH", "2.1", "mIU/L", "Normal"),
                    new ResultRow("FT4", "14.8", "pmol/L", "Normal"),
                    new ResultRow("HbA1c", "6.1", "%", "Borderline")
                };
                var parsedReviews = new List<ReviewTaskRow>
                {
                    new ReviewTaskRow("results[2].unit", "Unit mismatch needs confirmation")
                };

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
                foreach (var r in parsedResults) Results.Add(r);
                ReviewTasks.Clear();
                foreach (var r in parsedReviews) ReviewTasks.Add(r);

                ImportStatus = $"Import completed for {System.IO.Path.GetFileName(file)}";

                SaveReportJsonToDisk(file, parsedResults, parsedReviews);
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

    private void SaveReportJsonToDisk(string pdfPath, IEnumerable<ResultRow> results, IEnumerable<ReviewTaskRow> reviews)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "PathoLog", "reports");
        Directory.CreateDirectory(folder);

        var bytes = File.ReadAllBytes(pdfPath);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        var hash8 = hash.Length >= 8 ? hash[..8] : hash;

        var date = ExtractDateFromName(Path.GetFileName(pdfPath)) ?? DateTime.Today;
        var panelSlug = ExtractPanelFromName(Path.GetFileNameWithoutExtension(pdfPath));
        var patientSlug = Slug(SelectedPatient?.DisplayName ?? "patient");
        var filename = $"{date:yyyy-MM-dd}_{panelSlug}_{patientSlug}_{hash8}.json";

        var reportId = $"{date:yyyy-MM-dd}_{hash8}_{panelSlug}";
        var nowUtc = DateTime.UtcNow;

        var output = new
        {
            schema_version = "1.0",
            report = new
            {
                report_id = reportId,
                patient_id = Guid.NewGuid().ToString(),
                source_file_id = Guid.NewGuid().ToString(),
                source_pdf_hash = hash,
                provider = new
                {
                    lab_provider_name = (string?)null,
                    provider_trading_name = (string?)null,
                    provider_abn = (string?)null,
                    provider_phone = (string?)null,
                    provider_website = (string?)null,
                    nata_numbers = Array.Empty<string>(),
                    generator_system = (string?)null,
                    instrument_report_level = (string?)null
                },
                patient = new
                {
                    external_patient_key = (string?)null,
                    full_name = SelectedPatient?.DisplayName ?? "Unknown",
                    dob = (string?)null,
                    sex = "unknown",
                    lab_id = (string?)null,
                    address_text = (string?)null,
                    phone_text = (string?)null
                },
                clinician = new
                {
                    referrer_name = (string?)null,
                    referrer_ref = (string?)null,
                    copy_to = Array.Empty<object>()
                },
                timestamps = new
                {
                    requested_date = (string?)null,
                    collection_datetime_local = (string?)null,
                    received_datetime_local = (string?)null,
                    reported_datetime_local = (string?)null,
                    document_created_datetime_local = (string?)null,
                    time_zone = (string?)null
                },
                report_meta = new
                {
                    report_type = "single_panel_table",
                    panel_name_original = panelSlug,
                    specimen_original = (string?)null,
                    page_count = (int?)null,
                    raw_text_extraction_method = "pdf_text",
                    raw_text = (string?)null,
                    parsing_version = "wpf-cli-1.0",
                    parsing_status = "completed",
                    extraction_confidence_overall = "medium"
                },
                clinical_notes = Array.Empty<object>(),
                subpanels = Array.Empty<object>(),
                results = results.Select(r => new
                {
                    result_id = Guid.NewGuid().ToString(),
                    subpanel_id = (string?)null,
                    analyte_name_original = r.Analyte,
                    analyte_short_code = Slug(r.Analyte).Replace("-", "").ToUpperInvariant().PadRight(2, 'X').Substring(0, Math.Min(5, Slug(r.Analyte).Length > 0 ? Slug(r.Analyte).Length : 2)),
                    analyte_code_standard_system = "unknown",
                    analyte_code_standard_value = (string?)null,
                    analyte_group = (string?)null,
                    mapping_method = "generated",
                    mapping_confidence = "medium",
                    result_type = "qualitative",
                    value_numeric = double.TryParse(r.Value, out var d) ? d : (double?)null,
                    value_text = r.Value,
                    unit_original = r.Unit,
                    unit_normalised = r.Unit,
                    censored = false,
                    censor_operator = "none",
                    flag_abnormal = (bool?)null,
                    flag_severity = "unknown",
                    reference_range = new
                    {
                        ref_low = (double?)null,
                        ref_high = (double?)null,
                        ref_text = (string?)null,
                        reference_range_context = (string?)null
                    },
                    collection_context = (string?)null,
                    specimen = new { specimen_text = (string?)null, specimen_container = (string?)null, preservative = (string?)null },
                    method = (string?)null,
                    microbiology = new { organism_name = (string?)null, target_group = (string?)null, target_taxon_rank = (string?)null, detection_status = (string?)null },
                    narrative = new { morphology_comment = (string?)null, comment_text = (string?)null, comment_scope = (string?)null },
                    timing = new { collected_datetime_local = (string?)null, reported_datetime_local = (string?)null, lab_number = (string?)null },
                    audit = new { source_anchor = (string?)null, extraction_confidence = "medium" }
                }).ToList(),
                cumulative_series = Array.Empty<object>(),
                administrative_events = Array.Empty<object>(),
                review_tasks = reviews.Select(t => new
                {
                    review_task_id = Guid.NewGuid().ToString(),
                    task_type = "unknown",
                    status = "open",
                    payload_json = new { detail = t.Reason },
                    created_at_utc = nowUtc,
                    resolved_at_utc = (DateTime?)null,
                    resolved_by_user_id = (string?)null
                }).ToList()
            }
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folder, filename), json);
    }

    private static DateTime? ExtractDateFromName(string fileName)
    {
        var match = Regex.Match(fileName, @"(?<y>20\d{2})[-_](?<m>\d{2})[-_](?<d>\d{2})");
        if (match.Success &&
            int.TryParse(match.Groups["y"].Value, out var y) &&
            int.TryParse(match.Groups["m"].Value, out var m) &&
            int.TryParse(match.Groups["d"].Value, out var d))
        {
            if (DateTime.TryParse($"{y}-{m}-{d}", out var dt)) return dt;
        }
        return null;
    }

    private static string ExtractPanelFromName(string name)
    {
        var withoutDate = Regex.Replace(name, @"^20\d{2}[-_]\d{2}[-_]\d{2}\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(withoutDate)) return "report";

        var tokens = withoutDate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "blood", "test", "tests", "results", "for" };
        var panelTokens = tokens.Take(4).Where(t => !stopWords.Contains(t)).ToList();
        if (panelTokens.Count == 0) panelTokens = tokens.Take(2).ToList();
        return Slug(string.Join(" ", panelTokens));
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
