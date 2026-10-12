namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>Where hosts keep a key ring: an engine and a connection to an empty database of its own.</summary>
public sealed record KeyRingStore(string Provider, string ConnectionString)
{
    public const string KeyStoreSection = $"{DataProtectionConfigurationExtensions.SectionName}:{EfDataProtectionKeyStoreOptions.SectionKey}";

    /// <summary>The settings that enable the EF key store on this database, as a host is configured with them.</summary>
    public Dictionary<string, string?> Settings() => new()
    {
        [$"{KeyStoreSection}:{DataProtectionConfigurationExtensions.EnabledKey}"] = "true",
        [$"{KeyStoreSection}:{nameof(EfDataProtectionKeyStoreOptions.Provider)}"] = Provider,
        [$"{KeyStoreSection}:{nameof(EfDataProtectionKeyStoreOptions.ConnectionString)}"] = ConnectionString
    };
}

/// <summary>
/// One engine a key ring suite runs on, creating an empty database per store, so no test reads a key another wrote. A suite that
/// finds <see cref="SkipReason"/> set reports every test skipped rather than passed.
/// </summary>
public interface IKeyRingDatabase
{
    string? SkipReason { get; }

    Task<KeyRingStore> CreateStoreAsync();
}

/// <summary>A SQLite file per store, deleted when the database is disposed.</summary>
public sealed class SqliteKeyRingDatabase : IKeyRingDatabase, IDisposable
{
    private readonly List<string> _files = [];

    public string? SkipReason => null;

    public Task<KeyRingStore> CreateStoreAsync()
    {
        var file = Path.Join(Path.GetTempPath(), $"elsa-data-protection-{Guid.NewGuid():N}.db");
        lock (_files)
            _files.Add(file);
        // Unpooled, so no connection outlives a host and keeps the file open.
        return Task.FromResult(new KeyRingStore("Sqlite", $"Data Source={file};Pooling=False"));
    }

    public void Dispose()
    {
        lock (_files)
            foreach (var file in _files.SelectMany(file => new[] { file, file + "-journal", file + "-wal", file + "-shm" }))
                File.Delete(file);
    }
}
