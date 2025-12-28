using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace PathoLog.Wpf.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var argsDict = ParseArgs(args);
        if (!argsDict.TryGetValue("file", out var filePath))
        {
            Console.WriteLine("Usage: dotnet run --project apps/wpf-cli/PathoLog.Wpf.Cli.csproj -- --file <pdf> [--patient \"Name\"] [--email you@example.com] [--save] [--show-text]");
            return 1;
        }

        filePath = Path.GetFullPath(filePath);
        var patientName = argsDict.GetValueOrDefault("patient") ?? "CLI Patient";
        var email = argsDict.GetValueOrDefault("email") ?? "cli@patholog.dev";
        var save = argsDict.ContainsKey("save");
        var showText = argsDict.ContainsKey("show-text");

        Console.WriteLine("PathoLog WPF CLI");
        Console.WriteLine($"File   : {filePath}");
        Console.WriteLine($"Patient: {patientName}");
        Console.WriteLine($"Email  : {email}");
        Console.WriteLine($"Mode   : {(save ? "save" : "dry-run")}");

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return 1;
        }

        string text;
        int pages;
        try
        {
            using var doc = PdfDocument.Open(filePath);
            pages = doc.NumberOfPages;
            text = string.Join("\n", doc.GetPages().Select(p => p.Text));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read PDF: {ex.Message}");
            return 1;
        }

        text = text.Trim();
        Console.WriteLine($"Pages  : {pages}");
        Console.WriteLine($"Text   : {text.Length} characters");

        if (showText)
        {
            Console.WriteLine("--- Text preview (first 800 chars) ---");
            Console.WriteLine(text.Length > 800 ? text[..800] : text);
            Console.WriteLine("--- end preview ---");
        }

        var settings = LoadSettings();
        var parsed = await ExtractWithAiOrFallback(text, settings);

        Console.WriteLine($"Extracted results: {parsed.results.Count} (review tasks: {parsed.reviewTasks.Count})");
        foreach (var r in parsed.results.Take(5))
        {
            Console.WriteLine($" - {r.AnalyteNameOriginal} => {r.ValueText ?? r.ValueNumeric?.ToString() ?? "n/a"} {r.UnitOriginal ?? ""} ({r.ResultType}) [{r.ExtractionConfidence}]");
        }

        if (!save)
        {
            Console.WriteLine("Dry-run complete. Use --save to write JSON output locally.");
            return 0;
        }

        var fileHash = ComputeSha256(File.ReadAllBytes(filePath));
        var contentFingerprint = ComputeSha256(Encoding.UTF8.GetBytes(text + "|" + parsed.results.Count));

        var panelSlug = ExtractPanelFromName(Path.GetFileNameWithoutExtension(filePath));
        var patientSlug = Slugify(patientName);
        var dateForId = ExtractDateFromName(Path.GetFileName(filePath)) ?? DateTime.Today;
        var hash8 = fileHash.Length >= 8 ? fileHash[..8] : fileHash;
        var reportId = $"{dateForId:yyyy-MM-dd}_{hash8}_{panelSlug}";

        var nowUtc = DateTime.UtcNow;

        var output = new
        {
            schema_version = "1.0",
            report = new
            {
                report_id = reportId,
                patient_id = Guid.NewGuid().ToString(),
                source_file_id = Guid.NewGuid().ToString(),
                source_pdf_hash = fileHash,
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
                    full_name = patientName,
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
                    page_count = pages,
                    raw_text_extraction_method = text.Length > 0 ? "pdf_text" : "ocr",
                    raw_text = text,
                    parsing_version = "cli-1.0",
                    parsing_status = "completed",
                    extraction_confidence_overall = InferOverallConfidence(parsed.results)
                },
                clinical_notes = Array.Empty<object>(),
                subpanels = Array.Empty<object>(),
                results = parsed.results.Select(r => new
                {
                    result_id = Guid.NewGuid().ToString(),
                    subpanel_id = (string?)null,
                    analyte_name_original = r.AnalyteNameOriginal ?? "Analyte",
                    analyte_short_code = r.AnalyteShortCode,
                    analyte_code_standard_system = "unknown",
                    analyte_code_standard_value = (string?)null,
                    analyte_group = (string?)null,
                    mapping_method = "generated",
                    mapping_confidence = r.ExtractionConfidence ?? "medium",
                    result_type = r.ResultType,
                    value_numeric = r.ValueNumeric,
                    value_text = r.ValueText,
                    unit_original = r.UnitOriginal,
                    unit_normalised = r.UnitNormalised,
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
                    specimen = new
                    {
                        specimen_text = (string?)null,
                        specimen_container = (string?)null,
                        preservative = (string?)null
                    },
                    method = (string?)null,
                    microbiology = new
                    {
                        organism_name = (string?)null,
                        target_group = (string?)null,
                        target_taxon_rank = (string?)null,
                        detection_status = (string?)null
                    },
                    narrative = new
                    {
                        morphology_comment = (string?)null,
                        comment_text = (string?)null,
                        comment_scope = (string?)null
                    },
                    timing = new
                    {
                        collected_datetime_local = (string?)null,
                        reported_datetime_local = (string?)null,
                        lab_number = (string?)null
                    },
                    audit = new
                    {
                        source_anchor = (string?)null,
                        extraction_confidence = r.ExtractionConfidence ?? "medium"
                    }
                }).ToList(),
                cumulative_series = Array.Empty<object>(),
                administrative_events = Array.Empty<object>(),
                review_tasks = parsed.reviewTasks.Select(t => new
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

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "PathoLog", "cli-reports");
        Directory.CreateDirectory(folder);
        var outfile = Path.Combine(folder, BuildFileName(filePath, patientName, parsed.results, contentFingerprint));
        await File.WriteAllTextAsync(outfile, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved structured JSON to {outfile}");
        return 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < raw.Length; i++)
        {
            var arg = raw[i];
            if (!arg.StartsWith("--")) continue;
            var key = arg[2..];
            string? val = null;
            if (i + 1 < raw.Length && !raw[i + 1].StartsWith("--"))
            {
                val = raw[++i];
            }
            dict[key] = val ?? string.Empty;
        }
        return dict;
    }

    private static async Task<(List<ParsedResult> results, List<ReviewTask> reviewTasks)> ExtractWithAiOrFallback(string text, AppSettings settings)
    {
        var aiResult = await ExtractWithOpenAi(text, settings?.OpenAiApiKey);
        if (aiResult is not null)
        {
            return aiResult.Value;
        }

        aiResult = await ExtractWithGemini(text, settings?.GeminiApiKey);
        if (aiResult is not null)
        {
            return aiResult.Value;
        }

        return ExtractHeuristic(text);
    }

    private static (List<ParsedResult> results, List<ReviewTask> tasks) ExtractHeuristic(string text)
    {
        var list = new List<ParsedResult>();
        var tasks = new List<ReviewTask>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numberPattern = new Regex(@"^([A-Za-z][\w /\-%()]{2,})\s+([<>]?\d+(?:\.\d+)?)(?:\s*([A-Za-zµ/%-]+))?", RegexOptions.Compiled);
        var unitGuess = new Regex("[A-Za-zµ/%-]{1,6}$", RegexOptions.Compiled);

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

                list.Add(new ParsedResult
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
                    tasks.Add(new ReviewTask { FieldPath = $"result:{name}:unit", Reason = "Unit missing" });
                }
                if (confidence == "low")
                {
                    tasks.Add(new ReviewTask { FieldPath = $"result:{name}", Reason = "Low confidence extraction" });
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
                list.Add(new ParsedResult
                {
                    AnalyteNameOriginal = name,
                    AnalyteShortCode = shortCode,
                    ResultType = "qualitative",
                    ValueText = unit is null ? parts[^1] : string.Join(' ', parts.SkipLast(1)),
                    UnitOriginal = unit,
                    ExtractionConfidence = "low"
                });
                tasks.Add(new ReviewTask { FieldPath = $"result:{name}", Reason = "Low confidence extraction" });
            }
            if (list.Count >= 50) break;
        }

        if (list.Count == 0)
        {
            list.Add(new ParsedResult
            {
                AnalyteNameOriginal = "Report imported",
                AnalyteShortCode = "PDF",
                ResultType = "qualitative",
                ValueText = "Parsed text captured",
                ExtractionConfidence = "low"
            });
            tasks.Add(new ReviewTask { FieldPath = "result:Report imported", Reason = "Low confidence extraction" });
            tasks.Add(new ReviewTask { FieldPath = "result:Report imported:unit", Reason = "Unit missing" });
        }

        list = list
            .GroupBy(r => r.AnalyteNameOriginal.Trim())
            .Select(g => g.First())
            .OrderBy(r => r.AnalyteNameOriginal)
            .ToList();

        return (list, tasks);
    }

    private static string InferOverallConfidence(IEnumerable<ParsedResult> results)
    {
        if (!results.Any()) return "low";
        if (results.Any(r => r.ExtractionConfidence == "low")) return "medium";
        return "high";
    }

    private static async Task<(List<ParsedResult> results, List<ReviewTask> tasks)?> ExtractWithOpenAi(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var prompt = new
            {
                model = "gpt-4o",
                temperature = 0.1,
                messages = new object[]
                {
                    new { role = "system", content = "You are a pathology report parser. Extract performed analytes and results. Use the provided JSON schema. Do not hallucinate missing values." },
                    new
                    {
                        role = "user",
                        content = $@"Extract results from this report text. Return strict JSON only.
Schema:
{{
  ""results"": [
    {{
      ""analyte_name_original"": string,
      ""analyte_short_code"": string (2-5 letters, required; derive from name if missing),
      ""result_type"": ""numeric"" | ""qualitative"",
      ""value_numeric"": number | null,
      ""value_text"": string | null,
      ""unit_original"": string | null,
      ""extraction_confidence"": ""high"" | ""medium"" | ""low""
    }}
  ],
  ""review_tasks"": [
    {{ ""field_path"": string, ""reason"": string }}
  ]
}}
Rules:
- Prefer numeric values and units where present; include qualitative rows when numeric is absent.
- Do not invent analytes; only return those present in text.
- If unit is missing, keep it null and add a review task ""unit missing"".
- If confidence is low, add a review task for that analyte.
Text:
{text}"
                    }
                }
            };

            var resp = await client.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(prompt), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            using var parsed = JsonDocument.Parse(content);
            var results = new List<ParsedResult>();
            var tasks = new List<ReviewTask>();

            if (parsed.RootElement.TryGetProperty("results", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rEl.EnumerateArray())
                {
                    results.Add(new ParsedResult
                    {
                        AnalyteNameOriginal = r.GetPropertyOrDefault("analyte_name_original"),
                        AnalyteShortCode = r.GetPropertyOrDefault("analyte_short_code"),
                        ResultType = r.GetPropertyOrDefault("result_type") ?? "qualitative",
                        ValueNumeric = r.GetPropertyOrDefaultDouble("value_numeric"),
                        ValueText = r.GetPropertyOrDefault("value_text"),
                        UnitOriginal = r.GetPropertyOrDefault("unit_original"),
                        UnitNormalised = r.GetPropertyOrDefault("unit_normalised"),
                        ExtractionConfidence = r.GetPropertyOrDefault("extraction_confidence") ?? "medium"
                    });
                }
            }

            if (parsed.RootElement.TryGetProperty("review_tasks", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tEl.EnumerateArray())
                {
                    tasks.Add(new ReviewTask
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
                    r.AnalyteShortCode = ToShortCode(r.AnalyteNameOriginal);
                    tasks.Add(new ReviewTask { FieldPath = $"result:{r.AnalyteNameOriginal}:analyte_short_code", Reason = "Short code missing" });
                }
            }

            return (results, tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AI extraction failed, falling back to heuristic: {ex.Message}");
            return null;
        }
    }

    private static async Task<(List<ParsedResult> results, List<ReviewTask> tasks)?> ExtractWithGemini(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient();
            var payload = new
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
$@"Extract results from this report text. Return strict JSON only.
Schema:
{{
  ""results"": [
    {{
      ""analyte_name_original"": string,
      ""analyte_short_code"": string (2-5 letters, required; derive from name if missing),
      ""result_type"": ""numeric"" | ""qualitative"",
      ""value_numeric"": number | null,
      ""value_text"": string | null,
      ""unit_original"": string | null,
      ""extraction_confidence"": ""high"" | ""medium"" | ""low""
    }}
  ],
  ""review_tasks"": [
    {{ ""field_path"": string, ""reason"": string }}
  ]
}}
Rules:
- Prefer numeric values and units where present; include qualitative rows when numeric is absent.
- Do not invent analytes; only return those present in text.
- If unit is missing, keep it null and add a review task ""unit missing"".
- If confidence is low, add a review task for that analyte.
Text:
{text}"
                            }
                        }
                    }
                },
                generationConfig = new { temperature = 0.1 }
            };

            var resp = await client.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent?key={apiKey}",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var textContent = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(textContent))
            {
                return null;
            }

            using var parsed = JsonDocument.Parse(textContent);
            var results = new List<ParsedResult>();
            var tasks = new List<ReviewTask>();

            if (parsed.RootElement.TryGetProperty("results", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rEl.EnumerateArray())
                {
                    results.Add(new ParsedResult
                    {
                        AnalyteNameOriginal = r.GetPropertyOrDefault("analyte_name_original"),
                        AnalyteShortCode = r.GetPropertyOrDefault("analyte_short_code"),
                        ResultType = r.GetPropertyOrDefault("result_type") ?? "qualitative",
                        ValueNumeric = r.GetPropertyOrDefaultDouble("value_numeric"),
                        ValueText = r.GetPropertyOrDefault("value_text"),
                        UnitOriginal = r.GetPropertyOrDefault("unit_original"),
                        UnitNormalised = r.GetPropertyOrDefault("unit_normalised"),
                        ExtractionConfidence = r.GetPropertyOrDefault("extraction_confidence") ?? "medium"
                    });
                }
            }

            if (parsed.RootElement.TryGetProperty("review_tasks", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tEl.EnumerateArray())
                {
                    tasks.Add(new ReviewTask
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
                    r.AnalyteShortCode = ToShortCode(r.AnalyteNameOriginal);
                    tasks.Add(new ReviewTask { FieldPath = $"result:{r.AnalyteNameOriginal}:analyte_short_code", Reason = "Short code missing" });
                }
            }

            return (results, tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AI extraction (Gemini) failed, falling back to heuristic: {ex.Message}");
            return null;
        }
    }

    private static string ToShortCode(string name)
    {
        var letters = string.Concat(name.Where(char.IsLetter)).ToUpperInvariant();
        if (letters.Length >= 2 && letters.Length <= 5) return letters;
        if (letters.Length > 5) return letters[..5];
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
        return string.IsNullOrWhiteSpace(initials) ? "RS" : initials;
    }

    private sealed class ParsedResult
    {
        public string AnalyteNameOriginal { get; set; } = "";
        public string? AnalyteShortCode { get; set; }
        public string ResultType { get; set; } = "numeric";
        public double? ValueNumeric { get; set; }
        public string? ValueText { get; set; }
        public string? UnitOriginal { get; set; }
        public string? UnitNormalised { get; set; }
        public string? ReportedDatetimeLocal { get; set; }
        public string? ExtractionConfidence { get; set; }
    }

    private sealed class ReviewTask
    {
        public string FieldPath { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private static AppSettings LoadSettings()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, "PathoLog", "settings.json");
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // ignore
        }
        return new AppSettings();
    }

    private static string BuildFileName(string pdfPath, string patientName, IEnumerable<ParsedResult> results, string hashHex)
    {
        var date = ExtractDateFromName(Path.GetFileName(pdfPath)) ?? DateTime.Today;
        var panel = ExtractPanelFromName(Path.GetFileNameWithoutExtension(pdfPath));
        var patientSlug = Slugify(patientName);
        var hash8 = hashHex.Length >= 8 ? hashHex[..8] : hashHex;
        return $"{date:yyyy-MM-dd}_{panel}_{patientSlug}_{hash8}.json";
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
        return Slugify(string.Join(" ", panelTokens));
    }

    private static string Slugify(string value)
    {
        var cleaned = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    private sealed class AppSettings
    {
        public string? OpenAiApiKey { get; set; }
        public string? GeminiApiKey { get; set; }
    }
}

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
