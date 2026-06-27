using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Models;

public sealed class ActivityAvailabilitySettings
{
    public const string HostDefaultScope = "host-default";

    public string Scope { get; set; } = HostDefaultScope;

    public ActivityAvailabilityManagementMode Mode { get; set; } = ActivityAvailabilityManagementMode.AllExcept;

    public ActivityAvailabilityRuleSet Rules { get; set; } = new();
}
