using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<DbOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
            {
                var raw = cfg["DATABASE_URL"] ?? cfg["DB"] ?? string.Empty;
                opts.ConnectionString = ConnectionStringHelper.Normalize(raw);
            });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DbOptions>>().Value);
    })
    .Build();

var dbOptions = host.Services.GetRequiredService<DbOptions>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

if (string.IsNullOrWhiteSpace(dbOptions.ConnectionString))
{
    logger.LogWarning("Database connection string not provided; set DATABASE_URL or DB.");
}
else
{
    try
    {
        var sqlFolder = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "sql");
        await Migrations.ApplyAsync(dbOptions.ConnectionString, sqlFolder, logger);
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed: {Message}", ex.Message);
        throw;
    }
}

host.Run();
