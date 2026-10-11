using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.EntityFrameworkCore;

public static class ClusterMembershipEfModule
{
    /// <summary>The module name operators select it by in the persistence tool (ADR 0076 D3).</summary>
    public const string Name = "Cluster.Membership";

    public const string HistoryModuleName = "ElsaClusterMembership";
    public const string TableName = "elsa_cluster_members";

    /// <summary>The persisted-schema version every membership row is stamped with, and read against.</summary>
    /// <remarks>
    /// A row stamped with a version outside the family's readable set (<see cref="Chain"/>) was written by a provider
    /// version this build does not run. Unlike the other modules' stores, a reader does not refuse it: it returns the
    /// entry with an unknown report, which counts as reading nothing (spec 183, FR-012), so a newer member blocks every
    /// readability answer rather than dropping out of it.
    /// </remarks>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to.</summary>
    public const string SchemaFamily = "ClusterMembership";

    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(ClusterMembershipEfModule).Assembly, SchemaFamily);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
