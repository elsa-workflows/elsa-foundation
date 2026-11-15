using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Entities;

/// <summary>Provider-neutral persisted representation of the availability policy.</summary>
public sealed class ActivityAvailabilitySettingsRecord : Entity
{
    public string Scope { get; set; } = ActivityAvailabilitySettings.HostDefaultScope;
    public ActivityAvailabilityManagementMode Mode { get; set; }
    public ActivityAvailabilityRuleSet Rules { get; set; } = new();
}
