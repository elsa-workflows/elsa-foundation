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
    private const string FromKey = "runtime.ownership.borrowedFrom";
    private const string FromNoOwner = "noOwner";
    private const string FromAnOwner = "owner";
    private const string OwnerLeaseExpiresAtKey = "runtime.ownership.ownerLeaseExpiresAt";

    /// <summary>The mark of a lease borrowed while the run had no owner.</summary>
    public static IReadOnlyDictionary<string, string> ForNoOwner() =>
        new Dictionary<string, string> { [FromKey] = FromNoOwner };

    /// <summary>
    /// The mark of a lease borrowed from an owner. It carries when the owner's lease expires, so that a later
    /// borrower that finds this lease can leave the same expiry this borrower would have left.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForOwner(DateTimeOffset ownersLeaseExpiresAt) =>
        new Dictionary<string, string>
        {
            [FromKey] = FromAnOwner,
            [OwnerLeaseExpiresAtKey] = ownersLeaseExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// Whether <paramref name="lease"/> was borrowed while the run had no owner. Such a lease records no drain,
    /// whether its borrower is still running or stopped without releasing.
    /// </summary>
    public static bool IsFromNoOwner(RuntimeExecutionLease lease) => Is(lease, FromNoOwner);

    /// <summary>
    /// Whether <paramref name="lease"/> was borrowed from an owner and so stands in for that owner: the run is not
    /// quiescent, although the lease's holder is not draining it.
    /// </summary>
    public static bool StandsInForAnOwner(RuntimeExecutionLease lease) => Is(lease, FromAnOwner);

    /// <summary>
    /// When the lease of the owner that <paramref name="standIn"/> stands in for expires. A mark that cannot be read
    /// falls back to the stand-in's own expiry, so a damaged record delays recovery instead of blocking the run.
    /// </summary>
    public static DateTimeOffset ReadOwnersLeaseExpiry(RuntimeExecutionLease standIn) =>
        standIn.Metadata.TryGetValue(OwnerLeaseExpiresAtKey, out var text) &&
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            ? expiresAt
            : standIn.ExpiresAt;

    private static bool Is(RuntimeExecutionLease lease, string from) =>
        lease.Metadata.TryGetValue(FromKey, out var value) && StringComparer.Ordinal.Equals(value, from);
}
