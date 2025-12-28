using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

public record AuthPrincipal(string Provider, string UserId, string Email, string DisplayName);

public static class AuthHelper
{
    public static bool AllowAnonymous(HttpRequestData req)
    {
        return string.Equals(
            req.FunctionContext?.InstanceServices?.GetService(typeof(IConfiguration)) is IConfiguration cfg
                ? cfg["ALLOW_ANONYMOUS_AUTH"]
                : Environment.GetEnvironmentVariable("ALLOW_ANONYMOUS_AUTH"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    public static AuthPrincipal? GetPrincipal(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("x-ms-client-principal", out var values))
        {
            return null;
        }

        var encoded = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null)
            {
                return null;
            }

            var roles = node["userRoles"]?.AsArray()?.Select(r => r?.ToString()).Where(r => !string.IsNullOrWhiteSpace(r)).ToList()
                        ?? new List<string>();
            if (!roles.Contains("authenticated"))
            {
                return null;
            }

            var provider = node["identityProvider"]?.ToString();
            var userId = node["userId"]?.ToString();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var claims = node["claims"]?.AsArray();
            var email = ResolveEmail(claims, node["userDetails"]?.ToString());
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var displayName = ResolveDisplayName(claims, email);

            return new AuthPrincipal(provider, userId, email, displayName);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveEmail(JsonArray? claims, string? userDetails)
    {
        var claim = FindClaim(claims, "preferred_username")
                    ?? FindClaim(claims, "email")
                    ?? FindClaim(claims, "upn");
        if (!string.IsNullOrWhiteSpace(claim) && claim.Contains("@", StringComparison.Ordinal))
        {
            return claim;
        }

        if (!string.IsNullOrWhiteSpace(userDetails) && userDetails.Contains("@", StringComparison.Ordinal))
        {
            return userDetails;
        }

        return null;
    }

    private static string ResolveDisplayName(JsonArray? claims, string fallback)
    {
        var claim = FindClaim(claims, "name") ?? FindClaim(claims, "given_name");
        if (!string.IsNullOrWhiteSpace(claim))
        {
            return claim;
        }

        return fallback;
    }

    private static string? FindClaim(JsonArray? claims, string type)
    {
        if (claims is null)
        {
            return null;
        }

        foreach (var claimNode in claims)
        {
            if (claimNode is not JsonObject obj)
            {
                continue;
            }

            var typ = obj["typ"]?.ToString();
            if (!string.Equals(typ, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return obj["val"]?.ToString();
        }

        return null;
    }
}
