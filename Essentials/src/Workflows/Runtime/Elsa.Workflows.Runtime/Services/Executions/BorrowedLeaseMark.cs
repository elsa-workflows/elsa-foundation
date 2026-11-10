using System.Globalization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// What a borrowed lease says about itself in its metadata (see
/// <see cref="RuntimeExecutionOwnershipService.BorrowAsync"/>). It is stored with the lease, so any node reads it from
/// the ownership record. A drain's lease carries none of it.
/// </summary>
internal static class BorrowedLeaseMark
{
    private const string FromKey = "runtime.ownership.borrowed-from";
    private const string FromNoOwner = "no-owner";
    private const string FromAnOwner = "owner";
    private const string ByKey = "runtime.ownership.borrowed-by";
    private const string OwnerLeaseIdKey = "runtime.ownership.owner-lease-id";
    private const string OwnerAcquiredAtKey = "runtime.ownership.owner-lease-acquired-at";
    private const string OwnerExpiresAtKey = "runtime.ownership.owner-lease-expires-at";
    private const string OwnerHeartbeatIdKey = "runtime.ownership.owner-heartbeat-id";
    private const string OwnerHeartbeatOwnerIdKey = "runtime.ownership.owner-heartbeat-owner-id";
    private const string OwnerHeartbeatLeaseIdKey = "runtime.ownership.owner-heartbeat-lease-id";
    private const string OwnerHeartbeatRecordedAtKey = "runtime.ownership.owner-heartbeat-recorded-at";

    /// <summary>The mark of a lease borrowed while the run had no owner.</summary>
    public static IReadOnlyDictionary<string, string> ForNoOwner() =>
        new Dictionary<string, string> { [FromKey] = FromNoOwner };

    /// <summary>
    /// The mark of a lease borrowed from an owner. It carries everything <see cref="ReadOwner"/> needs to give the
    /// owner's lease and heartbeat back, so a later borrower can do that when this one does not.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForOwner(string borrowerId, RuntimeExecutionLease ownersLease, RuntimeHeartbeat? ownersHeartbeat)
    {
        var mark = new Dictionary<string, string>
        {
            [FromKey] = FromAnOwner,
            [ByKey] = borrowerId,
            [OwnerLeaseIdKey] = ownersLease.LeaseId,
            [OwnerAcquiredAtKey] = Format(ownersLease.AcquiredAt),
            [OwnerExpiresAtKey] = Format(ownersLease.ExpiresAt)
        };
        if (ownersHeartbeat is null)
            return mark;

        mark[OwnerHeartbeatIdKey] = ownersHeartbeat.HeartbeatId;
        mark[OwnerHeartbeatOwnerIdKey] = ownersHeartbeat.OwnerId;
        mark[OwnerHeartbeatRecordedAtKey] = Format(ownersHeartbeat.RecordedAt);
        if (ownersHeartbeat.LeaseId is { } heartbeatLeaseId)
            mark[OwnerHeartbeatLeaseIdKey] = heartbeatLeaseId;
        return mark;
    }

    /// <summary>
    /// Whether <paramref name="lease"/> was borrowed while the run had no owner. Such a lease records no drain,
    /// whether its borrower is still running or stopped without releasing.
    /// </summary>
    public static bool IsFromNoOwner(RuntimeExecutionLease lease) => Is(lease, FromNoOwner);

    /// <summary>
    /// Whether <paramref name="lease"/> was borrowed from an owner and so stands in for that owner until it is
    /// returned: the run is not quiescent, although the lease's holder is not draining it.
    /// </summary>
    public static bool StandsInForAnOwner(RuntimeExecutionLease lease) => Is(lease, FromAnOwner);

    /// <summary>The owner's lease and heartbeat that <paramref name="standIn"/> was borrowed from.</summary>
    public static (RuntimeExecutionLease Lease, RuntimeHeartbeat? Heartbeat) ReadOwner(RuntimeExecutionLease standIn)
    {
        var metadata = standIn.Metadata;
        var lease = new RuntimeExecutionLease(
            leaseId: Required(metadata, OwnerLeaseIdKey),
            workflowExecutionId: standIn.WorkflowExecutionId,
            ownerId: standIn.OwnerId,
            acquiredAt: Parse(Required(metadata, OwnerAcquiredAtKey)),
            expiresAt: Parse(Required(metadata, OwnerExpiresAtKey)),
            fencingToken: standIn.FencingToken);
        var heartbeat = metadata.TryGetValue(OwnerHeartbeatIdKey, out var heartbeatId)
            ? new RuntimeHeartbeat(
                heartbeatId,
                standIn.WorkflowExecutionId,
                Required(metadata, OwnerHeartbeatOwnerIdKey),
                metadata.GetValueOrDefault(OwnerHeartbeatLeaseIdKey),
                Parse(Required(metadata, OwnerHeartbeatRecordedAtKey)))
            : null;
        return (lease, heartbeat);
    }

    private static bool Is(RuntimeExecutionLease lease, string from) =>
        lease.Metadata.TryGetValue(FromKey, out var value) && StringComparer.Ordinal.Equals(value, from);

    private static string Required(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"A lease that stands in for an owner does not carry '{key}'.");

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
