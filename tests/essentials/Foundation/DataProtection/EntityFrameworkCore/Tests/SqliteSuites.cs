namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>The shared key ring suite on SQLite, a file of its own per store. The container legs run it in ProviderTests.</summary>
public sealed class SqliteSharedKeyRingTests(SqliteKeyRingDatabase database) : SharedKeyRingTests(database), IClassFixture<SqliteKeyRingDatabase>;
