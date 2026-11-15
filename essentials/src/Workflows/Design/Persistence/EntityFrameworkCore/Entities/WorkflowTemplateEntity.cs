using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;

internal sealed class WorkflowTemplateEntity : TenantEntity
{
    public string Revision { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string ContentJson { get; set; } = "";
}
