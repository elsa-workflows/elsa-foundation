namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// <see cref="EfDatabaseMigrator"/>'s own fail-closed signal under <see cref="EfMigratePolicy.Validate"/>:
/// the database was reached and read successfully, and it has pending migrations. Kept as its own type —
/// rather than a plain <see cref="InvalidOperationException"/> — so a caller can tell this negative result
/// apart from a genuine connectivity, credential or schema failure that <c>GetPendingMigrationsAsync</c>
/// can throw as an <see cref="InvalidOperationException"/> too, before this check is ever reached.
/// </summary>
/// <remarks>
/// Not sealed for one reason: <see cref="SchemaFinalization.EfContractingMigrationRefusedException"/> is a pending batch
/// that also may not be applied yet (spec 185, FR-024), so every caller that already treats pending migrations as a
/// negative result rather than a database failure does the same for it without a change.
/// </remarks>
public class EfPendingMigrationsException(string message) : InvalidOperationException(message);
