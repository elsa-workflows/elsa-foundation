using System.Data.Common;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>A provider leaf's connection source for one target, closing over the connection string.</summary>
public sealed class GroundworkTargetConnectionSource(
    string targetName,
    string providerIdentity,
    Func<DbConnection> connectionFactory) : IGroundworkTargetConnectionSource
{
    public string TargetName { get; } = GroundworkTargetNames.Normalize(targetName);

    public string ProviderIdentity { get; } = providerIdentity;

    public DbConnection CreateConnection() => connectionFactory();
}
