using System.Text.Json.Serialization;

public record UserRecord(Guid Id, string Email, string? FullName, string? GoogleSub, string? MicrosoftSub);

public record UserResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("fullName")] string? FullName,
    [property: JsonPropertyName("googleLinked")] bool GoogleLinked,
    [property: JsonPropertyName("microsoftLinked")] bool MicrosoftLinked);

public record PatientRecord(Guid Id, string FullName, DateOnly? Dob, string? Sex, DateTime CreatedAtUtc);

public record PatientResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("dob")] DateOnly? Dob,
    [property: JsonPropertyName("sex")] string? Sex);

public record SourceFileResponse(
    [property: JsonPropertyName("originalFilename")] string OriginalFilename);

public record ReportRecord(Guid Id, Guid PatientId, string ParsingStatus, DateTime CreatedAtUtc, string? OriginalFilename);

public record ReportResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("parsingStatus")] string ParsingStatus,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("sourceFile")] SourceFileResponse? SourceFile);

public record ReviewReportResponse(ReportResponse Report, PatientResponse? Patient);

public record ResultRecord(
    Guid Id,
    string AnalyteNameOriginal,
    string? AnalyteShortCode,
    string ResultType,
    decimal? ValueNumeric,
    string? ValueText,
    string? UnitOriginal,
    string? UnitNormalised,
    DateTime? ReportedDatetimeLocal,
    string? ExtractionConfidence,
    string? FlagSeverity);

public record TrendPoint(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reportedDatetimeLocal")] DateTime? ReportedDatetimeLocal,
    [property: JsonPropertyName("collectedDatetimeLocal")] DateTime? CollectedDatetimeLocal,
    [property: JsonPropertyName("valueNumeric")] decimal? ValueNumeric,
    [property: JsonPropertyName("valueText")] string? ValueText,
    [property: JsonPropertyName("unitOriginal")] string? UnitOriginal,
    [property: JsonPropertyName("unitNormalised")] string? UnitNormalised,
    [property: JsonPropertyName("flagSeverity")] string? FlagSeverity,
    [property: JsonPropertyName("extractionConfidence")] string? ExtractionConfidence,
    [property: JsonPropertyName("refLow")] decimal? RefLow,
    [property: JsonPropertyName("refHigh")] decimal? RefHigh);

public record AiProviderStatus(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("hasKey")] bool HasKey);

public record AiSettingsResponse(
    [property: JsonPropertyName("activeProvider")] string? ActiveProvider,
    [property: JsonPropertyName("providers")] IReadOnlyList<AiProviderStatus> Providers);

public record FileUploadRequest(string Filename, string ContentBase64, string? ContentType);

public record MappingEntryRequest(
    [property: JsonPropertyName("analyte_name_pattern")] string AnalyteNamePattern,
    [property: JsonPropertyName("analyte_short_code")] string AnalyteShortCode);

public record MappingConfirmRequest(
    [property: JsonPropertyName("analyte_short_code")] string AnalyteShortCode);

public record ResultCorrectionRequest(
    [property: JsonPropertyName("value_numeric")] decimal? ValueNumeric,
    [property: JsonPropertyName("value_text")] string? ValueText,
    [property: JsonPropertyName("unit_original")] string? UnitOriginal,
    [property: JsonPropertyName("unit_normalised")] string? UnitNormalised,
    [property: JsonPropertyName("flag_severity")] string? FlagSeverity,
    [property: JsonPropertyName("extraction_confidence")] string? ExtractionConfidence);

public record AiSettingsRequest(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("apiKey")] string ApiKey);

public record ParsedPayloadResult(
    [property: JsonPropertyName("analyte_name_original")] string AnalyteNameOriginal,
    [property: JsonPropertyName("analyte_short_code")] string? AnalyteShortCode,
    [property: JsonPropertyName("result_type")] string ResultType,
    [property: JsonPropertyName("value_numeric")] decimal? ValueNumeric,
    [property: JsonPropertyName("value_text")] string? ValueText,
    [property: JsonPropertyName("unit_original")] string? UnitOriginal,
    [property: JsonPropertyName("unit_normalised")] string? UnitNormalised,
    [property: JsonPropertyName("reported_datetime_local")] string? ReportedDatetimeLocal,
    [property: JsonPropertyName("extraction_confidence")] string? ExtractionConfidence,
    [property: JsonPropertyName("flag_severity")] string? FlagSeverity);

public record ParsedPayload(
    [property: JsonPropertyName("results")] IReadOnlyList<ParsedPayloadResult> Results,
    [property: JsonPropertyName("parsing_version")] string? ParsingVersion);

public record AnomalyResponse(
    [property: JsonPropertyName("analyte_short_code")] string AnalyteShortCode,
    string Type,
    object? Detail);
