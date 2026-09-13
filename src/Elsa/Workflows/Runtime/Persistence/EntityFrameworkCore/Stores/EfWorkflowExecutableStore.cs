using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowExecutableStore
{
    public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => SaveBatchAsync([executable], cancellationToken);
    public async ValueTask SaveBatchAsync(IReadOnlyList<WorkflowExecutable> executables, CancellationToken cancellationToken = default)
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
                var artifact = await context.WorkflowExecutables.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, cancellationToken);
                var coordination = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, cancellationToken);
                if (artifact is null && coordination is null)
                {
                    var json = RuntimeArtifactJson.Serialize(item);
                    context.WorkflowExecutables.Add(ToEntity(item, scope, id, json));
                    context.WorkflowExecutableCoordinations.Add(ToCoordinationEntity(artifactId, scope, id));
                }
                else if (artifact is null || coordination is null)
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
                else
                { _ = Read(artifact, scope, artifactId, id); _ = ReadCoordination(coordination, scope, artifactId, id); }
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception)) { context.ChangeTracker.Clear(); throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.", exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    { ArgumentException.ThrowIfNullOrWhiteSpace(artifactId); cancellationToken.ThrowIfCancellationRequested(); var scope = RequireScope(); var id = CreateId(scope, artifactId); var row = await context.WorkflowExecutables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, cancellationToken); return row is null ? null : Read(row, scope, artifactId, id); }
    public async ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(request); var scope = RequireScope(); var cursor = Decode(request.ContinuationToken, scope); var rows = await context.WorkflowExecutables.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && (cursor == null || string.CompareOrdinal(x.ArtifactIdOrderKey, cursor) > 0)).OrderBy(x => x.ArtifactIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken); var more = rows.Length > request.Limit; if (more) rows = rows[..request.Limit]; var items = rows.Select(x => Read(x, scope, x.ArtifactId, x.Id)).ToArray(); return new RuntimeStorePage<WorkflowExecutable>(request, items, more ? Encode(rows[^1].ArtifactIdOrderKey, scope) : null); }
    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) { ArgumentException.ThrowIfNullOrWhiteSpace(artifactId); var scope = RequireScope(); var id = CreateId(scope, artifactId); var a = await context.WorkflowExecutables.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, cancellationToken); var c = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, cancellationToken); if (a is null && c is null) return false; if (a is null || c is null) throw new InvalidDataException("Workflow executable storage is incomplete."); _ = Read(a, scope, artifactId, id); _ = ReadCoordination(c, scope, artifactId, id); context.RemoveRange(a, c); await context.SaveChangesAsync(cancellationToken); return true; }
    public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) { ValidateTransition(artifactId, leaseId, expiresAt, now); var scope = RequireScope(); for (var i = 0; i < 16; i++) { var row = await LoadCoordination(scope, artifactId, cancellationToken); if (row is null) return null; var s = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId)); var leases = s.Leases.Where(x => x.Value.ExpiresAt > now).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); if (s.Guard is { } g && g.ExpiresAt > now) return null; if (leases.TryGetValue(leaseId, out var existing)) return new(artifactId, leaseId, existing.Token); leases[leaseId] = new(leaseId, Token(), expiresAt); if (await UpdateCoordination(row, new(leases, null), cancellationToken)) return new(artifactId, leaseId, leases[leaseId].Token); } throw new InvalidOperationException("Runtime coordination changed concurrently."); }
    public async ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(lease); ValidateTransition(lease.ArtifactId, lease.LeaseId, expiresAt, now); var scope = RequireScope(); var row = await LoadCoordination(scope, lease.ArtifactId, cancellationToken); if (row is null) return false; var s = ReadCoordination(row, scope, lease.ArtifactId, CreateId(scope, lease.ArtifactId)); if (s.Guard is { } g && g.ExpiresAt > now || !s.Leases.TryGetValue(lease.LeaseId, out var current) || current.Token != lease.ConcurrencyToken || current.ExpiresAt <= now) return false; var leases = s.Leases.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); leases[lease.LeaseId] = current with { ExpiresAt = expiresAt }; return await UpdateCoordination(row, new(leases, s.Guard), cancellationToken); }
    public async ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(lease); var scope = RequireScope(); var row = await LoadCoordination(scope, lease.ArtifactId, cancellationToken); if (row is null) return; var s = ReadCoordination(row, scope, lease.ArtifactId, CreateId(scope, lease.ArtifactId)); if (!s.Leases.TryGetValue(lease.LeaseId, out var c) || c.Token != lease.ConcurrencyToken) return; var l = s.Leases.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); l.Remove(lease.LeaseId); _ = await UpdateCoordination(row, new(l, s.Guard), cancellationToken); }
    public async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) { ValidateTransition(artifactId, operationId, expiresAt, now); var scope = RequireScope(); for (var i = 0; i < 16; i++) { var row = await LoadCoordination(scope, artifactId, cancellationToken); if (row is null) return null; var s = ReadCoordination(row, scope, artifactId, CreateId(scope, artifactId)); var leases = s.Leases.Where(x => x.Value.ExpiresAt > now).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); if (leases.Count != 0) return null; if (s.Guard is { } old && old.ExpiresAt > now) return old.OperationId == operationId ? new(artifactId, operationId, old.Token) : null; var guard = new Guard(operationId, Token(), expiresAt); if (await UpdateCoordination(row, new(leases, guard), cancellationToken)) return new(artifactId, operationId, guard.Token); } throw new InvalidOperationException("Runtime coordination changed concurrently."); }
    public async ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(guard); var scope = RequireScope(); var row = await LoadCoordination(scope, guard.ArtifactId, cancellationToken); if (row is null) return false; var s = ReadCoordination(row, scope, guard.ArtifactId, CreateId(scope, guard.ArtifactId)); if (s.Guard is not { } g || g.OperationId != guard.OperationId || g.Token != guard.ConcurrencyToken) return false; return await UpdateCoordination(row, new(s.Leases, null), cancellationToken); }
    public async ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(guard); var scope = RequireScope(); var id = CreateId(scope, guard.ArtifactId); var a = await context.WorkflowExecutables.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(guard.ArtifactId) && x.ArtifactId == guard.ArtifactId, cancellationToken); var c = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(guard.ArtifactId) && x.ArtifactId == guard.ArtifactId, cancellationToken); if (a is null || c is null) return false; var s = ReadCoordination(c, scope, guard.ArtifactId, id); if (s.Guard is not { } g || g.OperationId != guard.OperationId || g.Token != guard.ConcurrencyToken || g.ExpiresAt <= now || s.Leases.Any(x => x.Value.ExpiresAt > now)) return false; context.RemoveRange(a, c); await context.SaveChangesAsync(cancellationToken); return true; }
    private async Task<WorkflowExecutableCoordinationEntity?> LoadCoordination(string scope, string artifactId, CancellationToken ct) => await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == CreateId(scope, artifactId) && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.ArtifactIdHash == Hash(artifactId) && x.ArtifactId == artifactId, ct);
    private async Task<bool> UpdateCoordination(WorkflowExecutableCoordinationEntity row, CoordinationState state, CancellationToken ct)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(state);
        row.Revision++;
        try
        { await context.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }

    private static WorkflowExecutableEntity ToEntity(WorkflowExecutable executable, string scope, string id, string json) => new()
    {
        Id = id,
        ScopeKey = Encode(scope),
        ScopeKeyHash = Hash(scope),
        ArtifactId = executable.Identity.ArtifactId,
        ArtifactIdHash = Hash(executable.Identity.ArtifactId),
        ArtifactIdOrderKey = OrderKey(executable.Identity.ArtifactId),
        ContentJson = json,
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion
    };

    private static WorkflowExecutableCoordinationEntity ToCoordinationEntity(string artifactId, string scope, string id) => new()
    {
        Id = id,
        ScopeKey = Encode(scope),
        ScopeKeyHash = Hash(scope),
        ArtifactId = artifactId,
        ArtifactIdHash = Hash(artifactId),
        ContentJson = RuntimeArtifactJson.Serialize(CoordinationState.Empty),
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
        Revision = 1
    };

    private static WorkflowExecutable Read(WorkflowExecutableEntity row, string scope, string expected, string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != expected || row.ArtifactIdHash != Hash(expected) ||
            row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.ArtifactIdOrderKey != OrderKey(expected))
            throw new InvalidDataException("The persisted workflow executable row is corrupt.");
        try
        {
            var x = RuntimeArtifactJson.Deserialize<WorkflowExecutable>(row.ContentJson);
            if (x.Identity.ArtifactId != expected)
                throw new InvalidDataException("The persisted workflow executable identity is corrupt.");
            return x;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("The persisted workflow executable payload is corrupt.", e); }
    }

    private static CoordinationState ReadCoordination(WorkflowExecutableCoordinationEntity row, string scope, string expected, string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ArtifactId != expected || row.ArtifactIdHash != Hash(expected) ||
            row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0)
            throw new InvalidDataException("The persisted workflow executable coordination row is corrupt.");
        return RuntimeArtifactJson.Deserialize<CoordinationState>(row.ContentJson);
    }

    private string RequireScope()
    {
        var current = accessContextAccessor.Current;
        if (current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF workflow executable persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private static void Validate(WorkflowExecutable x) { ArgumentNullException.ThrowIfNull(x); ArgumentException.ThrowIfNullOrWhiteSpace(x.Identity.ArtifactId); if (x.Identity.ArtifactId.Length > RuntimeArtifactEfModule.IdentityMaximumLength) throw new ArgumentOutOfRangeException(nameof(x)); }
    private static void ValidateTransition(string artifact, string id, DateTimeOffset expiry, DateTimeOffset now) { ArgumentException.ThrowIfNullOrWhiteSpace(artifact); ArgumentException.ThrowIfNullOrWhiteSpace(id); if (expiry <= now) throw new ArgumentOutOfRangeException(nameof(expiry)); }
    private static string CreateId(string scope, string artifactId) => Hash($"{scope.Length}:{scope}{artifactId.Length}:{artifactId}");
    private static string Hash(string x) => EfRelationalIdentity.Hash(x);
    private static string Encode(string x) => EfRelationalIdentity.Encode(x);
    private static string OrderKey(string x) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(x, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string Token() => Guid.NewGuid().ToString("N");
    private static string Encode(string x, string scope) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Hash(scope)}:{x}"));
    private static string? Decode(string? x, string scope) { if (x is null) return null; try { var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(x)); var prefix = $"{Hash(scope)}:"; if (!value.StartsWith(prefix, StringComparison.Ordinal)) throw new FormatException(); return value[prefix.Length..]; } catch (Exception e) when (e is FormatException or ArgumentException) { throw new ArgumentException("The workflow executable continuation token is invalid.", nameof(x), e); } }
    private sealed record Lease(string Id, string Token, DateTimeOffset ExpiresAt); private sealed record Guard(string OperationId, string Token, DateTimeOffset ExpiresAt); private sealed record CoordinationState(IReadOnlyDictionary<string, Lease> Leases, Guard? Guard) { public static CoordinationState Empty => new(new Dictionary<string, Lease>(StringComparer.Ordinal), null); }
}
