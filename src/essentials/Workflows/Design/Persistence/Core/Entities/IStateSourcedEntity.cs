using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// A design persistence entity that carries its <see cref="WorkflowDefinitionState"/> both as a
/// deserialized <see cref="State"/> object and as its serialized <see cref="StateSource"/> string.
/// Implemented by both <see cref="WorkflowDefinitionDraft"/> and <see cref="WorkflowDefinitionVersion"/>
/// so a single pair of EF Core entity-loading/saving handler bases can (de)serialize the state for
/// both, instead of the near-identical per-entity handlers this replaces (issue #417 item 5).
/// </summary>
public interface IStateSourcedEntity
{
    /// <summary>The deserialized <see cref="StateSource"/>.</summary>
    WorkflowDefinitionState State { get; set; }

    /// <summary>The serialized state of this entity.</summary>
    string? StateSource { get; set; }
}
