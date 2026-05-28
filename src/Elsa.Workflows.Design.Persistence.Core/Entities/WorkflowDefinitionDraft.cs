using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionDraft : TenantEntity, IWorkflowDefinitionDraft
{
    /// <summary>
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    [NotMapped]
    public WorkflowDefinitionState State { get; set; } = default!;

    /// <summary>
    /// Shadow property that contains the serialized state of this draft
    /// </summary>
    public string? StateSource { get; set; }
}
