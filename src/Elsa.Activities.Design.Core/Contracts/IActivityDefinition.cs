namespace Elsa.Activities.Design.Core.Contracts
{
    /// <summary>
    /// Read contract for the catalog parent — identity + creation provenance + display.
    /// Under the Model X reconciliation policy (Unit C 2026-05-28; pending 2026-06-01
    /// architecture review), reconciliation is one-shot at creation time and all provenance
    /// is immutable; there is no operational sibling, no per-pass mutating fields, and no
    /// stale / removed tracking. <see cref="IActivityDefinitionVersion.ProvisioningHash"/> on
    /// the version carries the immutable content hash used by the duplicate-detection path.
    /// </summary>
    public interface IActivityDefinition
    {
        string Id { get; }

        /// <summary>
        /// Stable logical identity for the activity type. Survives CLR renames, source-side
        /// repackaging, and provider migrations. Immutable once a row is persisted.
        /// </summary>
        string ActivityTypeKey { get; }

        /// <summary>
        /// Free-form string identifying the provenance source that produced this row (e.g.
        /// "Json", "ClrDiscovery", "Workflow"). Owned by the source module that produced the
        /// row; core does not enumerate the legal values. Immutable; part of the natural
        /// uniqueness key <c>(SourceKind, SourceId, ActivityTypeKey)</c>.
        /// </summary>
        string SourceKind { get; }

        /// <summary>
        /// Source-side asset identity (e.g. assembly name for JSON, workflow definition id for
        /// workflow source). Immutable; part of the natural uniqueness key.
        /// </summary>
        string SourceId { get; }

        /// <summary>
        /// Timestamp at which this row was first provisioned. Immutable.
        /// </summary>
        DateTimeOffset ProvisionedAt { get; }

        /// <summary>
        /// Identity (user, machine, or system) that produced this row. Immutable.
        /// </summary>
        string? ProvisionedBy { get; }

        /// <summary>
        /// The category of the activity type. Mutable — picker grouping.
        /// </summary>
        string Category { get; }

        /// <summary>
        /// The display name of the activity type.
        /// </summary>
        string? DisplayName { get; }

        /// <summary>
        /// The description of the activity type.
        /// </summary>
        string? Description { get; }
    }
}
