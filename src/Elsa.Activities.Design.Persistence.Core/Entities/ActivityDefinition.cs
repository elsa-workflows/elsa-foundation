using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities
{
    /// <summary>
    /// A definition of an activity type. Identity layer of the catalog — pairs immutable
    /// logical identity (<see cref="ActivityTypeKey"/>) with immutable creation provenance.
    /// Under the Model X reconciliation policy (Unit C 2026-05-28; pending 2026-06-01 review),
    /// there is no operational sibling; reconciliation is one-shot at creation time and the
    /// immutable <c>ActivityDefinitionVersion.ProvisioningHash</c> carries the content hash
    /// used by the duplicate-detection path.
    /// </summary>
    public sealed class ActivityDefinition : TenantEntity, IActivityDefinition
    {
        /// <summary>
        /// Stable logical identity. Immutable.
        /// </summary>
        [Immutable]
        public string ActivityTypeKey { get; init; } = null!;

        /// <summary>
        /// Provenance source identifier — free-form string owned by the source module
        /// (e.g. "Json", "ClrDiscovery", "Workflow"). Immutable.
        /// </summary>
        [Immutable]
        public string SourceKind { get; init; } = null!;

        /// <summary>
        /// Source-side asset identity. Immutable.
        /// </summary>
        [Immutable]
        public string SourceId { get; init; } = null!;

        /// <summary>
        /// First-provisioning timestamp. Immutable.
        /// </summary>
        [Immutable]
        public DateTimeOffset ReconciledAt { get; init; }

        /// <summary>
        /// Identity that produced this row. Immutable.
        /// </summary>
        [Immutable]
        public string? ReconciledBy { get; init; }

        /// <summary>
        /// The category of the activity type.
        /// </summary>
        public string Category { get; set; } = null!;

        /// <summary>
        /// The display name of the activity type.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// The description of the activity type.
        /// </summary>
        public string? Description { get; set; }
    }
}
