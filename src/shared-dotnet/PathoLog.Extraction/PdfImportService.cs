using PathoLog.Contracts.Dtos;
using PathoLog.Domain.Entities;
using PathoLog.Persistence;

namespace PathoLog.Extraction;

public sealed class PdfImportRequest
{
    public Guid PatientId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
}

public sealed class PdfImportResult
{
    public Guid SourceFileId { get; set; }
    public Guid ReportId { get; set; }
    public int ReviewTaskCount { get; set; }
}

public sealed class PdfImportService
{
    private readonly IExtractionPipeline _extractionPipeline;
    private readonly IPatientRepository _patientRepository;
    private readonly IReportRepository _reportRepository;
    private readonly IResultRepository _resultRepository;
    private readonly IReferenceRangeRepository _referenceRangeRepository;
    private readonly ICommentRepository _commentRepository;
    private readonly IAdministrativeEventRepository _administrativeEventRepository;
    private readonly IReviewTaskRepository _reviewTaskRepository;
    private readonly ISourceFileRepository _sourceFileRepository;
    private readonly ILocalFileStore _fileStore;
    private readonly ExtractionDocumentMapper _mapper;
    private readonly ExtractionReviewTaskBuilder _reviewTaskBuilder;

    public PdfImportService(
        IExtractionPipeline extractionPipeline,
        IPatientRepository patientRepository,
        IReportRepository reportRepository,
        IResultRepository resultRepository,
        IReferenceRangeRepository referenceRangeRepository,
        ICommentRepository commentRepository,
        IAdministrativeEventRepository administrativeEventRepository,
        IReviewTaskRepository reviewTaskRepository,
        ISourceFileRepository sourceFileRepository,
        ILocalFileStore fileStore,
        ExtractionDocumentMapper mapper,
        ExtractionReviewTaskBuilder reviewTaskBuilder)
    {
        _extractionPipeline = extractionPipeline;
        _patientRepository = patientRepository;
        _reportRepository = reportRepository;
        _resultRepository = resultRepository;
        _referenceRangeRepository = referenceRangeRepository;
        _commentRepository = commentRepository;
        _administrativeEventRepository = administrativeEventRepository;
        _reviewTaskRepository = reviewTaskRepository;
        _sourceFileRepository = sourceFileRepository;
        _fileStore = fileStore;
        _mapper = mapper;
        _reviewTaskBuilder = reviewTaskBuilder;
    }

    public async Task<PdfImportResult> ImportAsync(PdfImportRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PatientId == Guid.Empty)
        {
            throw new ArgumentException("PatientId is required", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("FileName is required", nameof(request));
        }

        if (request.Content == Stream.Null)
        {
            throw new ArgumentException("Content is required", nameof(request));
        }

        await using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var hash = Hashing.ComputeSha256(buffer);
        buffer.Position = 0;
        var storedPath = await _fileStore.SaveAsync(buffer, request.FileName, hash, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        var extractionDocument = await _extractionPipeline
            .ExtractAsync(buffer, request.FileName, cancellationToken)
            .ConfigureAwait(false);

        var sourceFile = new SourceFile
        {
            Id = Guid.NewGuid(),
            OriginalFileName = request.FileName,
            HashSha256 = hash,
            StoredPath = storedPath,
            SizeBytes = buffer.Length,
            ImportedUtc = DateTimeOffset.UtcNow
        };

        await _sourceFileRepository.UpsertAsync(sourceFile, cancellationToken).ConfigureAwait(false);

        var mappingResult = await _mapper
            .MapAsync(extractionDocument, request.PatientId, sourceFile.Id, cancellationToken)
            .ConfigureAwait(false);

        var patient = await _patientRepository.GetAsync(request.PatientId, cancellationToken).ConfigureAwait(false)
            ?? new Patient { Id = request.PatientId };
        MergePatient(patient, mappingResult.Patient);
        await _patientRepository.UpsertAsync(patient, cancellationToken).ConfigureAwait(false);

        await _reportRepository.UpsertAsync(mappingResult.Report, cancellationToken).ConfigureAwait(false);

        foreach (var result in mappingResult.Results)
        {
            await _resultRepository.UpsertAsync(result, cancellationToken).ConfigureAwait(false);
        }

        foreach (var range in mappingResult.ReferenceRanges)
        {
            await _referenceRangeRepository.UpsertAsync(range, cancellationToken).ConfigureAwait(false);
        }

        foreach (var comment in mappingResult.Comments)
        {
            await _commentRepository.UpsertAsync(comment, cancellationToken).ConfigureAwait(false);
        }

        foreach (var adminEvent in mappingResult.AdministrativeEvents)
        {
            await _administrativeEventRepository.UpsertAsync(adminEvent, cancellationToken).ConfigureAwait(false);
        }

        var reviewTasks = _reviewTaskBuilder.Build(extractionDocument, mappingResult.Report.Id);
        foreach (var task in reviewTasks)
        {
            await _reviewTaskRepository.UpsertAsync(task, cancellationToken).ConfigureAwait(false);
        }

        return new PdfImportResult
        {
            SourceFileId = sourceFile.Id,
            ReportId = mappingResult.Report.Id,
            ReviewTaskCount = reviewTasks.Count
        };
    }

    private static void MergePatient(Patient target, Patient extracted)
    {
        if (!string.IsNullOrWhiteSpace(extracted.FullName))
        {
            target.FullName = extracted.FullName;
        }

        if (extracted.DateOfBirth.HasValue)
        {
            target.DateOfBirth = extracted.DateOfBirth;
        }

        if (!string.IsNullOrWhiteSpace(extracted.SexAtBirth))
        {
            target.SexAtBirth = extracted.SexAtBirth;
        }

        if (!string.IsNullOrWhiteSpace(extracted.ExternalId))
        {
            target.ExternalId = extracted.ExternalId;
        }
    }
}
