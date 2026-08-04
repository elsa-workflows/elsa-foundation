using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Describes one replay-safe OpenIddict mutation and the document kinds that must commit together
/// (spec 106 T030). The operation identity is derived from the operation name and its fingerprint
/// components via the existing <see cref="OpenIddictGroundworkOperationIdentity"/> hash, so every retry of
/// the same logical request (redeem, revoke, prune, ...) addresses the same mutation receipt. Mirrors
/// <c>GroundworkIdentityAtomicMutation</c>.
/// </summary>
public sealed record OpenIddictGroundworkAtomicMutation
{
    private OpenIddictGroundworkAtomicMutation(
        string operationName,
        OpenIddictGroundworkOperationIdentity operationIdentity,
        DocumentCommitScope commitScope)
    {
        OperationName = operationName;
        OperationIdentity = operationIdentity;
        CommitScope = commitScope;
        MutationReceiptId = OpenIddictGroundworkOperationIdentity.Create(
            "mutation-receipt",
            operationIdentity.Value).Value;
    }

    public string OperationName { get; }
    public OpenIddictGroundworkOperationIdentity OperationIdentity { get; }
    public DocumentCommitScope CommitScope { get; }
    public string MutationReceiptId { get; }

    /// <summary>
    /// Creates one atomic mutation. <paramref name="fingerprintComponents"/> is fed verbatim to
    /// <see cref="OpenIddictGroundworkOperationIdentity.Create"/> alongside <paramref name="operationName"/>;
    /// callers do not hash their own request fingerprint.
    /// </summary>
    public static OpenIddictGroundworkAtomicMutation Create(
        string operationName,
        string?[] fingerprintComponents,
        params string[] documentKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(fingerprintComponents);
        ArgumentNullException.ThrowIfNull(documentKinds);
        if (documentKinds.Contains(OpenIddictGroundworkJson.MutationReceiptDocumentKind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The mutation receipt kind is managed by the atomic mutation executor and must not be declared by the caller.",
                nameof(documentKinds));
        }

        var operationIdentity = OpenIddictGroundworkOperationIdentity.Create(operationName, fingerprintComponents);
        var commitScope = new DocumentCommitScope(documentKinds.Distinct(StringComparer.Ordinal).ToArray());
        return new OpenIddictGroundworkAtomicMutation(operationName, operationIdentity, commitScope);
    }
}
