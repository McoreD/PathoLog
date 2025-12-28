using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class ReportsFunctions
{
    private readonly string _cs;

    public ReportsFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("ReportsList")]
    public async Task<HttpResponseData> ListReports([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}/reports")] HttpRequestData req, string patientId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(patientId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var patient = await Data.GetPatient(_cs, user.Id, id);
            if (patient == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var reports = await Data.ListReportsForPatient(_cs, user.Id, id);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { reports = reports.Select(r => ToReportResponse(r)) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsCreate")]
    public async Task<HttpResponseData> CreateReport([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "patients/{patientId}/reports")] HttpRequestData req, string patientId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(patientId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var patient = await Data.GetPatient(_cs, user.Id, id);
            if (patient == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var upload = await req.ReadFromJsonAsync<FileUploadRequest>();
            if (upload == null || string.IsNullOrWhiteSpace(upload.Filename) || string.IsNullOrWhiteSpace(upload.ContentBase64))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "File payload is required" });
                return bad;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(upload.ContentBase64);
            }
            catch
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid base64 payload" });
                return bad;
            }

            var fileId = await Data.CreateSourceFile(_cs, upload.Filename, upload.ContentType, bytes.Length, bytes);
            var report = await Data.CreateReport(_cs, id, fileId);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(new
            {
                report = ToReportResponse(report, upload.Filename)
            });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsNeedsReview")]
    public async Task<HttpResponseData> NeedsReview([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/needs-review")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var reports = await Data.ListNeedsReview(_cs, user.Id);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new
            {
                reports = await Task.WhenAll(reports.Select(async r =>
                {
                    var patient = await Data.GetPatient(_cs, user.Id, r.PatientId);
                    return new
                    {
                        id = r.Id,
                        parsingStatus = r.ParsingStatus,
                        createdAtUtc = r.CreatedAtUtc,
                        sourceFile = r.OriginalFilename == null ? null : new { originalFilename = r.OriginalFilename },
                        patient = patient == null ? null : new { fullName = patient.FullName }
                    };
                }))
            });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsGet")]
    public async Task<HttpResponseData> GetReport([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/{reportId}")] HttpRequestData req, string reportId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(reportId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var report = await Data.GetReport(_cs, user.Id, id);
            if (report == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var results = await Data.ListResultsForReport(_cs, user.Id, id);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new
            {
                report = new
                {
                    id = report.Id,
                    parsingStatus = report.ParsingStatus,
                    createdAtUtc = report.CreatedAtUtc,
                    sourceFile = report.OriginalFilename == null ? null : new { originalFilename = report.OriginalFilename },
                    results = results.Select(ToResultResponse)
                }
            });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsFile")]
    public async Task<HttpResponseData> GetReportFile([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/{reportId}/file")] HttpRequestData req, string reportId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(reportId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var file = await Data.GetReportFile(_cs, user.Id, id);
            if (file == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", file.Value.ContentType ?? "application/pdf");
            res.Headers.Add("Content-Disposition", $"inline; filename=\"{file.Value.Filename}\"");
            await res.WriteBytesAsync(file.Value.Bytes);
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsExport")]
    public async Task<HttpResponseData> ExportReport([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/{reportId}/export")] HttpRequestData req, string reportId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(reportId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var results = await Data.ListResultsForReport(_cs, user.Id, id);
            var rows = new List<string>
            {
                "analyte,short_code,value_numeric,value_text,unit,flag_severity,reported_datetime"
            };
            foreach (var r in results)
            {
                rows.Add(string.Join(',', new[]
                {
                    EscapeCsv(r.AnalyteNameOriginal),
                    EscapeCsv(r.AnalyteShortCode),
                    r.ValueNumeric?.ToString() ?? "",
                    EscapeCsv(r.ValueText),
                    EscapeCsv(r.UnitNormalised ?? r.UnitOriginal),
                    EscapeCsv(r.FlagSeverity),
                    r.ReportedDatetimeLocal?.ToString("O") ?? ""
                }));
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "text/csv");
            res.Headers.Add("Content-Disposition", $"attachment; filename=\"report-{reportId}.csv\"");
            await res.WriteStringAsync(string.Join('\n', rows));
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ReportsParsed")]
    public async Task<HttpResponseData> IngestParsed([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/{reportId}/parsed")] HttpRequestData req, string reportId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(reportId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var report = await Data.GetReport(_cs, user.Id, id);
            if (report == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var payload = await req.ReadFromJsonAsync<ParsedPayload>();
            if (payload == null || payload.Results == null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid parsed payload" });
                return bad;
            }

            await Data.DeleteResultsForReport(_cs, id);
            await Data.InsertResults(_cs, id, report.PatientId, payload.Results);
            await Data.UpdateReportStatus(_cs, id, "completed");

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { status = "ingested" });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    private static ReportResponse ToReportResponse(ReportRecord report, string? fallbackFilename = null)
    {
        var filename = report.OriginalFilename ?? fallbackFilename;
        SourceFileResponse? source = filename == null ? null : new SourceFileResponse(filename);
        return new ReportResponse(report.Id.ToString(), report.ParsingStatus, report.CreatedAtUtc, source);
    }

    private static object ToResultResponse(ResultRecord r)
    {
        return new
        {
            id = r.Id,
            analyteNameOriginal = r.AnalyteNameOriginal,
            analyteShortCode = r.AnalyteShortCode,
            resultType = r.ResultType,
            valueNumeric = r.ValueNumeric,
            valueText = r.ValueText,
            unitOriginal = r.UnitOriginal,
            unitNormalised = r.UnitNormalised,
            reportedDatetimeLocal = r.ReportedDatetimeLocal
        };
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value.Replace("\"", "\"\"");
        return sanitized.Contains(',') || sanitized.Contains('\n') || sanitized.Contains('\r')
            ? $"\"{sanitized}\""
            : sanitized;
    }
}
