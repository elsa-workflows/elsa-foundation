using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Internal;

/// <summary>
/// Explicitly permits operator scheduling for Sequence's authored child relation. This evidence is emitted by the
/// owning activity module at publish time; Runtime never grants equivalent permission from slot topology alone.
/// </summary>
internal sealed class SequenceOperatorActivitySchedulingCapabilityProvider : IOperatorActivitySchedulingCapabilityProvider
{
    public const string PolicyKey = "elsa.sequence.operator-schedule";
    public const int SchemaVersion = 1;

    public bool TryDescribe(
        OperatorActivitySchedulingCapabilityCompilationContext context,
        out OperatorActivitySchedulingCapability? capability)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!StringComparer.Ordinal.Equals(context.ParentActivityType, typeof(SequenceActivity).FullName) ||
            !StringComparer.Ordinal.Equals(context.ChildSlotName, SequenceActivity.ActivitiesSlotName))
        {
            capability = null;
            return false;
        }

        capability = new OperatorActivitySchedulingCapability(
            PolicyKey,
            SchemaVersion,
            JsonSerializer.SerializeToElement(new { }));
        return true;
    }
}
