namespace Elsa.Persistence.EFCore.Sqlite.Constants;

/// <summary>
/// Constants.
/// </summary>
public static class SqliteConstants
{
    /// <summary>
    /// Default connection string for SQLite. Deliberately private-cache: in shared-cache mode readers take
    /// table locks, so diagnostics queries racing a background writer fail it with SQLITE_LOCKED (issue #607).
    /// File-based databases get concurrent readers via write-ahead logging instead (see
    /// <c>SqliteWalConnectionInterceptor</c>). In-memory databases that need to share state across
    /// connections must build their own <c>Mode=Memory;Cache=Shared</c> connection string.
    /// </summary>
    public const string DefaultConnectionString = "Data Source=elsa.sqlite.db;";
}