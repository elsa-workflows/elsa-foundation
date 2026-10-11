using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Primitives.Entities;

/// <summary>
/// Represents an entity.
/// </summary>
public abstract class Entity
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long RowNumber { get; set; }

    /// <summary>
    /// Gets or sets the ID of this entity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp at which this row first appeared in the database.
    /// Persistence implementations own timestamp assignment and any write-once enforcement;
    /// this provider-neutral model does not enforce either behavior itself.
    /// External / source-side timestamps belong in their own field, not here.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp at which this row was last persisted.
    /// Timestamp assignment is owned by the persistence implementation.
    /// On rows that are supposed to be immutable (e.g. Version entities), a value greater than
    /// <see cref="CreatedAt"/> is a forensic signal that something bypassed the immutability guard.
    /// </summary>
    public DateTimeOffset LastModifiedAt { get; set; }
}
