namespace Elsa.Activities.Design.Core.Contracts
{
    /// <summary>
    /// Read contract for the catalog parent — the visual shell: identity + display fields.
    /// Source provenance and implementation details live on <see cref="IActivityDefinitionVersion"/>;
    /// the Definition itself is implementation-agnostic.
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
