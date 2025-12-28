using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class UserFunctions
{
    private readonly string _cs;

    public UserFunctions(DbOptions options)
    {
        _cs = options.ConnectionString;
    }

    [Function("MeGet")]
    public async Task<HttpResponseData> GetMe([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { user = ToResponse(user) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    [Function("MePatch")]
    public async Task<HttpResponseData> UpdateMe([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "me")] HttpRequestData req)
    {
        try
        {
            var user = await ApiAuth.RequireUserAsync(req, _cs);
            var payload = await req.ReadFromJsonAsync<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
            payload.TryGetValue("fullName", out var fullName);
            var normalized = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
            var updated = await Data.UpdateUserName(_cs, user.Id, normalized);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { user = ToResponse(updated) });
            return res;
        }
        catch (UnauthorizedAccessException)
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
    }

    private static UserResponse ToResponse(UserRecord user)
    {
        return new UserResponse(
            user.Id.ToString(),
            user.Email,
            user.FullName,
            !string.IsNullOrWhiteSpace(user.GoogleSub),
            !string.IsNullOrWhiteSpace(user.MicrosoftSub));
    }
}
