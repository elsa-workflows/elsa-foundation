using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Derives bounded, deterministic successor identifiers for the runtime scheduler / outbox / checkpoint id chains
/// (#923). The previous scheme composed a successor id by concatenating the <b>full</b> parent id with a per-hop
/// step (<c>{parentId}:{step}</c>). Because every parent id already embedded its own ancestry, ids grew
/// quadratically with workflow progress — multi-KB after only two activities — which bloated storage, flooded the
/// merged console stream, and overflowed the 128-char <c>by-incident-id</c> projection column (GW-PHYSICAL-037,
/// #922).
///
/// <para>Instead of carrying the whole ancestry in the id, the parent id is folded into a fixed-length
/// <see cref="Fingerprint(string)"/> and the readable per-hop <c>step</c> is appended: <c>{fingerprint}:{step}</c>.
/// The result is bounded by <c>16 + 1 + step.Length</c> regardless of chain depth, so each generation's id length
/// depends only on that hop's step and never on how deep in the workflow the hop is.</para>
///
/// <para><b>Semantics preserved.</b> Derivation is a pure, deterministic function of <c>(parentId, step)</c>: the
/// same logical work item, re-derived along the same path (replay / redrive), always yields the same successor id,
/// so idempotency- and dedupe-by-id-equality keep working. Siblings stay distinct because the step (which carries
/// the activity-execution / node / bookmark discriminator) is appended verbatim; different ancestries stay distinct
/// via the fingerprint. Human-readable ancestry that used to live in the id belongs in structured log properties /
/// metadata, not the identifier — matching the scheduler order key, which already folds the work-item id through a
/// stable hash.</para>
/// </summary>
public static class RuntimeChainId
{
    /// <summary>
    /// Number of hex characters retained from the SHA-256 of the parent id. 16 hex chars = 64 bits, which keeps the
    /// per-hop id short while making an accidental collision within a single execution's id tree negligible.
    /// </summary>
    public const int FingerprintLength = 16;

    /// <summary>
    /// Derives a bounded successor id of the form <c>{Fingerprint(parentId)}:{step}</c>. Deterministic and stable:
    /// identical inputs always produce the same successor, preserving idempotency / dedupe equality.
    /// </summary>
    /// <param name="parentId">The parent chain id to fold into a fixed-length fingerprint.</param>
    /// <param name="step">The readable, per-hop discriminator to append (e.g. <c>invoke:{activityExecutionId}</c>).</param>
    public static string Derive(string parentId, string step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        return $"{Fingerprint(parentId)}:{step}";
    }

    /// <summary>
    /// Returns a fixed-length (<see cref="FingerprintLength"/> hex chars), stable fingerprint of <paramref name="value"/>:
    /// the leading hex characters of its SHA-256 digest. Deterministic across processes and runs.
    /// </summary>
    public static string Fingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest)[..FingerprintLength];
    }
}
