using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class MappingFunctions
{
    private readonly string _cs;

    public MappingFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("MappingDictionaryUpsert")]
    public async Task<HttpResponseData> UpsertMapping([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mapping-dictionary")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var payload = await req.ReadFromJsonAsync<MappingEntryRequest>();
            if (payload == null || string.IsNullOrWhiteSpace(payload.AnalyteNamePattern) || string.IsNullOrWhiteSpace(payload.AnalyteShortCode))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "analyte_name_pattern and analyte_short_code are required" });
                return bad;
            }

            await Data.UpsertMappingEntry(_cs, user.Id, payload.AnalyteNamePattern, payload.AnalyteShortCode);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(new { entry = payload });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }
}
