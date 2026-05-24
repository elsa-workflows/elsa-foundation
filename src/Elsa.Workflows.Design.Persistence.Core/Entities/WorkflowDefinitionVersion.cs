using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionVersion(string definitionId, int version, DateTimeOffset createdAt, string? stateSource = null)
    : Entity, IWorkflowDefinitionVersion
{
    /// <summary>
    /// The version number of this workflow definition version
    /// </summary>
    [Immutable]
    public int Version { get; } = version;

    /// <summary>
    /// The id of the workflow definition
    /// </summary>
    [Immutable]
    public string DefinitionId { get; }= definitionId;

    /// <summary>
    /// Navigation property to the <see cref="WorkflowDefinition"/> entity
    /// </summary>
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    [NotMapped]
    public WorkflowDefinitionState? State { get; set; }

    /// <summary>
    /// Shadow property that contains the serialized immutable state of this version
    /// </summary>
    [Immutable]
    public string? StateSource { get; set; } = stateSource;

    /// <summary>
    /// Timestamp when this entity was created
    /// </summary>
    [Immutable]
    public DateTimeOffset CreatedAt { get; } = createdAt;

    IWorkflowDefinitionState IWorkflowDefinitionVersion.State => State ?? throw new ArgumentNullException(nameof(State));

    IWorkflowDefinition IWorkflowDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
