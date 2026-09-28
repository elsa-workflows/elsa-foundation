namespace Elsa.Cluster.EntityFrameworkCore.Testing;

/// <summary>
/// The database a membership suite runs against: an engine, a connection to its primary, and why it is unavailable when
/// it is. A suite that finds <see cref="SkipReason"/> set reports every test skipped rather than passed.
/// </summary>
public sealed record EfClusterMembershipTestStore(string Provider, string ConnectionString, string? SkipReason = null) : IDisposable
{
    private string? _ownedFile;

    /// <summary>A fresh SQLite file of its own, deleted when the store is disposed.</summary>
    public static EfClusterMembershipTestStore CreateSqlite()
    {
        var file = Path.Join(Path.GetTempPath(), $"elsa-cluster-membership-{Guid.NewGuid():N}.db");
        // Unpooled, so no connection outlives a test and keeps the file open.
        return new EfClusterMembershipTestStore("Sqlite", $"Data Source={file};Pooling=False") { _ownedFile = file };
    }

    /// <summary>A store every test of a suite skips, because the engine could not be started.</summary>
    public static EfClusterMembershipTestStore Unavailable(string provider, string reason) => new(provider, "", reason);

    public void Dispose()
    {
        if (_ownedFile is null)
            return;
        foreach (var file in new[] { _ownedFile, _ownedFile + "-journal", _ownedFile + "-wal", _ownedFile + "-shm" })
            File.Delete(file);
    }
}
