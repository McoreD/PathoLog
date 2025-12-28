namespace PathoLog.Domain.ValueObjects;

public enum ResultValueType
{
    Numeric,
    Qualitative,
    Text
}

public enum ResultFlag
{
    None,
    Low,
    High,
    Critical,
    Abnormal
}

public enum MappingMethod
{
    SourceProvided,
    Deterministic,
    AiGenerated,
    UserConfirmed
}

public enum ReviewTaskStatus
{
    Open,
    InReview,
    Resolved
}

public enum AdministrativeEventType
{
    SpecimenComment,
    Correction,
    Note
}
