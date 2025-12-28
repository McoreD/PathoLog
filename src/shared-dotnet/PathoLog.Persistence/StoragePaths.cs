namespace PathoLog.Persistence;

public sealed class StoragePaths
{
    public StoragePaths(string databasePath, string filesRootPath, string migrationsPath)
    {
        DatabasePath = databasePath;
        FilesRootPath = filesRootPath;
        MigrationsPath = migrationsPath;
    }

    public string DatabasePath { get; }
    public string FilesRootPath { get; }
    public string MigrationsPath { get; }
}
