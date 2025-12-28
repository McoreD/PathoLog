using Microsoft.Azure.Functions.Worker.Http;

public static class ApiAuth
{
    public static async Task<UserRecord?> TryGetUserAsync(HttpRequestData req, string cs)
    {
        var principal = AuthHelper.GetPrincipal(req);
        if (principal != null)
        {
            return await Data.UpsertUser(cs, principal);
        }

        if (AuthHelper.AllowAnonymous(req))
        {
            var local = new AuthPrincipal("local", "local", "local@patholog.dev", "Local User");
            return await Data.UpsertUser(cs, local);
        }

        return null;
    }

    public static async Task<UserRecord> RequireUserAsync(HttpRequestData req, string cs)
    {
        var user = await TryGetUserAsync(req, cs);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Authentication required.");
        }
        return user;
    }
}
