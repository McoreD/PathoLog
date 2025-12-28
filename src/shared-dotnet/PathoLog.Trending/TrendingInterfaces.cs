namespace PathoLog.Trending;

public sealed class TrendPoint
{
    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public string? Flag { get; set; }
}

public sealed class TrendSeries
{
    public string AnalyteShortCode { get; set; } = string.Empty;
    public IReadOnlyList<TrendPoint> Points { get; set; } = Array.Empty<TrendPoint>();
}

public interface ITrendQueryService
{
    Task<TrendSeries> GetSeriesAsync(Guid patientId, string analyteShortCode, CancellationToken cancellationToken = default);
}
