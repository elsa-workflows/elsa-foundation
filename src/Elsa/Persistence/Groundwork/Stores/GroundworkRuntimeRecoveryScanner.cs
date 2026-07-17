using System.Globalization;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Selects runtime recovery candidates through finite, provider-native liveness routes.
/// Each route applies its predicate, deterministic ordering, and requested limit before materialization.
/// </summary>
public sealed class GroundworkRuntimeRecoveryScanner(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(
        store,
        serializer,
        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
        boundedStore),
        IRuntimeRecoveryScanner
{
    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var documents = new Dictionary<string, DocumentEnvelope>(StringComparer.Ordinal);
        foreach (var route in Routes(request))
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    DocumentKind,
                    route.Identity,
                    route.Clauses,
                    route.Order,
                    take: request.Limit),
                cancellationToken);

            foreach (var envelope in result.Documents)
            {
                if (!documents.TryGetValue(envelope.Id, out var existing) || envelope.Version > existing.Version)
                    documents[envelope.Id] = envelope;
            }
        }

        var states = documents.Values
            .Select(envelope => Serializer
                .Deserialize<GroundworkExecutionLivenessStateStore.ExecutionLivenessStateDocument>(envelope)
                .State)
            .ToArray();

        return RuntimeRecoveryCandidateSelector.Select(states, request);
    }

    private static IReadOnlyCollection<RecoveryRoute> Routes(RuntimeRecoveryScanRequest request)
    {
        var detected = ((int)RuntimeInterruptionStatus.Detected).ToString(CultureInfo.InvariantCulture);
        var now = Stamp(request.Now);
        var leaseAcquiredBefore = Stamp(request.Now.Subtract(request.LeaseTimeout));
        var heartbeatRecordedBefore = Stamp(request.Now.Subtract(request.HeartbeatTimeout));

        if (request.OwnerId is not { } ownerId)
        {
            return
            [
                Route(
                    ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
                    [
                        Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected)
                    ],
                    ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                    ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
                Route(
                    ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
                    [
                        Due(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, now)
                    ],
                    ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField),
                Route(
                    ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
                    [
                        Due(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField, leaseAcquiredBefore)
                    ],
                    ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField),
                Route(
                    ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery,
                    [
                        Due(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField, heartbeatRecordedBefore)
                    ],
                    ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField)
            ];
        }

        return
        [
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByLeaseOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, ownerId)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByHeartbeatOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField, ownerId)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField, bool.FalseString)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, ownerId),
                    Due(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, now)
                ],
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, ownerId),
                    Due(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField, leaseAcquiredBefore)
                ],
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField),
            Route(
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField, ownerId),
                    Due(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField, heartbeatRecordedBefore)
                ],
                ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField)
        ];
    }

    private static RecoveryRoute Route(
        string identity,
        IReadOnlyList<DocumentQueryClause> clauses,
        params string[] order) =>
        new(identity, clauses, order.Select(path => new DocumentQueryOrder(path)).ToArray());

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause Due(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(path, value));

    private static string Stamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private sealed record RecoveryRoute(
        string Identity,
        IReadOnlyList<DocumentQueryClause> Clauses,
        IReadOnlyList<DocumentQueryOrder> Order);
}
