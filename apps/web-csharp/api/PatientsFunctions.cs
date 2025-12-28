using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class PatientsFunctions
{
    private readonly string _cs;

    public PatientsFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("PatientsList")]
    public async Task<HttpResponseData> ListPatients([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var patients = await Data.ListPatients(_cs, user.Id);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { patients = patients.Select(ToResponse) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("PatientsGet")]
    public async Task<HttpResponseData> GetPatient([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patients/{patientId}")] HttpRequestData req, string patientId)
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
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { patient = ToResponse(patient) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("PatientsCreate")]
    public async Task<HttpResponseData> CreatePatient([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "patients")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var payload = await req.ReadFromJsonAsync<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
            payload.TryGetValue("fullName", out var fullName);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "fullName is required" });
                return bad;
            }

            DateOnly? dob = null;
            if (payload.TryGetValue("dob", out var dobRaw) && DateOnly.TryParse(dobRaw, out var parsedDob))
            {
                dob = parsedDob;
            }
            payload.TryGetValue("sex", out var sex);

            var patient = await Data.CreatePatient(_cs, user.Id, fullName.Trim(), dob, string.IsNullOrWhiteSpace(sex) ? null : sex);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(new { patient = ToResponse(patient) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("PatientsUpdate")]
    public async Task<HttpResponseData> UpdatePatient([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "patients/{patientId}")] HttpRequestData req, string patientId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(patientId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var payload = await req.ReadFromJsonAsync<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
            payload.TryGetValue("fullName", out var fullName);
            payload.TryGetValue("dob", out var dobRaw);
            payload.TryGetValue("sex", out var sex);
            var current = await Data.GetPatient(_cs, user.Id, id);
            if (current == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var name = string.IsNullOrWhiteSpace(fullName) ? current.FullName : fullName.Trim();
            DateOnly? dob = current.Dob;
            if (!string.IsNullOrWhiteSpace(dobRaw))
            {
                if (DateOnly.TryParse(dobRaw, out var parsedDob))
                {
                    dob = parsedDob;
                }
            }
            var updated = await Data.UpdatePatient(_cs, user.Id, id, name, dob, string.IsNullOrWhiteSpace(sex) ? current.Sex : sex);
            if (updated == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { patient = ToResponse(updated) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("PatientsDelete")]
    public async Task<HttpResponseData> DeletePatient([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "patients/{patientId}")] HttpRequestData req, string patientId)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            if (!Guid.TryParse(patientId, out var id))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var deleted = await Data.DeletePatient(_cs, user.Id, id);
            if (!deleted)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    private static PatientResponse ToResponse(PatientRecord patient)
    {
        return new PatientResponse(patient.Id.ToString(), patient.FullName, patient.Dob, patient.Sex);
    }
}
