using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class AiSettingsFunctions
{
    private readonly string _cs;

    public AiSettingsFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("AiSettingsGet")]
    public async Task<HttpResponseData> GetSettings([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ai/settings")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var providers = await Data.ListAiProviders(_cs, user.Id);
            var active = providers.FirstOrDefault()?.Provider;
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new AiSettingsResponse(active, providers.ToList()));
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("AiSettingsPost")]
    public async Task<HttpResponseData> UpdateSettings([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/settings")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var payload = await req.ReadFromJsonAsync<AiSettingsRequest>();
            var provider = payload?.Provider?.Trim().ToLowerInvariant() ?? "openai";
            if (payload == null || !new[] { "openai", "gemini" }.Contains(provider))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Unsupported provider" });
                return bad;
            }

            var apiKey = payload.ApiKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await Data.DeleteAiKey(_cs, user.Id, provider);
                var res = req.CreateResponse(HttpStatusCode.OK);
                await res.WriteAsJsonAsync(new { provider, hasKey = false });
                return res;
            }

            await Data.UpsertAiKey(_cs, user.Id, provider, apiKey);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { provider, hasKey = true });
            return ok;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }
}
