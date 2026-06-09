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
}