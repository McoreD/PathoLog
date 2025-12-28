namespace PathoLog.Mapping;

public interface IAnalyteShortCodeAiClient
{
    Task<AnalyteShortCodeMapping?> GenerateAsync(string analyteName, string? unit, CancellationToken cancellationToken = default);
}
