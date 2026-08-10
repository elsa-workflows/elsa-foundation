using System.Data.Common;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Opens a raw connection to one Groundwork target's database.
/// <para>
/// A handful of read paths — the dashboard tiles — query the physical document tables directly instead of
/// going through <c>IDocumentStore</c>, because they aggregate across document kinds in one statement. They
/// still need to reach the right target, and <see cref="GroundworkTargetDeclaration"/> deliberately keeps
/// only a fingerprint of the connection so it can never hand one out. The provider leaf, which already holds
/// the connection string, supplies this instead, keyed by target.
/// </para>
/// <para>
/// This is for aggregate reads only. Anything that writes, or that needs tenancy and schema admission, goes
/// through the document store.
/// </para>
/// </summary>
public interface IGroundworkTargetConnectionSource
{
    /// <summary>The target this source opens.</summary>
    string TargetName { get; }

    /// <summary>The provider leaf identity, so a caller can pick the right SQL dialect.</summary>
    string ProviderIdentity { get; }

    /// <summary>Creates a new, unopened connection to the target's database.</summary>
    DbConnection CreateConnection();
}
