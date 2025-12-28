using PathoLog.Contracts.Dtos;

namespace PathoLog.Extraction;

public sealed class ExtractionPipeline : IExtractionPipeline
{
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IOcrTextExtractor _ocrTextExtractor;
    private readonly IExtractionModelClient _modelClient;

    public ExtractionPipeline(
        IPdfTextExtractor pdfTextExtractor,
        IOcrTextExtractor ocrTextExtractor,
        IExtractionModelClient modelClient)
    {
        _pdfTextExtractor = pdfTextExtractor;
        _ocrTextExtractor = ocrTextExtractor;
        _modelClient = modelClient;
    }

    public async Task<ExtractionDocumentDto> ExtractAsync(Stream pdfStream, string fileName, CancellationToken cancellationToken = default)
    {
        if (!pdfStream.CanSeek)
        {
            throw new InvalidOperationException("PDF stream must be seekable.");
        }

        pdfStream.Position = 0;
        var pdfResult = await _pdfTextExtractor.ExtractAsync(pdfStream, cancellationToken).ConfigureAwait(false);
        var extractedText = pdfResult.Text;

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            pdfStream.Position = 0;
            var ocrResult = await _ocrTextExtractor.ExtractAsync(pdfStream, cancellationToken).ConfigureAwait(false);
            extractedText = ocrResult.Text;
        }

        var hash = Hashing.ComputeSha256(pdfStream);

        var request = new ExtractionRequest
        {
            ExtractedText = extractedText ?? string.Empty,
            FileName = fileName,
            FileHashSha256 = hash,
            PageCount = pdfResult.PageCount
        };

        var document = await _modelClient.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
        document.SchemaVersion = string.IsNullOrWhiteSpace(document.SchemaVersion) ? "1.0.0" : document.SchemaVersion;
        return document;
    }
}
