using System.IO;
using System.Text.Json;

namespace PathoLog.Wpf.Services;

public sealed class ReportTemplateStore
{
    private readonly string _templatePath;

    public ReportTemplateStore()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "PathoLog");
        Directory.CreateDirectory(folder);
        _templatePath = Path.Combine(folder, "report-template.json");
    }

    public string LoadOrSeed()
    {
        if (File.Exists(_templatePath))
        {
            var existing = File.ReadAllText(_templatePath);
            if (!string.IsNullOrWhiteSpace(existing) && !existing.Contains("\"raw_text\""))
            {
                return existing;
            }
            var updated = BuildDefaultTemplate();
            File.WriteAllText(_templatePath, updated);
            return updated;
        }

        var template = BuildDefaultTemplate();
        File.WriteAllText(_templatePath, template);
        return template;
    }

    public void Save(string templateJson)
    {
        File.WriteAllText(_templatePath, templateJson);
    }

    public string GetDefaultTemplate() => BuildDefaultTemplate();

    private static string BuildDefaultTemplate()
    {
        var template = new
        {
            schema_version = "1.0",
            report = new
            {
                report_id = "YYYY-MM-DD_<unique_key>_<panel_slug>",
                patient_id = "uuid",
                source_file_id = "uuid",
                source_pdf_hash = "string",
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
                    full_name = "string",
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
                    panel_name_original = (string?)null,
                    specimen_original = (string?)null,
                    page_count = (int?)null,
                    raw_text_extraction_method = "pdf_text",
                    parsing_version = "wpf-1.0",
                    parsing_status = "pending",
                    extraction_confidence_overall = "medium"
                },
                clinical_notes = Array.Empty<object>(),
                subpanels = Array.Empty<object>(),
                results = Array.Empty<object>(),
                cumulative_series = Array.Empty<object>(),
                administrative_events = Array.Empty<object>(),
                review_tasks = Array.Empty<object>()
            }
        };

        return JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
    }
}
