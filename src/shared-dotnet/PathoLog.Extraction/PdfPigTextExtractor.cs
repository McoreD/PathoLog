using System.Text;
using UglyToad.PdfPig;

namespace PathoLog.Extraction;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<PdfTextExtractionResult> ExtractAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        if (!pdfStream.CanSeek)
        {
            throw new InvalidOperationException("PDF stream must be seekable.");
        }

        pdfStream.Position = 0;
        using var document = PdfDocument.Open(pdfStream);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        var result = new PdfTextExtractionResult
        {
            Text = builder.ToString(),
            PageCount = document.NumberOfPages
        };

        return Task.FromResult(result);
    }
}
