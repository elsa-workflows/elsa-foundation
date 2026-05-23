using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionDraft : Entity, IWorkflowDefinitionDraft
{
    /// <summary>
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    public WorkflowDefinitionState? State { get; set; }

    /// <summary>
    /// Shadow property that contains the serialized state of this draft
    /// </summary>
    public string? StateSource { get; set; }

    /// <summary>
    /// UTC timestamp when this draft was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// UTC timestamp when this draft was last modified
    /// </summary>
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.Now;

    IWorkflowDefinitionState IWorkflowDefinitionDraft.State => State ?? throw new ArgumentNullException(nameof(State));
}
