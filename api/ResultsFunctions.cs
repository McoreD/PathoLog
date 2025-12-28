using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;

public class ResultsFunctions
{
    private readonly string _cs;

    public ResultsFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("ResultsByPatient")]
    public async Task<HttpResponseData> ListResultsByPatient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}/results")] HttpRequestData req,
        string patientId)
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

            var query = QueryHelpers.ParseQuery(req.Url.Query ?? string.Empty);
            var analyte = query.TryGetValue("analyte_short_code", out var analyteValues) ? analyteValues.FirstOrDefault() : null;
            DateTime? from = query.TryGetValue("from", out var fromValues) && DateTime.TryParse(fromValues.FirstOrDefault(), out var fromDate) ? fromDate : null;
            DateTime? to = query.TryGetValue("to", out var toValues) && DateTime.TryParse(toValues.FirstOrDefault(), out var toDate) ? toDate : null;

            var results = await Data.ListResultsForPatient(_cs, user.Id, id, analyte, from, to);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { results = results.Select(ToResultResponse) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsByReport")]
    public async Task<HttpResponseData> ListResultsByReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/{reportId}/results")] HttpRequestData req,
        string reportId)
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
            await res.WriteAsJsonAsync(new { results = results.Select(ToResultResponse) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsTrend")]
    public async Task<HttpResponseData> Trend(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}/trend")] HttpRequestData req,
        string patientId)
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

            var query = QueryHelpers.ParseQuery(req.Url.Query ?? string.Empty);
            var analyte = query.TryGetValue("analyte_short_code", out var analyteValues) ? analyteValues.FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(analyte))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "analyte_short_code is required" });
                return bad;
            }

            var results = await Data.ListResultsForPatient(_cs, user.Id, id, analyte, null, null);
            var series = results
                .OrderBy(r => r.ReportedDatetimeLocal)
                .Select(r => new TrendPoint(
                    r.Id.ToString(),
                    r.ReportedDatetimeLocal,
                    null,
                    r.ValueNumeric,
                    r.ValueText,
                    r.UnitOriginal,
                    r.UnitNormalised,
                    r.FlagSeverity,
                    r.ExtractionConfidence,
                    null,
                    null))
                .ToList();

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { analyte_short_code = analyte, series });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsAnomalies")]
    public async Task<HttpResponseData> Anomalies(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}/integrity/anomalies")] HttpRequestData req,
        string patientId)
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

            var results = await Data.ListResultsForPatient(_cs, user.Id, id, null, null, null);
            var anomalies = new List<AnomalyResponse>();
            var byCode = results.GroupBy(r => r.AnalyteShortCode ?? r.AnalyteNameOriginal);

            foreach (var group in byCode)
            {
                var units = group
                    .Select(r => r.UnitNormalised ?? r.UnitOriginal)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct()
                    .ToList();
                if (units.Count > 1)
                {
                    anomalies.Add(new AnomalyResponse(group.Key, "unit_mismatch", units));
                }

                var numerics = group
                    .Where(r => r.ValueNumeric.HasValue)
                    .OrderBy(r => r.ReportedDatetimeLocal ?? DateTime.MinValue)
                    .ToList();
                for (var i = 1; i < numerics.Count; i++)
                {
                    var prev = numerics[i - 1].ValueNumeric ?? 0;
                    var curr = numerics[i].ValueNumeric ?? 0;
                    if (prev == 0)
                    {
                        continue;
                    }
                    var ratio = curr / prev;
                    if (ratio > 3 || ratio < 0.33m)
                    {
                        anomalies.Add(new AnomalyResponse(group.Key, "sudden_change", new
                        {
                            previous = prev,
                            current = curr,
                            reported_at = numerics[i].ReportedDatetimeLocal
                        }));
                        break;
                    }
                }
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { anomalies });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsConfirmMapping")]
    public async Task<HttpResponseData> ConfirmMapping(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "results/{resultId}/confirm-mapping")] HttpRequestData req,
        string resultId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(resultId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var payload = await req.ReadFromJsonAsync<MappingConfirmRequest>();
            if (payload == null || string.IsNullOrWhiteSpace(payload.AnalyteShortCode))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "analyte_short_code required" });
                return bad;
            }
            var updated = await Data.UpdateResultShortCode(_cs, user.Id, id, payload.AnalyteShortCode);
            if (updated == 0)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { result = new { id = resultId, analyte_short_code = payload.AnalyteShortCode } });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsCorrection")]
    public async Task<HttpResponseData> CorrectResult(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "results/{resultId}/correction")] HttpRequestData req,
        string resultId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(resultId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var payload = await req.ReadFromJsonAsync<ResultCorrectionRequest>() ?? new ResultCorrectionRequest(null, null, null, null, null, null);
            var updated = await Data.UpdateResultCorrection(_cs, user.Id, id, payload);
            if (updated == 0)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { result = new { id = resultId } });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("ResultsExportPatient")]
    public async Task<HttpResponseData> ExportPatient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}/results/export")] HttpRequestData req,
        string patientId)
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
            var query = QueryHelpers.ParseQuery(req.Url.Query ?? string.Empty);
            var analyte = query.TryGetValue("analyte_short_code", out var analyteValues) ? analyteValues.FirstOrDefault() : null;

            var results = await Data.ListResultsForPatient(_cs, user.Id, id, analyte, null, null);
            var rows = new List<string>
            {
                "reported_datetime,analyte,short_code,value_numeric,value_text,unit,flag_severity"
            };
            foreach (var r in results)
            {
                rows.Add(string.Join(',', new[]
                {
                    r.ReportedDatetimeLocal?.ToString("O") ?? "",
                    EscapeCsv(r.AnalyteNameOriginal),
                    EscapeCsv(r.AnalyteShortCode),
                    r.ValueNumeric?.ToString() ?? "",
                    EscapeCsv(r.ValueText),
                    EscapeCsv(r.UnitNormalised ?? r.UnitOriginal),
                    EscapeCsv(r.FlagSeverity)
                }));
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "text/csv");
            res.Headers.Add("Content-Disposition", $"attachment; filename=\"patient-{patientId}-results.csv\"");
            await res.WriteStringAsync(string.Join('\n', rows));
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
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
            reportedDatetimeLocal = r.ReportedDatetimeLocal,
            extractionConfidence = r.ExtractionConfidence,
            flagSeverity = r.FlagSeverity
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
