using Elsa.Activities.Design.Core.Contracts;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Events;

/// <summary>Collects additive source restrictions for one owner-resolved activity input.</summary>
/// <remarks>Published inline (Sequential); the caller reads the contributions after every provider completes.</remarks>
public sealed class InputConstraintsCollecting : IEvent
{
    public InputConstraintsCollecting(IActivityDefinitionVersion activityVersion, string inputKey)
    {
        ArgumentNullException.ThrowIfNull(activityVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ActivityVersion = activityVersion;
        InputKey = inputKey;
    }

    public IActivityDefinitionVersion ActivityVersion { get; }
    public string InputKey { get; }
    public ICollection<ActivityDataInputConstraint> Constraints { get; } = [];
}
