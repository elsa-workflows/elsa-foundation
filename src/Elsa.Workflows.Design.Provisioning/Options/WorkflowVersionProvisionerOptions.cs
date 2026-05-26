using Elsa.Primitives.Enums;

namespace Elsa.Workflows.Design.Provisioning.Options;

public sealed class WorkflowVersionProvisionerOptions
{
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;
}
