using MongoDB.Driver;

namespace Elsa.Persistence.Groundwork.MongoDb.Targets;

/// <summary>
/// Opens one Groundwork target's MongoDB database.
/// <para>
/// This is the document-database counterpart of <c>IGroundworkTargetConnectionSource</c>, and exists for the
/// same reason: the dashboard tiles aggregate across document kinds in one server-side pipeline rather than
/// going through <c>IDocumentStore</c>, so they need to reach a target's database directly, and
/// <c>GroundworkTargetDeclaration</c> deliberately keeps only a fingerprint of the connection so it can never
/// hand one out. The provider leaf already holds the connection string and database name, so it supplies
/// this, keyed by target.
/// </para>
/// <para>
/// It is a separate contract rather than a member of the relational one because the two return different
/// things -- a <c>DbConnection</c> against a database with one shared document table, versus an
/// <see cref="IMongoDatabase"/> with a collection per document kind -- and no caller can use one where it
/// wants the other. Keeping them apart also keeps the MongoDB driver out of the provider-neutral composition
/// project, which is why this lives in the leaf.
/// </para>
/// <para>
/// For aggregate reads only. Anything that writes, or that needs tenancy and schema admission, goes through
/// the document store.
/// </para>
/// </summary>
public interface IGroundworkTargetMongoDatabaseSource
{
    /// <summary>The target this source opens.</summary>
    string TargetName { get; }

    /// <summary>The database this target addresses, for diagnostics that must not quote a connection string.</summary>
    string DatabaseName { get; }

    /// <summary>Opens the target's database.</summary>
    IMongoDatabase GetDatabase();
}
