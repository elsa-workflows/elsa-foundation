using Elsa.Primitives.Enums;

namespace Elsa.Activities.Design.Reconciliation.Options;

public sealed class ActivityVersionReconcilerOptions
{
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;
}
