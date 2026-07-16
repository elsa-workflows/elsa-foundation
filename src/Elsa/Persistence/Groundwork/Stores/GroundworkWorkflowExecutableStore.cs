using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed executable artifact store. Retention-root writers and garbage collection
/// coordinate through provider CAS over the artifact envelope, so a root and physical deletion can
/// never both win the same race.
/// </summary>
public sealed class GroundworkWorkflowExecutableStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, boundedStore), IWorkflowExecutableStore
{
    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        var document = new ExecutableDocument(
            ElsaRuntimeStorageManifest.WorkflowExecutableCollection,
            executable,
            new Dictionary<string, RootWriteLeaseDocument>(StringComparer.Ordinal),
            null);
        var (schemaVersion, content) = Serializer.Serialize(DocumentKind, document);

        // Artifacts are immutable. Create-only persistence also prevents a concurrent first retention
        // transition from being overwritten by the old find-then-unconditional-save race.
        await Store.SaveAsync(
            new SaveDocumentRequest(DocumentKind, executable.Identity.ArtifactId, schemaVersion, content, ExpectedVersion: 0),
            cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var result = await DeleteDocumentAsync(artifactId, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, leaseId, expiresAt, now, nameof(leaseId));

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(artifactId, cancellationToken);
            if (loaded is null)
                return null;

            var (envelope, document) = loaded.Value;
            var leases = LiveLeases(document, now);
            if (IsLive(document.DeletionGuard, now))
                return null;

            // A duplicate logical acquire is idempotent but cannot extend the lease without presenting
            // the fencing token through RenewRootWriteLeaseAsync.
            if (leases.TryGetValue(leaseId, out var current))
                return ToLease(artifactId, current);

            var state = new RootWriteLeaseDocument(leaseId, NewFencingToken(), expiresAt);
            leases.Add(leaseId, state);
            var updated = document with { RootWriteLeases = leases, DeletionGuard = null };
            var result = await SaveEnvelopeAsync(artifactId, updated, envelope.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return ToLease(artifactId, state);
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return null;
        }
    }

    public async ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateTransition(lease.ArtifactId, lease.LeaseId, expiresAt, now, nameof(lease.LeaseId));

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(lease.ArtifactId, cancellationToken);
            if (loaded is null)
                return false;

            var (envelope, document) = loaded.Value;
            var leases = LiveLeases(document, now);
            if (!leases.TryGetValue(lease.LeaseId, out var current) || current.FencingToken != lease.ConcurrencyToken)
                return false;
            if (IsLive(document.DeletionGuard, now))
                return false;

