using System.Data.Common;
using PathoLog.Domain.Entities;

namespace PathoLog.Persistence;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IMigrationRunner
{
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);
}

public interface IPatientRepository
{
    Task<Patient?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Patient>> SearchAsync(string? name, CancellationToken cancellationToken = default);
    Task UpsertAsync(Patient patient, CancellationToken cancellationToken = default);
}

public interface IReportRepository
{
    Task<Report?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Report>> ListByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task UpsertAsync(Report report, CancellationToken cancellationToken = default);
}

public interface IResultRepository
{
    Task<IReadOnlyList<Result>> ListByReportAsync(Guid reportId, CancellationToken cancellationToken = default);
    Task UpsertAsync(Result result, CancellationToken cancellationToken = default);
}

public interface IMappingDictionaryRepository
{
    Task<MappingDictionaryEntry?> TryResolveAsync(string sourceName, CancellationToken cancellationToken = default);
    Task UpsertAsync(MappingDictionaryEntry entry, CancellationToken cancellationToken = default);
}

public interface IReviewTaskRepository
{
    Task<IReadOnlyList<ReviewTask>> ListOpenAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ReviewTask task, CancellationToken cancellationToken = default);
}

public interface ILocalFileStore
{
    Task<string> SaveAsync(Stream content, string fileName, string sha256Hash, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storedPath, CancellationToken cancellationToken = default);
}
