using System.Net;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class DiagnosticsFunctions
{
    private readonly string _cs;

    public DiagnosticsFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("DiagnosticsDb")]
    public async Task<HttpResponseData> DiagnosticsDb([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "diagnostics/db")] HttpRequestData req)
    {
        try
        {
            _ = await ApiAuth.RequireUserAsync(req, _cs);

            await using var db = Data.Conn(_cs);
            var tables = await db.QueryAsync<string>(
                "select table_name from information_schema.tables where table_schema = 'public' order by table_name");

            var counts = new
            {
                users = await db.ExecuteScalarAsync<int>("select count(*) from users"),
                patients = await db.ExecuteScalarAsync<int>("select count(*) from patients"),
                reports = await db.ExecuteScalarAsync<int>("select count(*) from reports"),
                results = await db.ExecuteScalarAsync<int>("select count(*) from results"),
                mappingDictionary = await db.ExecuteScalarAsync<int>("select count(*) from mapping_dictionary"),
                aiSettings = await db.ExecuteScalarAsync<int>("select count(*) from ai_settings")
            };

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new
            {
                ok = true,
                tables = tables.ToList(),
                counts
            });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
        catch (Exception ex)
        {
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Diagnostics failed", detail = ex.Message });
            return res;
        }
    }
}
