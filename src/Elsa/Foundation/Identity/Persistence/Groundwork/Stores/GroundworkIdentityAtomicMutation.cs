using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Describes one replay-safe identity mutation and the document kinds that must commit together.
/// The operation identity is derived from the operation name and canonical request fingerprint so
/// every retry of the same logical request addresses the same mutation receipt.
/// </summary>
public sealed record GroundworkIdentityAtomicMutation
{
    private GroundworkIdentityAtomicMutation(
        string operationName,
        IdentityRequestFingerprint requestFingerprint,
        DocumentCommitScope commitScope)
    {
        OperationName = operationName;
        RequestFingerprint = requestFingerprint;
        CommitScope = commitScope;
        OperationId = IdentityDocumentId.From("atomic-mutation", operationName, requestFingerprint.Value);
        MutationReceiptId = IdentityDocumentId.From("mutation-receipt", OperationId);
    }

    public string OperationName { get; }
    public string OperationId { get; }
    public IdentityRequestFingerprint RequestFingerprint { get; }
    public DocumentCommitScope CommitScope { get; }
    public string MutationReceiptId { get; }

    public static GroundworkIdentityAtomicMutation Create(
        string operationName,
        IdentityRequestFingerprint requestFingerprint,
        params string[] documentKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        if (string.IsNullOrWhiteSpace(requestFingerprint.Value))
            throw new ArgumentException("An identity atomic mutation requires a request fingerprint.", nameof(requestFingerprint));
        ArgumentNullException.ThrowIfNull(documentKinds);
        if (documentKinds.Contains(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The mutation receipt kind is managed by the atomic mutation executor and must not be declared by the caller.",
                nameof(documentKinds));
        }

        var commitScope = new DocumentCommitScope(
            documentKinds.Distinct(StringComparer.Ordinal).ToArray());
        return new GroundworkIdentityAtomicMutation(operationName, requestFingerprint, commitScope);
    }
}
