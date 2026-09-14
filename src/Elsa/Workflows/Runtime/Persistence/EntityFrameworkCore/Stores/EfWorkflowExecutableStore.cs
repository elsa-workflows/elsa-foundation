using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowExecutableStore
{
    public ValueTask SaveAsync(
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default) =>
        SaveBatchAsync([executable], cancellationToken);

    public async ValueTask SaveBatchAsync(
        IReadOnlyList<WorkflowExecutable> executables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables);
        cancellationToken.ThrowIfCancellationRequested();
        if (executables.Select(x => x.Identity.ArtifactId).Distinct(StringComparer.Ordinal).Count() != executables.Count)
            throw new ArgumentException("A workflow executable batch must contain distinct artifact ids.", nameof(executables));
        foreach (var item in executables)
            Validate(item);
        if (executables.Count == 0)
            return;

        var scope = RequireScope();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in executables)
            {
                var artifactId = item.Identity.ArtifactId;
                var id = CreateId(scope, artifactId);
                var artifact = await FindExecutableAsync(scope, artifactId, id, cancellationToken);
                var coordination = await FindCoordinationAsync(scope, artifactId, id, cancellationToken);
                if (artifact is null && coordination is null)
                {
                    var incarnationId = NewIncarnationId();
                    context.WorkflowExecutables.Add(ToEntity(
                        item,
                        scope,
                        id,
                        RuntimeArtifactJson.Serialize(item),
                        incarnationId));
                    context.WorkflowExecutableCoordinations.Add(ToCoordinationEntity(artifactId, scope, id, incarnationId));
                }
                else if (artifact is null || coordination is null)
                {
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
                }
                else
                {
                    _ = Read(artifact, scope, artifactId, id);
                    _ = ReadCoordination(coordination, scope, artifactId, id);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) ||
            EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.", exception);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }
    public async ValueTask<WorkflowExecutable?> FindAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, artifactId);
        var row = await context.WorkflowExecutables
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == id &&
                     x.ScopeKeyHash == Hash(scope) &&
                     x.ScopeKey == Encode(scope) &&
                     x.ArtifactIdHash == Hash(artifactId) &&
                     x.ArtifactId == artifactId,
                cancellationToken);
        return row is null ? null : Read(row, scope, artifactId, id);
    }

    public async ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = RequireScope();
        var cursor = Decode(request.ContinuationToken, scope);
        var scopeHash = Hash(scope);
        var encodedScope = Encode(scope);
        var rows = await context.WorkflowExecutables
            .AsNoTracking()
            .Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == encodedScope &&
                        (cursor == null || string.CompareOrdinal(x.ArtifactIdOrderKey, cursor) > 0))
            .OrderBy(x => x.ArtifactIdOrderKey)
            .Take(request.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var more = rows.Length > request.Limit;
        if (more)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, scope, x.ArtifactId, x.Id)).ToArray();
        var continuation = more ? Encode(rows[^1].ArtifactIdOrderKey, scope) : null;
        return new RuntimeStorePage<WorkflowExecutable>(request, items, continuation);
    }
    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
        return pair is not null && await DeletePairAsync(
            scope,
            artifactId,
            null,
            default,
            pair.Value.Artifact.IncarnationId,
            cancellationToken);
    }
    public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
        string artifactId,
        string leaseId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, leaseId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
            if (pair is null)
                return null;
            var row = pair.Value.Coordination;
            var state = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId));
            var leases = LiveLeases(state, now);
            if (state.Guard is { } guard && guard.ExpiresAt > now)
                return null;
            if (leases.TryGetValue(leaseId, out var existing))
                return new WorkflowExecutableRootWriteLease(artifactId, leaseId, existing.Token);
            var created = new Lease(leaseId, Token(), expiresAt);
            leases[leaseId] = created;
            if (await UpdateCoordination(row, new CoordinationState(leases, null), cancellationToken))
                return new WorkflowExecutableRootWriteLease(artifactId, leaseId, created.Token);
        }

        throw new InvalidOperationException("Runtime coordination changed concurrently.");
    }

    public async ValueTask<bool> RenewRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateTransition(lease.ArtifactId, lease.LeaseId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, lease.ArtifactId, cancellationToken);
        if (pair is null)
            return false;
        var row = pair.Value.Coordination;
        var state = ReadCoordination(row, scope, lease.ArtifactId, CreateId(scope, lease.ArtifactId));
        if (state.Guard is { } guard && guard.ExpiresAt > now ||
            !state.Leases.TryGetValue(lease.LeaseId, out var current) ||
            current.Token != lease.ConcurrencyToken || current.ExpiresAt <= now)
        {
            return false;
        }

        var leases = state.Leases.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        leases[lease.LeaseId] = current with { ExpiresAt = expiresAt };
        return await UpdateCoordination(row, new CoordinationState(leases, state.Guard), cancellationToken);
    }

    public async ValueTask ReleaseRootWriteLeaseAsync(
        WorkflowExecutableRootWriteLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.LeaseId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, lease.ArtifactId, cancellationToken);
        if (pair is null)
            return;
        var row = pair.Value.Coordination;
        var state = ReadCoordination(row, scope, lease.ArtifactId, CreateId(scope, lease.ArtifactId));
        if (!state.Leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.ConcurrencyToken)
            return;
        var leases = state.Leases.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        leases.Remove(lease.LeaseId);
        _ = await UpdateCoordination(row, new CoordinationState(leases, state.Guard), cancellationToken);
    }

    public async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
        string artifactId,
        string operationId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(artifactId, operationId, expiresAt, now);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
            if (pair is null)
                return null;
            var row = pair.Value.Coordination;
            var state = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId));
            var leases = LiveLeases(state, now);
            if (leases.Count != 0)
                return null;
            if (state.Guard is { } existing && existing.ExpiresAt > now)
                return existing.OperationId == operationId
                    ? new WorkflowExecutableDeletionGuard(artifactId, operationId, existing.Token)
                    : null;
            var created = new Guard(operationId, Token(), expiresAt);
            if (await UpdateCoordination(row, new CoordinationState(leases, created), cancellationToken))
                return new WorkflowExecutableDeletionGuard(artifactId, operationId, created.Token);
        }

        throw new InvalidOperationException("Runtime coordination changed concurrently.");
    }

    public async ValueTask<bool> CancelDeletionAsync(
        WorkflowExecutableDeletionGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ConcurrencyToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, guard.ArtifactId, cancellationToken);
        if (pair is null)
            return false;
        var row = pair.Value.Coordination;
        var state = ReadCoordination(row, scope, guard.ArtifactId, CreateId(scope, guard.ArtifactId));
        if (state.Guard is not { } current || current.OperationId != guard.OperationId || current.Token != guard.ConcurrencyToken)
            return false;
        return await UpdateCoordination(row, new CoordinationState(state.Leases, null), cancellationToken);
    }
    public async ValueTask<bool> DeleteAsync(
        WorkflowExecutableDeletionGuard guard,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guard.ConcurrencyToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var pair = await LoadPairAsync(scope, guard.ArtifactId, cancellationToken);
        return pair is not null && await DeletePairAsync(
            scope,
            guard.ArtifactId,
            guard,
            now,
            pair.Value.Artifact.IncarnationId,
            cancellationToken);
    }

    private async Task<bool> DeletePairAsync(
        string scope,
        string artifactId,
        WorkflowExecutableDeletionGuard? guard,
        DateTimeOffset now,
        string expectedIncarnationId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var artifact = await FindExecutableAsync(scope, artifactId, id, cancellationToken);
                var coordination = await FindCoordinationAsync(scope, artifactId, id, cancellationToken);

                if (artifact is null && coordination is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                if (artifact is null || coordination is null)
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");

                if (artifact.IncarnationId != expectedIncarnationId || coordination.IncarnationId != expectedIncarnationId)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                _ = Read(artifact, scope, artifactId, id);
                var state = ReadCoordination(coordination, scope, artifactId, id);

                if (guard is not null && !CanDelete(state, guard, now))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }

                context.RemoveRange(artifact, coordination);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                await RollbackQuietlyAsync(transaction);
                context.ChangeTracker.Clear();
                if (attempt == 31)
                    throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.", exception);
            }
            catch (OperationCanceledException)
            {
                await RollbackQuietlyAsync(transaction);
                context.ChangeTracker.Clear();
                throw;
            }
            catch
            {
                await RollbackQuietlyAsync(transaction);
                context.ChangeTracker.Clear();
                throw;
            }
        }

        throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.");
    }

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the operation's original exception.
        }
    }

    private async Task<WorkflowExecutableEntity?> FindExecutableAsync(
        string scope,
        string artifactId,
        string id,
        CancellationToken cancellationToken) =>
        await context.WorkflowExecutables.SingleOrDefaultAsync(
            x => x.Id == id &&
                 x.ScopeKeyHash == Hash(scope) &&
                 x.ScopeKey == Encode(scope) &&
                 x.ArtifactIdHash == Hash(artifactId) &&
                 x.ArtifactId == artifactId,
            cancellationToken);

    private async Task<WorkflowExecutableCoordinationEntity?> FindCoordinationAsync(
        string scope,
        string artifactId,
        string id,
        CancellationToken cancellationToken) =>
        await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(
            x => x.Id == id &&
                 x.ScopeKeyHash == Hash(scope) &&
                 x.ScopeKey == Encode(scope) &&
                 x.ArtifactIdHash == Hash(artifactId) &&
                 x.ArtifactId == artifactId,
            cancellationToken);
    private async Task<(WorkflowExecutableEntity Artifact, WorkflowExecutableCoordinationEntity Coordination)?> LoadPairAsync(
        string scope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var id = CreateId(scope, artifactId);
        var artifact = await FindExecutableAsync(scope, artifactId, id, cancellationToken);
        var coordination = await FindCoordinationAsync(scope, artifactId, id, cancellationToken);
        if (artifact is null && coordination is null)
            return null;
        if (artifact is null || coordination is null)
            throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
        if (artifact.IncarnationId != coordination.IncarnationId)
            throw new InvalidDataException($"Workflow executable '{id}' has mismatched incarnation identities.");

        _ = Read(artifact, scope, artifactId, id);
        _ = ReadCoordination(coordination, scope, artifactId, id);
        return (artifact, coordination);
    }

    private async Task<bool> UpdateCoordination(WorkflowExecutableCoordinationEntity row, CoordinationState state, CancellationToken ct)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(state);
        row.Revision++;
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static WorkflowExecutableEntity ToEntity(
        WorkflowExecutable executable,
        string scope,
        string id,
        string json,
        string incarnationId) => new()
        {
            Id = id,
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            ArtifactId = executable.Identity.ArtifactId,
            ArtifactIdHash = Hash(executable.Identity.ArtifactId),
            ArtifactIdOrderKey = OrderKey(executable.Identity.ArtifactId),
            ContentJson = json,
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            IncarnationId = incarnationId
        };

    private static WorkflowExecutableCoordinationEntity ToCoordinationEntity(
        string artifactId,
        string scope,
        string id,
        string incarnationId) => new()
        {
            Id = id,
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            ArtifactId = artifactId,
            ArtifactIdHash = Hash(artifactId),
            ContentJson = RuntimeArtifactJson.Serialize(CoordinationState.Empty),
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            Revision = 1,
            IncarnationId = incarnationId
        };

    private static WorkflowExecutable Read(
        WorkflowExecutableEntity row,
        string scope,
        string expected,
        string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != expected || row.ArtifactIdHash != Hash(expected) ||
            row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.ArtifactIdOrderKey != OrderKey(expected) ||
            string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted workflow executable row is corrupt.");
        try
        {
            var x = RuntimeArtifactJson.Deserialize<WorkflowExecutable>(row.ContentJson);
            if (x.Identity.ArtifactId != expected)
                throw new InvalidDataException("The persisted workflow executable identity is corrupt.");
            return x;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentException or NotSupportedException or NullReferenceException)
        {
            throw new InvalidDataException("The persisted workflow executable payload is corrupt.", exception);
        }
    }

    private static CoordinationState ReadCoordination(WorkflowExecutableCoordinationEntity row, string scope, string expected, string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != expected || row.ArtifactIdHash != Hash(expected) ||
            row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0 ||
            string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted workflow executable coordination row is corrupt.");

        try
        {
            using var document = JsonDocument.Parse(row.ContentJson);
            EnsureUniqueProperties(document.RootElement);
            var state = RuntimeArtifactJson.Deserialize<CoordinationState>(row.ContentJson);
            ValidateCoordination(state);
            return state;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentException or NotSupportedException or FormatException or
                                           OverflowException or NullReferenceException)
        {
            throw new InvalidDataException("The persisted workflow executable coordination payload is corrupt.", exception);
        }
    }

    private static void ValidateCoordination(CoordinationState? state)
    {
        if (state?.Leases is null)
            throw new InvalidDataException("The persisted workflow executable coordination leases are missing.");

        var leaseIds = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, lease) in state.Leases)
        {
            if (lease is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(lease.Id) ||
                !StringComparer.Ordinal.Equals(key, lease.Id) || string.IsNullOrWhiteSpace(lease.Token) ||
                lease.ExpiresAt == default || !leaseIds.Add(lease.Id) || !tokens.Add(lease.Token))
            {
                throw new InvalidDataException("The persisted workflow executable coordination contains an invalid lease.");
            }
        }

        if (state.Guard is { } guard &&
            (string.IsNullOrWhiteSpace(guard.OperationId) || string.IsNullOrWhiteSpace(guard.Token) ||
             guard.ExpiresAt == default || !tokens.Add(guard.Token)))
        {
            throw new InvalidDataException("The persisted workflow executable coordination contains an invalid deletion guard.");
        }
    }

    private static void EnsureUniqueProperties(JsonElement element, bool dictionaryKeys = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(dictionaryKeys ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("The persisted workflow executable coordination contains duplicate JSON properties.");
                var isLeaseDictionary = !dictionaryKeys &&
                                        property.Name.Equals("Leases", StringComparison.OrdinalIgnoreCase);
                EnsureUniqueProperties(property.Value, isLeaseDictionary);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureUniqueProperties(item);
        }
    }

    private static bool CanDelete(CoordinationState state, WorkflowExecutableDeletionGuard guard, DateTimeOffset now) =>
        state.Guard is { } current &&
        current.OperationId == guard.OperationId &&
        current.Token == guard.ConcurrencyToken &&
        current.ExpiresAt > now &&
        state.Leases.All(x => x.Value.ExpiresAt <= now);

    private string RequireScope()
    {
        var current = accessContextAccessor.Current;
        if (current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF workflow executable persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private static void Validate(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);
        if (executable.Identity.ArtifactId.Length > RuntimeArtifactEfModule.IdentityMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(executable));
    }

    private static void ValidateTransition(
        string artifact,
        string id,
        DateTimeOffset expiry,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (expiry <= now)
            throw new ArgumentOutOfRangeException(nameof(expiry));
    }
    private static string CreateId(string scope, string artifactId) =>
        Hash($"{scope.Length}:{scope}{artifactId.Length}:{artifactId}");

    private static string NewIncarnationId() => Guid.NewGuid().ToString("N");

    private static string Hash(string x) => EfRelationalIdentity.Hash(x);

    private static string Encode(string x) => EfRelationalIdentity.Encode(x);

    private static string OrderKey(string x) =>
        Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(x, RuntimeArtifactEfModule.IdentityMaximumLength));

    private static string Token() => Guid.NewGuid().ToString("N");

    private static Dictionary<string, Lease> LiveLeases(CoordinationState state, DateTimeOffset now) =>
        state.Leases
            .Where(x => x.Value.ExpiresAt > now)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static string Encode(string x, string scope) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Hash(scope)}:{x}"));

    private static string? Decode(string? x, string scope)
    {
        if (x is null)
            return null;
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(x));
            var prefix = $"{Hash(scope)}:";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
                throw new FormatException();
            return value[prefix.Length..];
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The workflow executable continuation token is invalid.", nameof(x), exception);
        }
    }

    private sealed record Lease(string Id, string Token, DateTimeOffset ExpiresAt);

    private sealed record Guard(string OperationId, string Token, DateTimeOffset ExpiresAt);

    private sealed record CoordinationState(IReadOnlyDictionary<string, Lease> Leases, Guard? Guard)
    {
        public static CoordinationState Empty =>
            new(new Dictionary<string, Lease>(StringComparer.Ordinal), null);
    }
}
