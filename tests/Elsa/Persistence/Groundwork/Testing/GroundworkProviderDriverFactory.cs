namespace Elsa.Persistence.Groundwork.Testing;

public static class GroundworkProviderDriverFactory
{
    public static GroundworkProviderDriver Create(string providerKey) => providerKey switch
    {
        "sqlite" => new SqliteGroundworkProviderDriver(),
        "sqlserver" => new SqlServerGroundworkProviderDriver(),
        "postgresql" => new PostgreSqlGroundworkProviderDriver(),
        "mongodb" => new MongoDbGroundworkProviderDriver(),
        _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, "Unknown Groundwork provider.")
    };
}
