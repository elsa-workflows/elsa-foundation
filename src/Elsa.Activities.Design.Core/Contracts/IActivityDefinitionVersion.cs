using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    int Version { get; }

    string DefinitionId { get; }
    

    /// <summary>
    /// Registry lookup key. Equals <see cref="ImplementationDescriptor"/>.<c>Kind</c> for this
    /// row; stored separately so the loading handler can resolve the deserialization target
    /// before deserializing the descriptor JSON payload.
    /// </summary>
    string ImplementationKind { get; }

    /// <summary>
    /// Polymorphic descriptor — the concrete shape varies by <see cref="ImplementationKind"/>.
    /// Hydrated by the loading handler from the EF shadow column.
    /// </summary>
    IImplementationDescriptor ImplementationDescriptor { get; }

    IActivityDefinition Definition { get; }

    IEnumerable<InputDefinition> Inputs { get; }

    IEnumerable<OutputDefinition> Outputs { get; }

    IEnumerable<ActivityPortDefinition> Ports { get; }

    ActivityExecutionType ExecutionType { get; }

    /// <summary>
    /// Immutable content hash of this version's projection, computed by
    /// <c>IActivityDefinitionHasher</c> at reconciliation time. Used by the Model X
    /// duplicate-detection path: when a subsequent reconciliation pass observes the same
    /// <c>(DefinitionId, Version)</c>, the incoming candidate's hash is compared to this
    /// stored value. Mismatch signals "the source is broken — same identity, different
    /// content" and the reconciler throws; match → skip or throw per duplicate-handling
    /// configuration.
    /// </summary>
    string? ReconcilliationHash { get; }
}
