using System;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

public static class ConnectionStringHelper
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (!raw.TrimStart().StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
            !raw.TrimStart().StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/')
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                builder.Username = Uri.UnescapeDataString(parts[0]);
            }
            if (parts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        var unsupported = new[] { "channel_binding" };
        foreach (var kvp in query)
        {
            if (!unsupported.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder[kvp.Key] = kvp.Value.LastOrDefault();
            }
        }

        return builder.ToString();
    }
}
