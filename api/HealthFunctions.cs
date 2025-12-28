using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class HealthFunctions
{
    private readonly string _cs;

    public HealthFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);

        if (string.IsNullOrWhiteSpace(_cs))
        {
            await res.WriteAsJsonAsync(new { status = "unhealthy", database = "not configured" });
            return res;
        }

        try
        {
            await using var db = Data.Conn(_cs);
            await db.OpenAsync();
            await res.WriteAsJsonAsync(new { status = "healthy", database = "connected" });
        }
        catch (Exception ex)
        {
            res = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await res.WriteAsJsonAsync(new { status = "unhealthy", database = "disconnected", error = ex.Message });
        }

        return res;
    }
}