            leases[lease.LeaseId] = current with { ExpiresAt = expiresAt };
            var result = await SaveEnvelopeAsync(
                lease.ArtifactId,
                document with { RootWriteLeases = leases, DeletionGuard = null },
                envelope.Version,
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return true;
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return false;
        }
    }

    public async ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.LeaseId);

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(lease.ArtifactId, cancellationToken);
            if (loaded is null)
                return;

            var (envelope, document) = loaded.Value;
            var leases = CopyLeases(document);
            if (!leases.TryGetValue(lease.LeaseId, out var current) || current.FencingToken != lease.ConcurrencyToken)
                return;

            leases.Remove(lease.LeaseId);
            var result = await SaveEnvelopeAsync(
                lease.ArtifactId,
                document with { RootWriteLeases = leases },
                envelope.Version,
                cancellationToken);
            if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
                return;
        }
    }

    public async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, operationId, expiresAt, now, nameof(operationId));

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(artifactId, cancellationToken);
            if (loaded is null)
                return null;

            var (envelope, document) = loaded.Value;
            var leases = LiveLeases(document, now);
            if (leases.Count != 0)
                return null;

            if (IsLive(document.DeletionGuard, now))
            {
                var current = document.DeletionGuard!;
                if (current.OperationId != operationId)
                    return null;
                return ToGuard(artifactId, current);
            }

            var state = new DeletionGuardDocument(operationId, NewFencingToken(), expiresAt);
            var updated = document with { RootWriteLeases = leases, DeletionGuard = state };
            var result = await SaveEnvelopeAsync(artifactId, updated, envelope.Version, cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return ToGuard(artifactId, state);
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return null;
        }
    }

    public async ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(guard.ArtifactId, cancellationToken);
            if (loaded is null)
                return false;

            var (envelope, document) = loaded.Value;
            if (!Matches(document.DeletionGuard, guard))
                return false;

            var result = await SaveEnvelopeAsync(
                guard.ArtifactId,
                document with { DeletionGuard = null },
                envelope.Version,
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return true;
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return false;
        }
    }

    public async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);

        while (true)
        {
            var loaded = await LoadEnvelopeAsync(guard.ArtifactId, cancellationToken);
            if (loaded is null)
                return false;

            var (envelope, document) = loaded.Value;
            if (!Matches(document.DeletionGuard, guard) || !IsLive(document.DeletionGuard, now))
                return false;
            if (LiveLeases(document, now).Count != 0)
                return false;

            var result = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, guard.ArtifactId, ExpectedVersion: envelope.Version),
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Deleted)
                return true;
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return false;
        }
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return await LoadDocumentAsync<ExecutableDocument, WorkflowExecutable>(
            artifactId, document => document.Executable, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default)
    {
        var executables = await QueryDocumentsAsync<ExecutableDocument, WorkflowExecutable>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.WorkflowExecutableCollection,
            document => document.Executable,
            cancellationToken);
        return executables.ToArray();
    }

    private async ValueTask<(DocumentEnvelope Envelope, ExecutableDocument Document)?> LoadEnvelopeAsync(
        string artifactId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, artifactId, cancellationToken);
        return envelope is null ? null : (envelope, Serializer.Deserialize<ExecutableDocument>(envelope));
    }

    private Task<DocumentStoreWriteResult> SaveEnvelopeAsync(
        string artifactId,
        ExecutableDocument document,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(DocumentKind, document);
        return Store.SaveAsync(
            new SaveDocumentRequest(DocumentKind, artifactId, schemaVersion, content, ExpectedVersion: expectedVersion),
            cancellationToken);
    }

    private static Dictionary<string, RootWriteLeaseDocument> CopyLeases(ExecutableDocument document) =>
        new(document.RootWriteLeases ?? new Dictionary<string, RootWriteLeaseDocument>(), StringComparer.Ordinal);

    private static Dictionary<string, RootWriteLeaseDocument> LiveLeases(ExecutableDocument document, DateTimeOffset now) =>
        CopyLeases(document)
            .Where(x => x.Value.ExpiresAt > now)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static bool IsLive(DeletionGuardDocument? guard, DateTimeOffset now) =>
        guard is not null && guard.ExpiresAt > now;

    private static bool Matches(DeletionGuardDocument? state, WorkflowExecutableDeletionGuard guard) =>
        state is not null &&
        state.OperationId == guard.OperationId &&
        state.FencingToken == guard.ConcurrencyToken;

    private static WorkflowExecutableRootWriteLease ToLease(string artifactId, RootWriteLeaseDocument state) =>
        new(artifactId, state.LeaseId, state.FencingToken);

    private static WorkflowExecutableDeletionGuard ToGuard(string artifactId, DeletionGuardDocument state) =>
        new(artifactId, state.OperationId, state.FencingToken);

    private static string NewFencingToken() => Guid.NewGuid().ToString("N");

    private static void ValidateTransition(
        string artifactId,
        string ownerId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string ownerParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId, ownerParameterName);
        if (expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "The retention guard expiry must be later than now.");
    }

    private sealed record ExecutableDocument(
        string Collection,
        WorkflowExecutable Executable,
        Dictionary<string, RootWriteLeaseDocument>? RootWriteLeases,
        DeletionGuardDocument? DeletionGuard);

    private sealed record RootWriteLeaseDocument(string LeaseId, string FencingToken, DateTimeOffset ExpiresAt);

    private sealed record DeletionGuardDocument(string OperationId, string FencingToken, DateTimeOffset ExpiresAt);
}
