using PathoLog.Persistence;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Mapping;

public sealed class DeterministicShortCodeGenerator
{
    public AnalyteShortCodeMapping Generate(string analyteName)
    {
        var normalized = Normalize(analyteName);
        return new AnalyteShortCodeMapping
        {
            ShortCode = string.IsNullOrWhiteSpace(normalized) ? "UNKNOWN" : normalized,
            Method = MappingMethod.Deterministic.ToString(),
            Confidence = 0.5,
            RequiresReview = true
        };
    }

    private static string Normalize(string analyteName)
    {
        var tokens = analyteName
            .Split(new[] { ' ', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 0)
            .Select(token => token.ToUpperInvariant());

        var combined = string.Concat(tokens);
        if (combined.Length > 12)
        {
            combined = combined.Substring(0, 12);
        }

        return combined;
    }
}

public sealed class AnalyteShortCodeGenerator : IAnalyteShortCodeGenerator
{
    private readonly IMappingDictionaryRepository _mappingDictionaryRepository;
    private readonly DeterministicShortCodeGenerator _deterministicGenerator;
    private readonly IAnalyteShortCodeAiClient? _aiClient;

    public AnalyteShortCodeGenerator(
        IMappingDictionaryRepository mappingDictionaryRepository,
        DeterministicShortCodeGenerator deterministicGenerator,
        IAnalyteShortCodeAiClient? aiClient = null)
    {
        _mappingDictionaryRepository = mappingDictionaryRepository;
        _deterministicGenerator = deterministicGenerator;
        _aiClient = aiClient;
    }

    public async Task<AnalyteShortCodeMapping> GenerateAsync(
        string analyteName,
        string? unit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(analyteName))
        {
            return new AnalyteShortCodeMapping
            {
                ShortCode = "UNKNOWN",
                Method = MappingMethod.Deterministic.ToString(),
                Confidence = 0.1,
                RequiresReview = true
            };
        }

        var existing = await _mappingDictionaryRepository
            .TryResolveAsync(analyteName, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new AnalyteShortCodeMapping
            {
                ShortCode = existing.AnalyteShortCode,
                Method = existing.MappingMethod.ToString(),
                Confidence = existing.MappingConfidence,
                RequiresReview = false
            };
        }

        if (_aiClient is not null)
        {
            var aiMapping = await _aiClient.GenerateAsync(analyteName, unit, cancellationToken).ConfigureAwait(false);
            if (aiMapping is not null && !string.IsNullOrWhiteSpace(aiMapping.ShortCode))
            {
                aiMapping.Method = MappingMethod.AiGenerated.ToString();
                aiMapping.RequiresReview = true;
                return aiMapping;
            }
        }

        return _deterministicGenerator.Generate(analyteName);
    }
}
