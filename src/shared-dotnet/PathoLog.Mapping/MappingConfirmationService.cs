using PathoLog.Domain.ValueObjects;
using PathoLog.Persistence;

namespace PathoLog.Mapping;

public sealed class MappingConfirmationService
{
    private readonly IMappingDictionaryRepository _mappingDictionaryRepository;

    public MappingConfirmationService(IMappingDictionaryRepository mappingDictionaryRepository)
    {
        _mappingDictionaryRepository = mappingDictionaryRepository;
    }

    public async Task ConfirmAsync(
        string sourceName,
        string analyteShortCode,
        double? confidence,
        CancellationToken cancellationToken = default)
    {
        var entry = await _mappingDictionaryRepository
            .TryResolveAsync(sourceName, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            entry = new PathoLog.Domain.Entities.MappingDictionaryEntry
            {
                Id = Guid.NewGuid(),
                SourceName = sourceName
            };
        }

        entry.AnalyteShortCode = analyteShortCode;
        entry.MappingMethod = MappingMethod.UserConfirmed;
        entry.MappingConfidence = confidence;
        entry.LastConfirmedUtc = DateTimeOffset.UtcNow;

        await _mappingDictionaryRepository.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
    }
}
