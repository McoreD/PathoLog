using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PathoLog.Wpf.Services;
using PathoLog.Wpf.Dialogs;
using UglyToad.PdfPig;

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
    private readonly Dictionary<string, string> _reportJsonPaths = new();
    private readonly Dictionary<string, string> _reportPdfPaths = new();
    private AppSettings _settings;
    private bool _isImporting;
    private const string OpenAiModel = "gpt-4o";
    private const string GeminiModel = "gemini-2.5-pro";
    public ObservableCollection<AiOption> AiProviders { get; } = new();

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
                ApplyReportData(value?.Id);
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
    public string ModelInfo => $"Models: OpenAI={OpenAiModel}, Gemini={GeminiModel}";
    private Uri? _pdfViewerSource;
    public Uri? PdfViewerSource
    {
        get => _pdfViewerSource;
        private set
        {
            if (Equals(_pdfViewerSource, value)) return;
            _pdfViewerSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPdfSource));
        }
    }
    public bool HasPdfSource => PdfViewerSource != null;
    public string SelectedAiProviderId
    {
        get => _settings.PreferredAiProvider ?? "openai";
        set
        {
            if (value == _settings.PreferredAiProvider) return;
            _settings.PreferredAiProvider = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        _settings = _settingsStore.Load();
        SelectPdfCommand = new RelayCommand(_ => SelectPdf());
        ImportPdfCommand = new RelayCommand(async _ => await ImportPdfAsync(), _ => CanImport());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        NewPatientCommand = new RelayCommand(_ => CreatePatient());
        LoadReportJsonCommand = new RelayCommand(_ => LoadReportJson());

        AiProviders.Add(new AiOption("openai", $"OpenAI ({OpenAiModel})"));
        AiProviders.Add(new AiOption("gemini", $"Gemini ({GeminiModel})"));

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
                Results.Clear();
                Results.Add(new ResultRow("Uploaded PDF", "Pending parse", file, "Queued"));
                ImportStatus = $"Reading {System.IO.Path.GetFileName(file)}";

                var extraction = await ExtractReportAsync(file);
                var jsonPath = SaveReportJsonToDisk(file, extraction, out var reportId);

                var reportDate = ExtractDateFromName(System.IO.Path.GetFileName(file)) ?? DateTime.Today;
                var panelSlug = ExtractPanelFromName(System.IO.Path.GetFileNameWithoutExtension(file));
                var report = new ReportSummary(reportId, reportDate.ToString("yyyy-MM-dd"), panelSlug);
                Reports.Insert(0, report);
                SelectedReport = report;

                _reportJsonPaths[report.Id] = jsonPath;
                _reportPdfPaths[report.Id] = file;
                ApplyReportData(report.Id);

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

            var reportId = parsed.Report.ReportId ?? Guid.NewGuid().ToString();
            _reportJsonPaths[reportId] = dialog.FileName;

            if (Reports.All(r => r.Id != reportId))
            {
                Reports.Add(new ReportSummary(reportId, DateTime.Now.ToString("yyyy-MM-dd"), Path.GetFileName(dialog.FileName)));
            }
            SelectedReport = Reports.FirstOrDefault(r => r.Id == reportId);

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

    private string SaveReportJsonToDisk(string pdfPath, ParsedExtraction extraction, out string reportId)
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

        reportId = $"{date:yyyy-MM-dd}_{hash8}_{panelSlug}";
        var nowUtc = DateTime.UtcNow;
        var reportType = MapReportType(extraction.ReportType);

        var parsingStatus = extraction.ReviewTasks.Any() || extraction.Results.Any(r => r.ExtractionConfidence == "low")
            ? "needs_review"
            : "completed";

        var output = new
        {
            schema_version = "1.0",
            report = new
            {
                report_id = reportId,
                patient_id = SelectedPatient?.Id ?? Guid.NewGuid().ToString(),
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
                    report_type = reportType,
                    panel_name_original = panelSlug,
                    specimen_original = (string?)null,
                    page_count = extraction.PageCount,
                    raw_text_extraction_method = extraction.RawTextMethod,
                    raw_text = string.IsNullOrWhiteSpace(extraction.RawText) ? null : extraction.RawText,
                    parsing_version = "wpf-1.0",
                    parsing_status = parsingStatus,
                    extraction_confidence_overall = InferOverallConfidence(extraction.Results)
                },
                clinical_notes = Array.Empty<object>(),
                subpanels = Array.Empty<object>(),
                results = extraction.Results.Select(r => new
                {
                    result_id = Guid.NewGuid().ToString(),
                    subpanel_id = (string?)null,
                    analyte_name_original = r.AnalyteNameOriginal ?? "Analyte",
                    analyte_short_code = string.IsNullOrWhiteSpace(r.AnalyteShortCode) ? ToShortCode(r.AnalyteNameOriginal ?? "Analyte") : r.AnalyteShortCode,
                    analyte_code_standard_system = "unknown",
                    analyte_code_standard_value = (string?)null,
                    analyte_group = (string?)null,
                    mapping_method = "generated",
                    mapping_confidence = r.ExtractionConfidence ?? "medium",
                    result_type = string.IsNullOrWhiteSpace(r.ResultType) ? (r.ValueNumeric.HasValue ? "numeric" : "qualitative") : r.ResultType,
                    value_numeric = r.ValueNumeric,
                    value_text = r.ValueText,
                    unit_original = r.UnitOriginal,
                    unit_normalised = r.UnitNormalised ?? r.UnitOriginal,
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
                    audit = new { source_anchor = r.SourceAnchor, extraction_confidence = r.ExtractionConfidence ?? "medium" }
                }).ToList(),
                cumulative_series = Array.Empty<object>(),
                administrative_events = Array.Empty<object>(),
                review_tasks = extraction.ReviewTasks.Select(t => new
                {
                    review_task_id = Guid.NewGuid().ToString(),
                    task_type = ResolveReviewTaskType(t.Reason),
                    status = "open",
                    payload_json = new { field_path = t.FieldPath, reason = t.Reason },
                    created_at_utc = nowUtc,
                    resolved_at_utc = (DateTime?)null,
                    resolved_by_user_id = (string?)null
                }).ToList()
            }
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        var outPath = Path.Combine(folder, filename);
        File.WriteAllText(outPath, json);
        return outPath;
    }

    private async Task<ParsedExtraction> ExtractReportAsync(string pdfPath)
    {
        ImportStatus = "Extracting text...";
        var (text, pages) = ExtractTextFromPdf(pdfPath);
        var rawMethod = string.IsNullOrWhiteSpace(text) ? "ocr" : "pdf_text";

        ImportStatus = "Running AI parser...";
        AiParseResult? aiResult = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            aiResult = await ExtractWithPreferredAiAsync(text);
        }

        aiResult ??= ExtractHeuristic(text);

        ImportStatus = "Normalising results...";
        return new ParsedExtraction(
            aiResult.Results,
            aiResult.ReviewTasks,
            text,
            aiResult.ReportType,
            pages,
            rawMethod
        );
    }

    private static (string Text, int PageCount) ExtractTextFromPdf(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pages = doc.NumberOfPages;
            var text = string.Join("\n", doc.GetPages().Select(p => p.Text)).Trim();
            return (text, pages);
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    private async Task<AiParseResult?> ExtractWithPreferredAiAsync(string text)
    {
        var preferred = SelectedAiProviderId;
        if (preferred == "gemini")
        {
            var gemini = await ExtractWithGeminiAsync(text, GeminiApiKey);
            if (gemini != null) return gemini;
            return await ExtractWithOpenAiAsync(text, OpenAiApiKey);
        }

        var openAi = await ExtractWithOpenAiAsync(text, OpenAiApiKey);
        if (openAi != null) return openAi;
        return await ExtractWithGeminiAsync(text, GeminiApiKey);
    }

    private static async Task<AiParseResult?> ExtractWithOpenAiAsync(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var pass1Payload = new
            {
                model = OpenAiModel,
                temperature = 0.1,
                messages = new object[]
                {
                    new { role = "system", content = "You are parsing an Australian pathology PDF report." },
                    new
                    {
                        role = "user",
                        content = $@"Task
1. Classify the report type. Choose one
blood_chemistry
haematology
endocrine
immunology
microbiology_pcr
microbiology_culture
urine
faeces
histology
administrative_only

2. Identify the main results table or list. Return page number and the exact header text above it.

3. Extract patient identifiers and report timestamps.

4. Return a short glossary of analytes or targets found. Keep original spelling.

Rules
- Output JSON only.
- If a field is not present, use null.
- Provide source_anchor values like page_1_table_iron_studies or page_2_section_microbiology.

Text:
{text}"
                    }
                }
            };

            var pass1Json = await SendOpenAiAsync(client, pass1Payload);
            var reportType = ParseReportType(pass1Json);

            var pass2Payload = new
            {
                model = OpenAiModel,
                temperature = 0.1,
                messages = new object[]
                {
                    new { role = "system", content = "You are extracting structured pathology results from an Australian lab PDF." },
                    new
                    {
                        role = "user",
                        content = $@"Context
This report is of type <report_type_from_pass_1>. It may contain
- results in tables with columns like Test Result Units Reference range Flag
- multiple subpanels on one report
- cumulative tables with historical rows
- microbiology targets with Detected or Not Detected
- narrative comments or specimen notes
- tests not performed

Pass 1 JSON:
{pass1Json ?? "null"}

Task
Populate this JSON schema exactly. Do not add keys. Do not rename keys.
For each result row, capture
- analyte_name_original exactly as printed
- unit_original exactly as printed
- reference range exactly as printed
- abnormal flags if shown
- detection status for microbiology
- censored values like < 0.03 using censored=true and censor_operator=lt
- create analyte_short_code if absent in the PDF. Use 2 to 5 characters. Prefer common clinical abbreviations. Store mapping_method=generated and mapping_confidence.

Output requirements
- JSON only
- Keep numeric values as numbers
- Keep value_text as the exact printed text
- source_anchor per row and per section
- extraction_confidence per row
- If a requested test was not performed, add an administrative_event and do not invent results.

Schema:
{{
  ""results"": [
    {{
      ""analyte_name_original"": string,
      ""analyte_short_code"": string,
      ""result_type"": ""numeric"" | ""qualitative"",
      ""value_numeric"": number | null,
      ""value_text"": string | null,
      ""unit_original"": string | null,
      ""extraction_confidence"": ""high"" | ""medium"" | ""low"",
      ""source_anchor"": string | null
    }}
  ],
  ""review_tasks"": [
    {{ ""field_path"": string, ""reason"": string }}
  ]
}}

Text:
{text}"
                    }
                }
            };

            var pass2Json = await SendOpenAiAsync(client, pass2Payload);
            if (string.IsNullOrWhiteSpace(pass2Json))
            {
                return null;
            }

            return ParseAiResult(pass2Json, reportType);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<AiParseResult?> ExtractWithGeminiAsync(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient();
            var pass1Payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new
                            {
                                text =
$@"You are parsing an Australian pathology PDF report.

Task
1. Classify the report type. Choose one
blood_chemistry
haematology
endocrine
immunology
microbiology_pcr
microbiology_culture
urine
faeces
histology
administrative_only

2. Identify the main results table or list. Return page number and the exact header text above it.

3. Extract patient identifiers and report timestamps.

4. Return a short glossary of analytes or targets found. Keep original spelling.

Rules
- Output JSON only.
- If a field is not present, use null.
- Provide source_anchor values like page_1_table_iron_studies or page_2_section_microbiology.

Text:
{text}"
                            }
                        }
                    }
                },
                generationConfig = new { temperature = 0.1 }
            };

            var pass1Json = await SendGeminiAsync(client, apiKey, pass1Payload);
            var reportType = ParseReportType(pass1Json);

            var pass2Payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new
                            {
                                text =
$@"You are extracting structured pathology results from an Australian lab PDF.

Context
This report is of type <report_type_from_pass_1>. It may contain
- results in tables with columns like Test Result Units Reference range Flag
- multiple subpanels on one report
- cumulative tables with historical rows
- microbiology targets with Detected or Not Detected
- narrative comments or specimen notes
- tests not performed

Pass 1 JSON:
{pass1Json ?? "null"}

Task
Populate this JSON schema exactly. Do not add keys. Do not rename keys.
For each result row, capture
- analyte_name_original exactly as printed
- unit_original exactly as printed
- reference range exactly as printed
- abnormal flags if shown
- detection status for microbiology
- censored values like < 0.03 using censored=true and censor_operator=lt
- create analyte_short_code if absent in the PDF. Use 2 to 5 characters. Prefer common clinical abbreviations. Store mapping_method=generated and mapping_confidence.

Output requirements
- JSON only
- Keep numeric values as numbers
- Keep value_text as the exact printed text
- source_anchor per row and per section
- extraction_confidence per row
- If a requested test was not performed, add an administrative_event and do not invent results.

Schema:
{{
  ""results"": [
    {{
      ""analyte_name_original"": string,
      ""analyte_short_code"": string,
      ""result_type"": ""numeric"" | ""qualitative"",
      ""value_numeric"": number | null,
      ""value_text"": string | null,
      ""unit_original"": string | null,
      ""extraction_confidence"": ""high"" | ""medium"" | ""low"",
      ""source_anchor"": string | null
    }}
  ],
  ""review_tasks"": [
    {{ ""field_path"": string, ""reason"": string }}
  ]
}}

Text:
{text}"
                            }
                        }
                    }
                },
                generationConfig = new { temperature = 0.1 }
            };

            var pass2Json = await SendGeminiAsync(client, apiKey, pass2Payload);
            if (string.IsNullOrWhiteSpace(pass2Json))
            {
                return null;
            }

            return ParseAiResult(pass2Json, reportType);
        }
        catch
        {
            return null;
        }
    }

    private static AiParseResult ExtractHeuristic(string text)
    {
        var results = new List<ParsedResultJson>();
        var reviewTasks = new List<ParsedReviewTaskJson>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numberPattern = new Regex(@"^([A-Za-z][\w /\-%()]{2,})\s+([<>]?\d+(?:\.\d+)?)(?:\s*([A-Za-zć/%-]+))?", RegexOptions.Compiled);
        var unitGuess = new Regex("[A-Za-zć/%-]{1,6}$", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var match = numberPattern.Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                var rawVal = match.Groups[2].Value.Trim();
                var unit = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
                var numeric = double.TryParse(rawVal.TrimStart('<', '>'), out var d) ? d : (double?)null;
                var shortCode = ToShortCode(name);
                var confidence = numeric is null ? "low" : "medium";

                results.Add(new ParsedResultJson
                {
                    AnalyteNameOriginal = name,
                    AnalyteShortCode = shortCode,
                    ResultType = "numeric",
                    ValueNumeric = numeric,
                    ValueText = rawVal,
                    UnitOriginal = unit,
                    ExtractionConfidence = confidence
                });

                if (string.IsNullOrWhiteSpace(unit))
                {
                    reviewTasks.Add(new ParsedReviewTaskJson { FieldPath = $"result:{name}:unit", Reason = "Unit missing" });
                }
                if (confidence == "low")
                {
                    reviewTasks.Add(new ParsedReviewTaskJson { FieldPath = $"result:{name}", Reason = "Low confidence extraction" });
                }
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length > 2 && char.IsLetter(parts[0][0]))
            {
                var maybeUnit = unitGuess.Match(parts[^1]);
                var unit = maybeUnit.Success ? maybeUnit.Value : null;
                var name = string.Join(' ', parts.Take(Math.Max(1, parts.Length - 1)));
                var shortCode = ToShortCode(name);
                results.Add(new ParsedResultJson
                {
                    AnalyteNameOriginal = name,
                    AnalyteShortCode = shortCode,
                    ResultType = "qualitative",
                    ValueText = unit is null ? parts[^1] : string.Join(' ', parts.SkipLast(1)),
                    UnitOriginal = unit,
                    ExtractionConfidence = "low"
                });
                reviewTasks.Add(new ParsedReviewTaskJson { FieldPath = $"result:{name}", Reason = "Low confidence extraction" });
            }
            if (results.Count >= 50) break;
        }

        if (results.Count == 0)
        {
            results.Add(new ParsedResultJson
            {
                AnalyteNameOriginal = "Report imported",
                AnalyteShortCode = "PDF",
                ResultType = "qualitative",
                ValueText = "Parsed text captured",
                ExtractionConfidence = "low"
            });
            reviewTasks.Add(new ParsedReviewTaskJson { FieldPath = "result:Report imported", Reason = "Low confidence extraction" });
            reviewTasks.Add(new ParsedReviewTaskJson { FieldPath = "result:Report imported:unit", Reason = "Unit missing" });
        }

        results = results
            .GroupBy(r => r.AnalyteNameOriginal?.Trim() ?? "")
            .Select(g => g.First())
            .OrderBy(r => r.AnalyteNameOriginal)
            .ToList();

        return new AiParseResult(results, reviewTasks, null);
    }

    private static AiParseResult ParseAiResult(string json, string? reportType)
    {
        using var parsed = JsonDocument.Parse(json);
        var results = new List<ParsedResultJson>();
        var tasks = new List<ParsedReviewTaskJson>();

        if (parsed.RootElement.TryGetProperty("results", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rEl.EnumerateArray())
            {
                results.Add(new ParsedResultJson
                {
                    AnalyteNameOriginal = r.GetPropertyOrDefault("analyte_name_original"),
                    AnalyteShortCode = r.GetPropertyOrDefault("analyte_short_code"),
                    ResultType = r.GetPropertyOrDefault("result_type") ?? "qualitative",
                    ValueNumeric = r.GetPropertyOrDefaultDouble("value_numeric"),
                    ValueText = r.GetPropertyOrDefault("value_text"),
                    UnitOriginal = r.GetPropertyOrDefault("unit_original"),
                    UnitNormalised = r.GetPropertyOrDefault("unit_normalised"),
                    ExtractionConfidence = r.GetPropertyOrDefault("extraction_confidence") ?? "medium",
                    SourceAnchor = r.GetPropertyOrDefault("source_anchor")
                });
            }
        }

        if (parsed.RootElement.TryGetProperty("review_tasks", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tEl.EnumerateArray())
            {
                tasks.Add(new ParsedReviewTaskJson
                {
                    FieldPath = t.GetPropertyOrDefault("field_path") ?? "",
                    Reason = t.GetPropertyOrDefault("reason") ?? ""
                });
            }
        }

        foreach (var r in results)
        {
            if (string.IsNullOrWhiteSpace(r.AnalyteShortCode))
            {
                r.AnalyteShortCode = ToShortCode(r.AnalyteNameOriginal ?? "Analyte");
                tasks.Add(new ParsedReviewTaskJson { FieldPath = $"result:{r.AnalyteNameOriginal}:analyte_short_code", Reason = "Short code missing" });
            }
        }

        return new AiParseResult(results, tasks, reportType);
    }

    private static string? ParseReportType(string? pass1Json)
    {
        if (string.IsNullOrWhiteSpace(pass1Json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(pass1Json);
            if (doc.RootElement.TryGetProperty("report_type", out var rt) && rt.ValueKind == JsonValueKind.String)
            {
                return rt.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string MapReportType(string? reportType)
    {
        return reportType switch
        {
            "microbiology_pcr" => "multi_panel",
            "microbiology_culture" => "multi_panel",
            "administrative_only" => "narrative_only",
            _ => "single_panel_table"
        };
    }

    private static string ResolveReviewTaskType(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "unknown";
        var lowered = reason.ToLowerInvariant();
        if (lowered.Contains("short code") || lowered.Contains("short_code")) return "missing_short_code";
        if (lowered.Contains("unit")) return "unit_mismatch";
        if (lowered.Contains("range")) return "range_context";
        if (lowered.Contains("confidence")) return "low_confidence";
        return "unknown";
    }

    private static string ToShortCode(string name)
    {
        var letters = string.Concat(name.Where(char.IsLetter)).ToUpperInvariant();
        if (letters.Length >= 2 && letters.Length <= 5) return letters;
        if (letters.Length > 5) return letters[..5];
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
        return string.IsNullOrWhiteSpace(initials) ? "RS" : initials;
    }

    private static string InferOverallConfidence(IEnumerable<ParsedResultJson> results)
    {
        var list = results?.ToList() ?? new List<ParsedResultJson>();
        if (!list.Any()) return "low";
        if (list.Any(r => r.ExtractionConfidence == "low")) return "medium";
        return "high";
    }

    private static async Task<string?> SendOpenAiAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static async Task<string?> SendGeminiAsync(HttpClient client, string apiKey, object payload)
    {
        var response = await client.PostAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={apiKey}",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
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

    private void ApplyReportData(string? reportId)
    {
        Results.Clear();
        ReviewTasks.Clear();
        PdfViewerSource = null;
        if (string.IsNullOrWhiteSpace(reportId)) return;
        if (_reportPdfPaths.TryGetValue(reportId, out var pdfPath) && File.Exists(pdfPath))
        {
            PdfViewerSource = new Uri(pdfPath, UriKind.Absolute);
        }
        if (!_reportJsonPaths.TryGetValue(reportId, out var path) || !File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<ParsedReportWrapper>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed?.Report is null) return;

            foreach (var r in parsed.Report.Results ?? Array.Empty<ParsedResultJson>())
            {
                Results.Add(new ResultRow(
                    r.AnalyteNameOriginal ?? r.AnalyteShortCode ?? "Analyte",
                    r.ValueText ?? (r.ValueNumeric?.ToString("0.###") ?? ""),
                    r.UnitOriginal ?? r.UnitNormalised ?? "",
                    r.ExtractionConfidence ?? ""));
            }

            foreach (var t in parsed.Report.ReviewTasks ?? Array.Empty<ParsedReviewTaskJson>())
            {
                var field = t.FieldPath ?? t.Payload?.FieldPath ?? t.TaskType ?? "field";
                var reason = t.Reason ?? t.Payload?.Reason ?? "needs review";
                ReviewTasks.Add(new ReviewTaskRow(field, reason));
            }
        }
        catch
        {
            // ignore load errors
        }

        OnPropertyChanged(nameof(PendingReviewsCount));
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
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("report")]
    public ParsedReportJson? Report { get; set; }
}

public sealed class ParsedReportJson
{
    [JsonPropertyName("report_id")]
    public string? ReportId { get; set; }

    [JsonPropertyName("results")]
    public ParsedResultJson[]? Results { get; set; }

    [JsonPropertyName("review_tasks")]
    public ParsedReviewTaskJson[]? ReviewTasks { get; set; }
}

public sealed class ParsedResultJson
{
    [JsonPropertyName("analyte_name_original")]
    public string? AnalyteNameOriginal { get; set; }
    [JsonPropertyName("analyte_short_code")]
    public string? AnalyteShortCode { get; set; }
    [JsonPropertyName("result_type")]
    public string? ResultType { get; set; }
    [JsonPropertyName("value_text")]
    public string? ValueText { get; set; }
    [JsonPropertyName("value_numeric")]
    public double? ValueNumeric { get; set; }
    [JsonPropertyName("unit_original")]
    public string? UnitOriginal { get; set; }
    [JsonPropertyName("unit_normalised")]
    public string? UnitNormalised { get; set; }
    [JsonPropertyName("extraction_confidence")]
    public string? ExtractionConfidence { get; set; }
    [JsonPropertyName("source_anchor")]
    public string? SourceAnchor { get; set; }
}

public sealed class ParsedReviewTaskJson
{
    [JsonPropertyName("field_path")]
    public string? FieldPath { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("task_type")]
    public string? TaskType { get; set; }
    [JsonPropertyName("payload_json")]
    public ReviewTaskPayload? Payload { get; set; }
}

public sealed record AiOption(string Id, string Label);

public sealed class ReviewTaskPayload
{
    [JsonPropertyName("field_path")]
    public string? FieldPath { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed record ParsedExtraction(
    List<ParsedResultJson> Results,
    List<ParsedReviewTaskJson> ReviewTasks,
    string RawText,
    string? ReportType,
    int PageCount,
    string RawTextMethod);

public sealed record AiParseResult(
    List<ParsedResultJson> Results,
    List<ParsedReviewTaskJson> ReviewTasks,
    string? ReportType);

internal static class JsonExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind == JsonValueKind.Number) return p.ToString();
        }
        return null;
    }

    public static double? GetPropertyOrDefaultDouble(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
        {
            if (p.TryGetDouble(out var d)) return d;
        }
        return null;
    }
}
