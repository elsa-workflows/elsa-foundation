namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// A file-backed SQLite database for one test. File-backed rather than <c>:memory:</c> because these tests
/// apply schema on one connection and read it back on another.
/// </summary>
internal sealed class TemporarySqliteDatabase(string prefix = "elsa-groundwork-unified") : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={_path}";

    public ValueTask DisposeAsync()
    {
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }
}
