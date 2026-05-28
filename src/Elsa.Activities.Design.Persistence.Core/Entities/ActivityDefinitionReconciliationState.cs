using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities
{
    /// <summary>
    /// Operational layer of the catalog — 1:0..1 sibling of <see cref="ActivityDefinition"/>.
    /// All fields are mutable by design: the reconciler rewrites them each pass.
    /// </summary>
    public sealed class ActivityDefinitionReconciliationState : TenantEntity, IActivityDefinitionReconciliationState
    {
        public string ActivityDefinitionId { get; set; } = null!;

        public string? SourceVersion { get; set; }

        public string? ProvisioningHash { get; set; }

        public DateTimeOffset LastSeenAt { get; set; }

        public DateTimeOffset LastProvisionedAt { get; set; }

        public string? LastProvisionedBy { get; set; }

        public bool IsStale { get; set; }

        public DateTimeOffset? RemovedAt { get; set; }
    }
}
