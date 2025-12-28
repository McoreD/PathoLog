using PathoLog.Domain.ValueObjects;

namespace PathoLog.Domain.Entities;

public sealed class Patient
{
    public Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class Report
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? SourceFileId { get; set; }
    public DateOnly? ReportDate { get; set; }
    public string? LaboratoryName { get; set; }
    public string? PanelName { get; set; }
    public string? SpecimenDescription { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class Subpanel
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class Result
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid? SubpanelId { get; set; }
    public string AnalyteName { get; set; } = string.Empty;
    public string AnalyteShortCode { get; set; } = string.Empty;
    public ResultValueType ValueType { get; set; }
    public decimal? ValueNumber { get; set; }
    public string? ValueText { get; set; }
    public string? Unit { get; set; }
    public ResultFlag Flag { get; set; }
    public string? SourceAnchor { get; set; }
    public double? ExtractionConfidence { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class ReferenceRange
{
    public Guid Id { get; set; }
    public Guid ResultId { get; set; }
    public decimal? Low { get; set; }
    public decimal? High { get; set; }
    public string? Unit { get; set; }
    public string? Text { get; set; }
}

public sealed class Comment
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class AdministrativeEvent
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ReportId { get; set; }
    public AdministrativeEventType EventType { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset EventDateUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class MappingDictionaryEntry
{
    public Guid Id { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string AnalyteShortCode { get; set; } = string.Empty;
    public MappingMethod MappingMethod { get; set; }
    public double? MappingConfidence { get; set; }
    public DateTimeOffset? LastConfirmedUtc { get; set; }
}

public sealed class ReviewTask
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string FieldPath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ReviewTaskStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ResolvedUtc { get; set; }
}

public sealed class SourceFile
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string HashSha256 { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset ImportedUtc { get; set; }
}
