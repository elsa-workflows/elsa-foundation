using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Versioned durable parent outcome carried by one scheduler item when a child fault and ordered notifications
/// must be evaluated together. <see cref="Fault"/> must already be normalized and secret-masked before persistence.
/// </summary>
public sealed class RuntimeParentOutcome
{
    public const int CurrentVersion = 1;

    [JsonConstructor]
    public RuntimeParentOutcome(
        int version,
        ActivityFault fault,
        string incidentId,
        IReadOnlyList<ActivityChildNotifiedContext> notifications)
    {
        if (version != CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(version), version, $"Parent outcome version must be {CurrentVersion}.");

        ArgumentNullException.ThrowIfNull(fault);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentNullException.ThrowIfNull(notifications);

        if (notifications.Count == 0)
            throw new ArgumentException("A parent outcome requires at least one child notification.", nameof(notifications));

        if (notifications.Any(notification => notification is null))
            throw new ArgumentException("Parent outcome notifications cannot contain null entries.", nameof(notifications));

        Version = version;
        Fault = fault;
        IncidentId = incidentId;
        Notifications = Array.AsReadOnly(notifications.ToArray());
    }

    public int Version { get; }
    public ActivityFault Fault { get; }
    public string IncidentId { get; }
    public IReadOnlyList<ActivityChildNotifiedContext> Notifications { get; }
}
