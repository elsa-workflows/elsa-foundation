using Elsa.Primitives.Enums;

namespace Elsa.Workflows.Design.Reconciliation.Options;

public sealed class WorkflowVersionReconcilerOptions
{
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;
}
