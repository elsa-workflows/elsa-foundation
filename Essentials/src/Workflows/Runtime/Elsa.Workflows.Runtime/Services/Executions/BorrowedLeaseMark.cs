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
    private const string OwnerAcquiredAtKey = "runtime.ownership.owner-lease-acquired-at";
    private const string OwnerExpiresAtKey = "runtime.ownership.owner-lease-expires-at";

    /// <summary>The mark of a lease borrowed while the run had no owner.</summary>
    public static IReadOnlyDictionary<string, string> ForNoOwner() =>
        new Dictionary<string, string> { [FromKey] = FromNoOwner };

    /// <summary>
    /// The mark of a lease borrowed from an owner. It carries when the owner's lease was acquired and when it
    /// expires, so the lease that is left on the record afterwards expires when the owner's would have, whichever
    /// borrower leaves it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForOwner((DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt) ownersLease) =>
        new Dictionary<string, string>
        {
            [FromKey] = FromAnOwner,
            [OwnerAcquiredAtKey] = Format(ownersLease.AcquiredAt),
            [OwnerExpiresAtKey] = Format(ownersLease.ExpiresAt)
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
    /// The times of the owner's lease that <paramref name="standIn"/> stands in for. A mark that cannot be read
    /// falls back to the stand-in's own times, so a damaged record delays recovery instead of blocking the run.
    /// </summary>
    public static (DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt) ReadOwnersLease(RuntimeExecutionLease standIn) =>
        TryParse(standIn.Metadata, OwnerAcquiredAtKey, out var acquiredAt) &&
        TryParse(standIn.Metadata, OwnerExpiresAtKey, out var expiresAt) &&
        expiresAt > acquiredAt
            ? (acquiredAt, expiresAt)
            : (standIn.AcquiredAt, standIn.ExpiresAt);

    private static bool Is(RuntimeExecutionLease lease, string from) =>
        lease.Metadata.TryGetValue(FromKey, out var value) && StringComparer.Ordinal.Equals(value, from);

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static bool TryParse(IReadOnlyDictionary<string, string> metadata, string key, out DateTimeOffset value)
    {
        value = default;
        return metadata.TryGetValue(key, out var text) &&
            DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }
}
