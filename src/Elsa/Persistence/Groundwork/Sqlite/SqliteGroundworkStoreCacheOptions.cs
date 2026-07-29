namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>Controls reuse of immutable access-bound SQLite store adapters.</summary>
public sealed class SqliteGroundworkStoreCacheOptions
{
    public const int DefaultCapacity = 256;

    /// <summary>Whether access-bound stores are reused across persistence operations.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum retained access bindings per shell.</summary>
    public int Capacity { get; set; } = DefaultCapacity;

    internal void Validate()
    {
        if (Enabled && Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Capacity),
                Capacity,
                "The SQLite access-bound store cache capacity must be positive when caching is enabled.");
        }
    }
}
