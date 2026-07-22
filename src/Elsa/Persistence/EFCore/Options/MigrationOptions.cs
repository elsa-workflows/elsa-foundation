namespace Elsa.Persistence.EFCore.Options;

public sealed class MigrationOptions
{
    /// <summary>
    /// Gets or sets a collection that determines whether Entity Framework Core migrations
    /// should be executed for specific DbContext types.
    /// </summary>
    /// <remarks>
    /// The key is the DbContext type, and the value is a boolean indicating whether migrations
    /// should be applied for that specific context (true to apply, false to skip).
    /// </remarks>
    public IDictionary<string, bool> RunMigrations { get; set; } = new Dictionary<string, bool>();

    /// <summary>
    /// The age beyond which an unreleased EF Core migrations lock is considered abandoned and is reclaimed
    /// before migrations run. Only relevant to SQLite, whose <c>__EFMigrationsLock</c> row persists across
    /// process death (issue #949). Must comfortably exceed the longest expected migration. Defaults to 5 minutes;
    /// set to <see cref="TimeSpan.Zero"/> or a negative value to disable reclamation.
    /// </summary>
    public TimeSpan StaleMigrationLockTimeout { get; set; } = TimeSpan.FromMinutes(5);
}