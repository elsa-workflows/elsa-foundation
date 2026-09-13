using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableStore(BookmarkStateDbContext context) : IWorkflowExecutableStore
{
    public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => SaveBatchAsync([executable], cancellationToken);
    public async ValueTask SaveBatchAsync(IReadOnlyList<WorkflowExecutable> executables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executables); cancellationToken.ThrowIfCancellationRequested();
        if (executables.Select(x => x.Identity.ArtifactId).Distinct(StringComparer.Ordinal).Count() != executables.Count) throw new ArgumentException("A workflow executable batch must contain distinct artifact ids.", nameof(executables));
        foreach (var item in executables) Validate(item);
        if (executables.Count == 0) return;
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in executables)
            {
                var id = item.Identity.ArtifactId;
                var artifact = await context.WorkflowExecutables.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                var coordination = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (artifact is null && coordination is null)
                {
                    var json = RuntimeArtifactJson.Serialize(item);
                    context.WorkflowExecutables.Add(new WorkflowExecutableEntity { Id = id, ArtifactId = id, ArtifactIdOrderKey = OrderKey(id), ContentJson = json, SchemaVersion = RuntimeArtifactEfModule.SchemaVersion });
                    context.WorkflowExecutableCoordinations.Add(new WorkflowExecutableCoordinationEntity { Id = id, ArtifactId = id, ContentJson = RuntimeArtifactJson.Serialize(CoordinationState.Empty), SchemaVersion = RuntimeArtifactEfModule.SchemaVersion, Revision = 1 });
                }
                else if (artifact is null || coordination is null)
                    throw new InvalidDataException($"Workflow executable '{id}' has incomplete persisted state.");
                else { _ = Read(artifact, id); _ = ReadCoordination(coordination, id); }
            }
            await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception)) { context.ChangeTracker.Clear(); throw new InvalidOperationException("The workflow executable changed concurrently; retry the operation.", exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    { ArgumentException.ThrowIfNullOrWhiteSpace(artifactId); cancellationToken.ThrowIfCancellationRequested(); var row = await context.WorkflowExecutables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == artifactId, cancellationToken); return row is null ? null : Read(row, artifactId); }
    public async ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(request); var cursor = Decode(request.ContinuationToken); var rows = await context.WorkflowExecutables.AsNoTracking().Where(x => cursor == null || string.CompareOrdinal(x.ArtifactIdOrderKey, cursor) > 0).OrderBy(x => x.ArtifactIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken); var more = rows.Length > request.Limit; if (more) rows = rows[..request.Limit]; var items = rows.Select(x => Read(x, x.ArtifactId)).ToArray(); return new RuntimeStorePage<WorkflowExecutable>(request, items, more ? Encode(rows[^1].ArtifactIdOrderKey) : null); }
    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) { ArgumentException.ThrowIfNullOrWhiteSpace(artifactId); var a = await context.WorkflowExecutables.SingleOrDefaultAsync(x => x.Id == artifactId, cancellationToken); var c = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x => x.Id == artifactId, cancellationToken); if (a is null && c is null) return false; if (a is null || c is null) throw new InvalidDataException("Workflow executable storage is incomplete."); _ = Read(a, artifactId); _ = ReadCoordination(c, artifactId); context.RemoveRange(a, c); await context.SaveChangesAsync(cancellationToken); return true; }
    public async ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) { ValidateTransition(artifactId, leaseId, expiresAt, now); for (var i=0;i<16;i++){ var row=await LoadCoordination(artifactId,cancellationToken); if(row is null)return null; var s=ReadCoordination(row,artifactId); var leases=s.Leases.Where(x=>x.Value.ExpiresAt>now).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal); if(s.Guard is { } g&&g.ExpiresAt>now)return null; if(leases.TryGetValue(leaseId,out var existing))return new(artifactId,leaseId,existing.Token); leases[leaseId]=new(leaseId,Token(),expiresAt); if(await UpdateCoordination(row, new(leases,null),cancellationToken)) return new(artifactId,leaseId,leases[leaseId].Token);} throw new InvalidOperationException("Runtime coordination changed concurrently."); }
    public async ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(lease); ValidateTransition(lease.ArtifactId,lease.LeaseId,expiresAt,now); var row=await LoadCoordination(lease.ArtifactId,cancellationToken); if(row is null)return false; var s=ReadCoordination(row,lease.ArtifactId); if(s.Guard is { } g&&g.ExpiresAt>now||!s.Leases.TryGetValue(lease.LeaseId,out var current)||current.Token!=lease.ConcurrencyToken||current.ExpiresAt<=now)return false; var leases=s.Leases.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal); leases[lease.LeaseId]=current with { ExpiresAt=expiresAt }; return await UpdateCoordination(row,new(leases,s.Guard),cancellationToken); }
    public async ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(lease); var row=await LoadCoordination(lease.ArtifactId,cancellationToken); if(row is null)return; var s=ReadCoordination(row,lease.ArtifactId); if(!s.Leases.TryGetValue(lease.LeaseId,out var c)||c.Token!=lease.ConcurrencyToken)return; var l=s.Leases.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal); l.Remove(lease.LeaseId); _=await UpdateCoordination(row,new(l,s.Guard),cancellationToken); }
    public async ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId,string operationId,DateTimeOffset expiresAt,DateTimeOffset now,CancellationToken cancellationToken=default){ValidateTransition(artifactId,operationId,expiresAt,now);for(var i=0;i<16;i++){var row=await LoadCoordination(artifactId,cancellationToken);if(row is null)return null;var s=ReadCoordination(row,artifactId);var leases=s.Leases.Where(x=>x.Value.ExpiresAt>now).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);if(leases.Count!=0)return null;if(s.Guard is { } old&&old.ExpiresAt>now)return old.OperationId==operationId?new(artifactId,operationId,old.Token):null;var guard=new Guard(operationId,Token(),expiresAt);if(await UpdateCoordination(row,new(leases,guard),cancellationToken))return new(artifactId,operationId,guard.Token);}throw new InvalidOperationException("Runtime coordination changed concurrently.");}
    public async ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard,CancellationToken cancellationToken=default){ArgumentNullException.ThrowIfNull(guard);var row=await LoadCoordination(guard.ArtifactId,cancellationToken);if(row is null)return false;var s=ReadCoordination(row,guard.ArtifactId);if(s.Guard is not { } g||g.OperationId!=guard.OperationId||g.Token!=guard.ConcurrencyToken)return false;return await UpdateCoordination(row,new(s.Leases,null),cancellationToken);}
    public async ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard,DateTimeOffset now,CancellationToken cancellationToken=default){ArgumentNullException.ThrowIfNull(guard);var a=await context.WorkflowExecutables.SingleOrDefaultAsync(x=>x.Id==guard.ArtifactId,cancellationToken);var c=await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x=>x.Id==guard.ArtifactId,cancellationToken);if(a is null||c is null)return false;var s=ReadCoordination(c,guard.ArtifactId);if(s.Guard is not { }g||g.OperationId!=guard.OperationId||g.Token!=guard.ConcurrencyToken||g.ExpiresAt<=now||s.Leases.Any(x=>x.Value.ExpiresAt>now))return false;context.RemoveRange(a,c);await context.SaveChangesAsync(cancellationToken);return true;}
    private async Task<WorkflowExecutableCoordinationEntity?> LoadCoordination(string id,CancellationToken ct)=>await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(x=>x.Id==id,ct);
    private async Task<bool> UpdateCoordination(WorkflowExecutableCoordinationEntity row,CoordinationState state,CancellationToken ct){row.ContentJson=RuntimeArtifactJson.Serialize(state);row.Revision++;try{await context.SaveChangesAsync(ct);return true;}catch(DbUpdateConcurrencyException){context.ChangeTracker.Clear();return false;}}
    private static WorkflowExecutable Read(WorkflowExecutableEntity row,string expected){if(row.Id!=expected||row.ArtifactId!=expected||row.SchemaVersion!=RuntimeArtifactEfModule.SchemaVersion||row.ArtifactIdOrderKey!=OrderKey(expected))throw new InvalidDataException("The persisted workflow executable row is corrupt.");try{var x=RuntimeArtifactJson.Deserialize<WorkflowExecutable>(row.ContentJson);if(x.Identity.ArtifactId!=expected)throw new InvalidDataException("The persisted workflow executable identity is corrupt.");return x;}catch(Exception e)when(e is JsonException or InvalidOperationException or ArgumentException or NotSupportedException){throw new InvalidDataException("The persisted workflow executable payload is corrupt.",e);}}
    private static CoordinationState ReadCoordination(WorkflowExecutableCoordinationEntity row,string expected){if(row.Id!=expected||row.ArtifactId!=expected||row.SchemaVersion!=RuntimeArtifactEfModule.SchemaVersion||row.Revision<=0)throw new InvalidDataException("The persisted workflow executable coordination row is corrupt.");return RuntimeArtifactJson.Deserialize<CoordinationState>(row.ContentJson);}
    private static void Validate(WorkflowExecutable x){ArgumentNullException.ThrowIfNull(x);ArgumentException.ThrowIfNullOrWhiteSpace(x.Identity.ArtifactId);}
    private static void ValidateTransition(string artifact,string id,DateTimeOffset expiry,DateTimeOffset now){ArgumentException.ThrowIfNullOrWhiteSpace(artifact);ArgumentException.ThrowIfNullOrWhiteSpace(id);if(expiry<=now)throw new ArgumentOutOfRangeException(nameof(expiry));}
    private static string OrderKey(string x)=>Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(x,RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string Token()=>Guid.NewGuid().ToString("N");
    private static string Encode(string x)=>Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(x)); private static string Decode(string? x){if(x is null)return null!;try{return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(x));}catch(Exception e)when(e is FormatException or ArgumentException){throw new ArgumentException("The workflow executable continuation token is invalid.",nameof(x),e);}}
    private sealed record Lease(string Id,string Token,DateTimeOffset ExpiresAt); private sealed record Guard(string OperationId,string Token,DateTimeOffset ExpiresAt); private sealed record CoordinationState(IReadOnlyDictionary<string,Lease> Leases,Guard? Guard){public static CoordinationState Empty=>new(new Dictionary<string,Lease>(StringComparer.Ordinal),null);}
}
