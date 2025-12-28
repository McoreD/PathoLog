using PathoLog.Domain.Entities;

namespace PathoLog.Persistence;

public interface ISourceFileRepository
{
    Task<SourceFile?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertAsync(SourceFile sourceFile, CancellationToken cancellationToken = default);
}

public interface ICommentRepository
{
    Task<IReadOnlyList<Comment>> ListByReportAsync(Guid reportId, CancellationToken cancellationToken = default);
    Task UpsertAsync(Comment comment, CancellationToken cancellationToken = default);
}

public interface IReferenceRangeRepository
{
    Task<ReferenceRange?> GetByResultAsync(Guid resultId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ReferenceRange referenceRange, CancellationToken cancellationToken = default);
}

public interface IAdministrativeEventRepository
{
    Task<IReadOnlyList<AdministrativeEvent>> ListByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task UpsertAsync(AdministrativeEvent administrativeEvent, CancellationToken cancellationToken = default);
}
