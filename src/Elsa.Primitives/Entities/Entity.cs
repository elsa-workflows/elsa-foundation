using Elsa.Primitives.Attributes;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Primitives.Entities
{
    /// <summary>
    /// Represents an entity.
    /// </summary>
    public abstract class Entity
    {
        [Immutable]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long RowNumber { get; set; }

        /// <summary>
        /// Gets or sets the ID of this entity.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ID of the tenant that own this entity.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// Gets or sets the timestamp at which this row first appeared in the database.
        /// Auto-stamped by <c>ElsaDbContextBase</c> on insert. Treated as immutable post-save.
        /// External / source-side timestamps belong in their own field, not here.
        /// </summary>
        [Immutable]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the timestamp at which this row was last persisted.
        /// Auto-stamped by <c>ElsaDbContextBase</c> on insert and update.
        /// On rows that are supposed to be immutable (e.g. Version entities), a value greater than
        /// <see cref="CreatedAt"/> is a forensic signal that something bypassed the immutability guard.
        /// </summary>
        public DateTimeOffset LastModifiedAt { get; set; }
    }
}
