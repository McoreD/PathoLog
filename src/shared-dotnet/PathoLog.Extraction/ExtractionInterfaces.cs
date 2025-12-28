namespace PathoLog.Extraction;

public sealed class PdfTextExtractionResult
{
    public string Text { get; set; } = string.Empty;
    public int PageCount { get; set; }
}

public sealed class OcrTextExtractionResult
{
    public string Text { get; set; } = string.Empty;
    public double? AverageConfidence { get; set; }
}

public interface IPdfTextExtractor
{
    Task<PdfTextExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

public interface IOcrTextExtractor
{
    Task<OcrTextExtractionResult> ExtractAsync(Stream imageOrPdfStream, CancellationToken cancellationToken = default);
}

public interface ITableDetector
{
    Task<IReadOnlyList<string>> DetectTablesAsync(string extractedText, CancellationToken cancellationToken = default);
}

public sealed class ExtractionRequest
{
    public string ExtractedText { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FileHashSha256 { get; set; }
    public int? PageCount { get; set; }
}

public interface IExtractionModelClient
{
    Task<PathoLog.Contracts.Dtos.ExtractionDocumentDto> ExtractAsync(
        ExtractionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IExtractionPipeline
{
    Task<PathoLog.Contracts.Dtos.ExtractionDocumentDto> ExtractAsync(
        Stream pdfStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
