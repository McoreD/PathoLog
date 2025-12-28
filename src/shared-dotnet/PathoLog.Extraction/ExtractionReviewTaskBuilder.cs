using PathoLog.Contracts.Dtos;
using PathoLog.Domain.Entities;
using PathoLog.Domain.ValueObjects;

namespace PathoLog.Extraction;

public sealed class ExtractionReviewTaskBuilder
{
    private readonly double _threshold;

    public ExtractionReviewTaskBuilder(double threshold = 0.7)
    {
        _threshold = threshold;
    }

    public IReadOnlyList<ReviewTask> Build(ExtractionDocumentDto document, Guid reportId)
    {
        var tasks = new List<ReviewTask>();

        AddIfLow(tasks, reportId, "patient.full_name", document.Patient?.FullName);
        AddIfLow(tasks, reportId, "patient.date_of_birth", document.Patient?.DateOfBirth);
        AddIfLow(tasks, reportId, "patient.sex_at_birth", document.Patient?.SexAtBirth);
        AddIfLow(tasks, reportId, "patient.external_id", document.Patient?.ExternalId);

        AddIfLow(tasks, reportId, "report.report_date", document.Report?.ReportDate);
        AddIfLow(tasks, reportId, "report.laboratory_name", document.Report?.LaboratoryName);
        AddIfLow(tasks, reportId, "report.panel_name", document.Report?.PanelName);
        AddIfLow(tasks, reportId, "report.specimen_description", document.Report?.SpecimenDescription);

        for (var i = 0; i < document.Results.Count; i++)
        {
            var result = document.Results[i];
            var basePath = $"results[{i}]";
            AddIfLow(tasks, reportId, $"{basePath}.analyte_name", result.AnalyteName);
            AddIfLow(tasks, reportId, $"{basePath}.analyte_short_code", result.AnalyteShortCode);
            AddIfLow(tasks, reportId, $"{basePath}.value_number", result.ValueNumber);
            AddIfLow(tasks, reportId, $"{basePath}.value_text", result.ValueText);
            AddIfLow(tasks, reportId, $"{basePath}.unit", result.Unit);
            AddIfLow(tasks, reportId, $"{basePath}.flag", result.Flag);

            if (result.OverallConfidence.HasValue && result.OverallConfidence.Value < _threshold)
            {
                tasks.Add(CreateTask(reportId, $"{basePath}.overall_confidence", "Low overall confidence"));
            }

            if (result.ReferenceRange is not null)
            {
                AddIfLow(tasks, reportId, $"{basePath}.reference_range.low", result.ReferenceRange.Low);
                AddIfLow(tasks, reportId, $"{basePath}.reference_range.high", result.ReferenceRange.High);
                AddIfLow(tasks, reportId, $"{basePath}.reference_range.text", result.ReferenceRange.Text);
                AddIfLow(tasks, reportId, $"{basePath}.reference_range.unit", result.ReferenceRange.Unit);
            }
        }

        for (var i = 0; i < document.Comments.Count; i++)
        {
            var comment = document.Comments[i];
            AddIfLow(tasks, reportId, $"comments[{i}].category", comment.Category);
            AddIfLow(tasks, reportId, $"comments[{i}].text", comment.Text);
        }

        for (var i = 0; i < document.AdministrativeEvents.Count; i++)
        {
            var admin = document.AdministrativeEvents[i];
            AddIfLow(tasks, reportId, $"administrative_events[{i}].event_type", admin.EventType);
            AddIfLow(tasks, reportId, $"administrative_events[{i}].description", admin.Description);
            AddIfLow(tasks, reportId, $"administrative_events[{i}].event_date", admin.EventDate);
        }

        return tasks;
    }

    private void AddIfLow(List<ReviewTask> tasks, Guid reportId, string fieldPath, ExtractedStringValueDto? value)
    {
        if (value?.ExtractionConfidence is null)
        {
            return;
        }

        if (value.ExtractionConfidence.Value < _threshold)
        {
            tasks.Add(CreateTask(reportId, fieldPath, "Low confidence value"));
        }
    }

    private void AddIfLow(List<ReviewTask> tasks, Guid reportId, string fieldPath, ExtractedNumberValueDto? value)
    {
        if (value?.ExtractionConfidence is null)
        {
            return;
        }

        if (value.ExtractionConfidence.Value < _threshold)
        {
            tasks.Add(CreateTask(reportId, fieldPath, "Low confidence value"));
        }
    }

    private void AddIfLow(List<ReviewTask> tasks, Guid reportId, string fieldPath, ExtractedDateValueDto? value)
    {
        if (value?.ExtractionConfidence is null)
        {
            return;
        }

        if (value.ExtractionConfidence.Value < _threshold)
        {
            tasks.Add(CreateTask(reportId, fieldPath, "Low confidence value"));
        }
    }

    private ReviewTask CreateTask(Guid reportId, string fieldPath, string reason)
    {
        return new ReviewTask
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            FieldPath = fieldPath,
            Reason = reason,
            Status = ReviewTaskStatus.Open,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }
}
