using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;

public static class AiParsingService
{
    private const string OpenAiModel = "gpt-4o";
    private const string GeminiModel = "gemini-2.5-pro";
    private static readonly HttpClient Client = new();

    public static async Task<IReadOnlyList<ParsedPayloadResult>> ParseAsync(byte[] pdfBytes, string? openAiKey, string? geminiKey)
    {
        var text = ExtractText(pdfBytes);
        AiParseResult? aiResult = null;

        if (!string.IsNullOrWhiteSpace(text))
        {
            aiResult = await ExtractWithOpenAi(text, openAiKey)
                       ?? await ExtractWithGemini(text, geminiKey);
        }

        aiResult ??= ExtractHeuristic(text);
        return aiResult.Results;
    }

    public static bool NeedsReview(IEnumerable<ParsedPayloadResult> results)
    {
        return results.Any(r => string.Equals(r.ExtractionConfidence, "low", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractText(byte[] pdfBytes)
    {
        try
        {
            using var stream = new MemoryStream(pdfBytes);
            using var doc = PdfDocument.Open(stream);
            return string.Join("\n", doc.GetPages().Select(p => p.Text)).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<AiParseResult?> ExtractWithOpenAi(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

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

        string? pass1Json;
        try
        {
            pass1Json = await SendOpenAiAsync(apiKey, pass1Payload);
        }
        catch
        {
            pass1Json = null;
        }

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

        try
        {
            var pass2Json = await SendOpenAiAsync(apiKey, pass2Payload);
            return string.IsNullOrWhiteSpace(pass2Json) ? null : ParseAiResult(pass2Json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<AiParseResult?> ExtractWithGemini(string text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

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

        string? pass1Json;
        try
        {
            pass1Json = await SendGeminiAsync(apiKey, pass1Payload);
        }
        catch
        {
            pass1Json = null;
        }

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

        try
        {
            var pass2Json = await SendGeminiAsync(apiKey, pass2Payload);
            return string.IsNullOrWhiteSpace(pass2Json) ? null : ParseAiResult(pass2Json);
        }
        catch
        {
            return null;
        }
    }

    private static AiParseResult ExtractHeuristic(string text)
    {
        var results = new List<ParsedPayloadResult>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numberPattern = new System.Text.RegularExpressions.Regex(@"^([A-Za-z][\w /\-%()]{2,})\s+([<>]?\d+(?:\.\d+)?)(?:\s*([A-Za-zć/%-]+))?");

        foreach (var line in lines)
        {
            var match = numberPattern.Match(line);
            if (!match.Success) continue;
            var name = match.Groups[1].Value.Trim();
            var rawVal = match.Groups[2].Value.Trim();
            var unit = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
            var numeric = decimal.TryParse(rawVal.TrimStart('<', '>'), out var d) ? d : (decimal?)null;
            results.Add(new ParsedPayloadResult(
                name,
                ToShortCode(name),
                "numeric",
                numeric,
                rawVal,
                unit,
                unit,
                null,
                "low",
                null));
            if (results.Count >= 12) break;
        }

        if (results.Count == 0)
        {
            results.Add(new ParsedPayloadResult(
                "Report imported",
                "PDF",
                "qualitative",
                null,
                "Parsed text captured",
                null,
                null,
                null,
                "low",
                null));
        }

        return new AiParseResult(results);
    }

    private static AiParseResult ParseAiResult(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var results = new List<ParsedPayloadResult>();

        if (parsed.RootElement.TryGetProperty("results", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rEl.EnumerateArray())
            {
                var name = r.GetPropertyOrDefault("analyte_name_original") ?? "Analyte";
                var shortCode = r.GetPropertyOrDefault("analyte_short_code") ?? ToShortCode(name);
                results.Add(new ParsedPayloadResult(
                    name,
                    shortCode,
                    r.GetPropertyOrDefault("result_type") ?? "qualitative",
                    r.GetPropertyOrDefaultDecimal("value_numeric"),
                    r.GetPropertyOrDefault("value_text"),
                    r.GetPropertyOrDefault("unit_original"),
                    r.GetPropertyOrDefault("unit_normalised"),
                    null,
                    r.GetPropertyOrDefault("extraction_confidence") ?? "medium",
                    null));
            }
        }

        if (results.Count == 0)
        {
            results.Add(new ParsedPayloadResult(
                "Report imported",
                "PDF",
                "qualitative",
                null,
                "Parsed text captured",
                null,
                null,
                null,
                "low",
                null));
        }

        return new AiParseResult(results);
    }

    private static string ToShortCode(string name)
    {
        var letters = new string(name.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        if (letters.Length >= 2 && letters.Length <= 5) return letters;
        if (letters.Length > 5) return letters[..5];
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
        return string.IsNullOrWhiteSpace(initials) ? "RS" : initials;
    }

    private static async Task<string?> SendOpenAiAsync(string apiKey, object payload)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(message).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static async Task<string?> SendGeminiAsync(string apiKey, object payload)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent?key={apiKey}");
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(message).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }

    private sealed record AiParseResult(IReadOnlyList<ParsedPayloadResult> Results);
}

internal static class AiJsonExtensions
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

    public static decimal? GetPropertyOrDefaultDecimal(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
        {
            if (p.TryGetDecimal(out var d)) return d;
        }
        return null;
    }
}
