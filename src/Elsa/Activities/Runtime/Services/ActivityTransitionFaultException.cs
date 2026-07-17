using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Runtime incident carrier for an activity-authored fault transition. The durable activity fault is
/// persisted separately as normalized data; this exception exists only so the existing incident path
/// can retain its blocking-incident and parent-fault propagation behavior.
/// </summary>
internal sealed class ActivityTransitionFaultException(ActivityFault fault) : Exception(fault.Message)
{
    public ActivityFault Fault { get; } = fault;
}
