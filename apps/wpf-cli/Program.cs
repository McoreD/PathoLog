using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

var argsDict = ParseArgs(args);
if (!argsDict.TryGetValue("file", out var filePath))
{
    Console.WriteLine("Usage: dotnet run --project apps/wpf-cli/PathoLog.Wpf.Cli.csproj -- --file <pdf> [--patient \"Name\"] [--email you@example.com] [--save] [--show-text]");
    return;
}

filePath = Path.GetFullPath(filePath);
var patientName = argsDict.GetValueOrDefault("patient") ?? "CLI Patient";
var email = argsDict.GetValueOrDefault("email") ?? "cli@patholog.dev";
var save = argsDict.ContainsKey("save");
var showText = argsDict.ContainsKey("show-text");

Console.WriteLine($"PathoLog WPF CLI");
Console.WriteLine($"File   : {filePath}");
Console.WriteLine($"Patient: {patientName}");
Console.WriteLine($"Email  : {email}");
Console.WriteLine($"Mode   : {(save ? "save" : "dry-run")}");

if (!File.Exists(filePath))
{
    Console.WriteLine("File not found.");
    return;
}

string text;
var pages = 0;
try
{
    using var doc = PdfDocument.Open(filePath);
    pages = doc.NumberOfPages;
    text = string.Join("\n", doc.GetPages().Select(p => p.Text));
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to read PDF: {ex.Message}");
    return;
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

var results = ExtractResults(text);
Console.WriteLine($"Extracted results: {results.Count}");
foreach (var r in results.Take(5))
{
    Console.WriteLine($" - {r.AnalyteNameOriginal} => {r.ValueText ?? r.ValueNumeric?.ToString() ?? "n/a"} {r.UnitOriginal} ({r.ResultType})");
}

if (!save)
{
    Console.WriteLine("Dry-run complete. Use --save to write JSON output locally.");
    return;
}

var output = new
{
    patient = new { name = patientName, email },
    source = new { file = filePath, pages, textLength = text.Length },
    results
};

var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var folder = Path.Combine(appData, "PathoLog", "cli-reports");
Directory.CreateDirectory(folder);
var outfile = Path.Combine(folder, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.json");
await File.WriteAllTextAsync(outfile, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Saved structured JSON to {outfile}");

// ----------------- helpers -----------------

static Dictionary<string, string> ParseArgs(string[] raw)
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
        if (val is not null)
        {
            dict[key] = val;
        }
        else
        {
            dict[key] = string.Empty;
        }
    }
    return dict;
}

static List<ParsedResult> ExtractResults(string text)
{
    var list = new List<ParsedResult>();
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var numberPattern = new Regex(@"^([A-Za-z][\w \/\-%()]{2,})\s+([<>]?\d+(?:\.\d+)?)(?:\s*([A-Za-zµ/%-]+))?", RegexOptions.Compiled);
    foreach (var line in lines)
    {
        var match = numberPattern.Match(line);
        if (!match.Success) continue;
        var name = match.Groups[1].Value.Trim();
        var rawVal = match.Groups[2].Value.Trim();
        var unit = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
        var numeric = double.TryParse(rawVal.TrimStart('<', '>'), out var d) ? d : (double?)null;
        list.Add(new ParsedResult
        {
            AnalyteNameOriginal = name,
            AnalyteShortCode = ToShortCode(name),
            ResultType = "numeric",
            ValueNumeric = numeric,
            ValueText = rawVal,
            UnitOriginal = unit,
            ExtractionConfidence = "low"
        });
        if (list.Count >= 12) break;
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
    }

    return list;
}

static string ToShortCode(string name)
{
    var letters = string.Concat(name.Where(char.IsLetter)).ToUpperInvariant();
    if (letters.Length >= 2 && letters.Length <= 5) return letters;
    if (letters.Length > 5) return letters[..5];
    var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
    return string.IsNullOrWhiteSpace(initials) ? "RS" : initials;
}

public sealed class ParsedResult
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
