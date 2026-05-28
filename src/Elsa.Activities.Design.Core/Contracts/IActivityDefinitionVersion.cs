using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    int Version { get; }

    string DefinitionId { get; }

    /// <summary>
    /// Denormalised from the parent <see cref="IActivityDefinition.ActivityTypeKey"/>. Set on
    /// insert; never updated. Lets consumers join by (ActivityTypeKey, Version) without a
    /// round-trip to the parent table.
    /// </summary>
    string ActivityTypeKey { get; }

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
}
