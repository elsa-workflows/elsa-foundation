namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// <see cref="EfDatabaseMigrator"/>'s own fail-closed signal under <see cref="EfMigratePolicy.Validate"/>:
/// the database was reached and read successfully, and it has pending migrations. Kept as its own type —
/// rather than a plain <see cref="InvalidOperationException"/> — so a caller can tell this negative result
/// apart from a genuine connectivity, credential or schema failure that <c>GetPendingMigrationsAsync</c>
/// can throw as an <see cref="InvalidOperationException"/> too, before this check is ever reached.
/// </summary>
public sealed class EfPendingMigrationsException(string message) : InvalidOperationException(message);
