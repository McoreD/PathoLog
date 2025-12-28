namespace PathoLog.Mapping;

public sealed class AnalyteShortCodeMapping
{
    public string ShortCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public bool RequiresReview { get; set; }
}

public interface IAnalyteShortCodeGenerator
{
    Task<AnalyteShortCodeMapping> GenerateAsync(string analyteName, string? unit, CancellationToken cancellationToken = default);
}
