using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities
{
    /// <summary>
    /// A definition of an activity type. Identity layer of the catalog — pairs immutable
    /// logical identity (<see cref="ActivityTypeKey"/>) with immutable creation provenance.
    /// Operational reconciliation state (LastSeenAt, hashes, removal) lives on the sibling
    /// <c>ActivityDefinitionReconciliationState</c>.
    /// </summary>
    public sealed class ActivityDefinition : TenantEntity, IActivityDefinition
    {
        /// <summary>
        /// Stable logical identity. Immutable.
        /// </summary>
        [Immutable]
        public string ActivityTypeKey { get; set; } = null!;

        /// <summary>
        /// Provenance source identifier — free-form string owned by the source module
        /// (e.g. "Json", "ClrDiscovery", "Workflow"). Immutable.
        /// </summary>
        [Immutable]
        public string SourceKind { get; set; } = null!;

        /// <summary>
        /// Source-side asset identity. Immutable.
        /// </summary>
        [Immutable]
        public string SourceId { get; set; } = null!;

        /// <summary>
        /// First-provisioning timestamp. Immutable.
        /// </summary>
        [Immutable]
        public DateTimeOffset ProvisionedAt { get; set; }

        /// <summary>
        /// Identity that produced this row. Immutable.
        /// </summary>
        [Immutable]
        public string? ProvisionedBy { get; set; }

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
