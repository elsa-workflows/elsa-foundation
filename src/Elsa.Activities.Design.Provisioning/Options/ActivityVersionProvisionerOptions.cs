using Elsa.Primitives.Enums;

namespace Elsa.Activities.Design.Provisioning.Options;

public sealed class ActivityVersionProvisionerOptions
{
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;
}
