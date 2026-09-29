using System.ComponentModel.DataAnnotations;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>One stored note.</summary>
public sealed partial class NoteRecord
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The persisted-schema version this row was written in: the family's write version at the time. It is a concurrency
    /// token, so a save that restamps a row compares the stamp it read: a row another host restamped since is not overwritten.
    /// </summary>
    [ConcurrencyCheck]
    public string SchemaVersion { get; set; } = null!;
}
