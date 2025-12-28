using Tesseract;

namespace PathoLog.Extraction;

public sealed class TesseractOcrTextExtractor : IOcrTextExtractor
{
    private readonly string _dataPath;
    private readonly string _language;

    public TesseractOcrTextExtractor(string dataPath, string language = "eng")
    {
        _dataPath = dataPath;
        _language = language;
    }

    public Task<OcrTextExtractionResult> ExtractAsync(Stream imageOrPdfStream, CancellationToken cancellationToken = default)
    {
        if (!imageOrPdfStream.CanSeek)
        {
            throw new InvalidOperationException("OCR stream must be seekable.");
        }

        imageOrPdfStream.Position = 0;
        using var engine = new TesseractEngine(_dataPath, _language, EngineMode.Default);
        using var pix = Pix.LoadFromMemory(ReadAllBytes(imageOrPdfStream));
        using var page = engine.Process(pix);

        var result = new OcrTextExtractionResult
        {
            Text = page.GetText(),
            AverageConfidence = page.GetMeanConfidence()
        };

        return Task.FromResult(result);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
