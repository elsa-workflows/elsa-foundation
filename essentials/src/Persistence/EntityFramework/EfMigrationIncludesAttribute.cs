namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Declares that an already recorded cumulative migration includes an earlier migration's schema.
/// Used for forward compatibility when retaining the history preceding a previously written cumulative Initial.
/// It neither inserts nor replaces physical history rows. Fresh installations still apply the retained chain.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EfMigrationIncludesAttribute : Attribute
{
    public EfMigrationIncludesAttribute(string migrationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        MigrationId = migrationId;
    }

    public string MigrationId { get; }
}
