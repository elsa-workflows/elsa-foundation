namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Where a first-party EF module connects when a host names neither a connection string nor a connection
/// name. Every module shares one connection by default, so a host configures <c>ConnectionStrings:Elsa</c>
/// once and overrides a module only when it deliberately splits storage.
/// </summary>
public static class EfConnectionDefaults
{
    public const string ConnectionName = "Elsa";
    public const string SqliteConnectionString = "Data Source=elsa.db";
}
