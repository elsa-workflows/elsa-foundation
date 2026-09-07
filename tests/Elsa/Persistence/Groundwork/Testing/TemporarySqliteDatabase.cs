using Groundwork.Sqlite;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>A file-backed SQLite provider connection on a temporary path, removed on disposal.</summary>
public sealed class TemporarySqliteDatabase : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("elsa-groundwork-").FullName;

    public TemporarySqliteDatabase() =>
        Connection = new SqliteProviderFactory().Create($"Data Source={Path.Combine(directory, "store.db")}");

    public IStorageProviderConnection Connection { get; }

    public void Dispose()
    {
        Connection.Dispose();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
