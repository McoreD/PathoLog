using System.Text.Json.Serialization;

namespace PathoLog.Contracts.Dtos;

public sealed class ExtractedStringValueDto
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("source_anchor")]
    public string? SourceAnchor { get; set; }

    [JsonPropertyName("extraction_confidence")]
    public double? ExtractionConfidence { get; set; }
}

public sealed class ExtractedNumberValueDto
{
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("source_anchor")]
    public string? SourceAnchor { get; set; }

    [JsonPropertyName("extraction_confidence")]
    public double? ExtractionConfidence { get; set; }
}

public sealed class ExtractedDateValueDto
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("source_anchor")]
    public string? SourceAnchor { get; set; }

    [JsonPropertyName("extraction_confidence")]
    public double? ExtractionConfidence { get; set; }
}

public sealed class ExtractionSourceFileDto
{
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_hash_sha256")]
    public string? FileHashSha256 { get; set; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; set; }
}

public sealed class ExtractionPatientDto
{
    [JsonPropertyName("full_name")]
    public ExtractedStringValueDto? FullName { get; set; }

    [JsonPropertyName("date_of_birth")]
    public ExtractedDateValueDto? DateOfBirth { get; set; }

    [JsonPropertyName("sex_at_birth")]
    public ExtractedStringValueDto? SexAtBirth { get; set; }

    [JsonPropertyName("external_id")]
    public ExtractedStringValueDto? ExternalId { get; set; }
}

public sealed class ExtractionReportDto
{
    [JsonPropertyName("report_date")]
    public ExtractedDateValueDto? ReportDate { get; set; }

    [JsonPropertyName("laboratory_name")]
    public ExtractedStringValueDto? LaboratoryName { get; set; }

    [JsonPropertyName("panel_name")]
    public ExtractedStringValueDto? PanelName { get; set; }

    [JsonPropertyName("specimen_description")]
    public ExtractedStringValueDto? SpecimenDescription { get; set; }
}

public sealed class ExtractionReferenceRangeDto
{
    [JsonPropertyName("low")]
    public ExtractedNumberValueDto? Low { get; set; }

    [JsonPropertyName("high")]
    public ExtractedNumberValueDto? High { get; set; }

    [JsonPropertyName("text")]
    public ExtractedStringValueDto? Text { get; set; }

    [JsonPropertyName("unit")]
    public ExtractedStringValueDto? Unit { get; set; }
}

public sealed class ExtractionMappingDto
{
    [JsonPropertyName("mapping_method")]
    public string? MappingMethod { get; set; }

    [JsonPropertyName("mapping_confidence")]
    public double? MappingConfidence { get; set; }
}

public sealed class ExtractionResultDto
{
    [JsonPropertyName("analyte_name")]
    public ExtractedStringValueDto? AnalyteName { get; set; }

    [JsonPropertyName("analyte_short_code")]
    public ExtractedStringValueDto? AnalyteShortCode { get; set; }

    [JsonPropertyName("value_type")]
    public string? ValueType { get; set; }

    [JsonPropertyName("value_number")]
    public ExtractedNumberValueDto? ValueNumber { get; set; }

    [JsonPropertyName("value_text")]
    public ExtractedStringValueDto? ValueText { get; set; }

    [JsonPropertyName("unit")]
    public ExtractedStringValueDto? Unit { get; set; }

    [JsonPropertyName("flag")]
    public ExtractedStringValueDto? Flag { get; set; }

    [JsonPropertyName("reference_range")]
    public ExtractionReferenceRangeDto? ReferenceRange { get; set; }

    [JsonPropertyName("mapping")]
    public ExtractionMappingDto? Mapping { get; set; }

    [JsonPropertyName("overall_confidence")]
    public double? OverallConfidence { get; set; }
}

public sealed class ExtractionCommentDto
{
    [JsonPropertyName("category")]
    public ExtractedStringValueDto? Category { get; set; }

    [JsonPropertyName("text")]
    public ExtractedStringValueDto? Text { get; set; }
}

public sealed class ExtractionAdministrativeEventDto
{
    [JsonPropertyName("event_type")]
    public ExtractedStringValueDto? EventType { get; set; }

    [JsonPropertyName("description")]
    public ExtractedStringValueDto? Description { get; set; }

    [JsonPropertyName("event_date")]
    public ExtractedDateValueDto? EventDate { get; set; }
}

public sealed class ExtractionDocumentDto
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("source_file")]
    public ExtractionSourceFileDto? SourceFile { get; set; }

    [JsonPropertyName("patient")]
    public ExtractionPatientDto? Patient { get; set; }

    [JsonPropertyName("report")]
    public ExtractionReportDto? Report { get; set; }

    [JsonPropertyName("results")]
    public List<ExtractionResultDto> Results { get; set; } = new();

    [JsonPropertyName("comments")]
    public List<ExtractionCommentDto> Comments { get; set; } = new();

    [JsonPropertyName("administrative_events")]
    public List<ExtractionAdministrativeEventDto> AdministrativeEvents { get; set; } = new();
}
