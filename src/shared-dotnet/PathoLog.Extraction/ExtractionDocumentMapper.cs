using PathoLog.Contracts.Dtos;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;
using PathoLog.Mapping;

namespace PathoLog.Extraction;

public sealed class ExtractionMappingResult
{
    public Patient Patient { get; set; } = new();
    public Report Report { get; set; } = new();
    public List<Result> Results { get; set; } = new();
    public List<ReferenceRange> ReferenceRanges { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<AdministrativeEvent> AdministrativeEvents { get; set; } = new();
}

public sealed class ExtractionDocumentMapper
{
    private readonly IAnalyteShortCodeGenerator? _shortCodeGenerator;

    public ExtractionDocumentMapper(IAnalyteShortCodeGenerator? shortCodeGenerator = null)
    {
        _shortCodeGenerator = shortCodeGenerator;
    }

    public async Task<ExtractionMappingResult> MapAsync(
        ExtractionDocumentDto document,
        Guid patientId,
        Guid sourceFileId,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionMappingResult();

        result.Patient = MapPatient(document.Patient, patientId);
        result.Report = MapReport(document.Report, patientId, sourceFileId);

        var mappingTasks = new List<Task>();
        foreach (var item in document.Results)
        {
            var mapped = MapResult(item, result.Report.Id);
            if (string.IsNullOrWhiteSpace(mapped.AnalyteShortCode) && _shortCodeGenerator is not null)
            {
                mappingTasks.Add(ApplyShortCodeAsync(mapped, item, cancellationToken));
            }

            result.Results.Add(mapped);

            if (item.ReferenceRange is not null)
            {
                var range = MapReferenceRange(item.ReferenceRange, mapped.Id);
                result.ReferenceRanges.Add(range);
            }
        }

        if (mappingTasks.Count > 0)
        {
            await Task.WhenAll(mappingTasks).ConfigureAwait(false);
        }

        foreach (var comment in document.Comments)
        {
            result.Comments.Add(MapComment(comment, result.Report.Id));
        }

        foreach (var adminEvent in document.AdministrativeEvents)
        {
            result.AdministrativeEvents.Add(MapAdministrativeEvent(adminEvent, patientId, result.Report.Id));
        }

        return result;
    }

    private async Task ApplyShortCodeAsync(Result target, ExtractionResultDto source, CancellationToken cancellationToken)
    {
        var analyteName = source.AnalyteName?.Value ?? string.Empty;
        var unit = source.Unit?.Value;
        var mapping = await _shortCodeGenerator!.GenerateAsync(analyteName, unit, cancellationToken).ConfigureAwait(false);
        target.AnalyteShortCode = string.IsNullOrWhiteSpace(mapping.ShortCode)
            ? NormalizeShortCode(analyteName)
            : mapping.ShortCode;
    }

    private static Patient MapPatient(ExtractionPatientDto? patient, Guid patientId)
    {
        return new Patient
        {
            Id = patientId,
            FullName = patient?.FullName?.Value ?? string.Empty,
            DateOfBirth = ParseDateOnly(patient?.DateOfBirth?.Value),
            SexAtBirth = patient?.SexAtBirth?.Value,
            ExternalId = patient?.ExternalId?.Value
        };
    }

    private static Report MapReport(ExtractionReportDto? report, Guid patientId, Guid sourceFileId)
    {
        return new Report
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            SourceFileId = sourceFileId,
            ReportDate = ParseDateOnly(report?.ReportDate?.Value),
            LaboratoryName = report?.LaboratoryName?.Value,
            PanelName = report?.PanelName?.Value,
            SpecimenDescription = report?.SpecimenDescription?.Value
        };
    }

    private static Result MapResult(ExtractionResultDto result, Guid reportId)
    {
        var analyteName = result.AnalyteName?.Value ?? string.Empty;
        var shortCode = result.AnalyteShortCode?.Value;
        var valueType = ParseValueType(result.ValueType);

        return new Result
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            AnalyteName = analyteName,
            AnalyteShortCode = string.IsNullOrWhiteSpace(shortCode) ? NormalizeShortCode(analyteName) : shortCode!,
            ValueType = valueType,
            ValueNumber = result.ValueNumber?.Value.HasValue == true ? (decimal?)result.ValueNumber.Value : null,
            ValueText = result.ValueText?.Value,
            Unit = result.Unit?.Value,
            Flag = ParseFlag(result.Flag?.Value),
            SourceAnchor = ResolveSourceAnchor(result),
            ExtractionConfidence = ResolveConfidence(result)
        };
    }

    private static ReferenceRange MapReferenceRange(ExtractionReferenceRangeDto range, Guid resultId)
    {
        return new ReferenceRange
        {
            Id = Guid.NewGuid(),
            ResultId = resultId,
            Low = range.Low?.Value.HasValue == true ? (decimal?)range.Low.Value : null,
            High = range.High?.Value.HasValue == true ? (decimal?)range.High.Value : null,
            Unit = range.Unit?.Value,
            Text = range.Text?.Value
        };
    }

    private static Comment MapComment(ExtractionCommentDto comment, Guid reportId)
    {
        return new Comment
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            Category = comment.Category?.Value ?? "General",
            Text = comment.Text?.Value ?? string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static AdministrativeEvent MapAdministrativeEvent(ExtractionAdministrativeEventDto adminEvent, Guid patientId, Guid reportId)
    {
        return new AdministrativeEvent
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ReportId = reportId,
            EventType = ParseAdministrativeEventType(adminEvent.EventType?.Value),
            Description = adminEvent.Description?.Value ?? string.Empty,
            EventDateUtc = ParseDateTimeOffset(adminEvent.EventDate?.Value) ?? DateTimeOffset.UtcNow,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static ResultValueType ParseValueType(string? value)
    {
        return value switch
        {
            "numeric" => ResultValueType.Numeric,
            "qualitative" => ResultValueType.Qualitative,
            "text" => ResultValueType.Text,
            _ => ResultValueType.Text
        };
    }

    private static ResultFlag ParseFlag(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "low" => ResultFlag.Low,
            "high" => ResultFlag.High,
            "critical" => ResultFlag.Critical,
            "abnormal" => ResultFlag.Abnormal,
            _ => ResultFlag.None
        };
    }

    private static AdministrativeEventType ParseAdministrativeEventType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "specimen_comment" => AdministrativeEventType.SpecimenComment,
            "correction" => AdministrativeEventType.Correction,
            _ => AdministrativeEventType.Note
        };
    }

    private static string ResolveSourceAnchor(ExtractionResultDto result)
    {
        return result.ValueNumber?.SourceAnchor
            ?? result.ValueText?.SourceAnchor
            ?? result.AnalyteName?.SourceAnchor
            ?? string.Empty;
    }

    private static double? ResolveConfidence(ExtractionResultDto result)
    {
        return result.OverallConfidence
            ?? result.ValueNumber?.ExtractionConfidence
            ?? result.ValueText?.ExtractionConfidence
            ?? result.AnalyteName?.ExtractionConfidence;
    }

    private static string NormalizeShortCode(string analyteName)
    {
        var trimmed = analyteName.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "UNKNOWN" : trimmed.Replace(" ", string.Empty).ToUpperInvariant();
    }
}
