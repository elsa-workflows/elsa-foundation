using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfExecutableActivityTemplateStore(BookmarkStateDbContext context) : IExecutableActivityTemplateStore
{
    public async ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
    {
        Validate(template); cancellationToken.ThrowIfCancellationRequested(); context.ChangeTracker.Clear();
        var id = template.TemplateId; var claimId = HashClaimId(template.TemplateHash); var json = RuntimeArtifactJson.Serialize(template);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var current = await context.ExecutableActivityTemplates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            var claim = await context.ExecutableActivityTemplateHashClaims.SingleOrDefaultAsync(x => x.Id == claimId, cancellationToken);
            if (current is not null)
            {
                var existing = Read(current, id);
                if (claim is null) throw new InvalidDataException("Executable activity template has no hash claim.");
                EnsureClaim(claim, template.TemplateHash, id);
                if (!StringComparer.Ordinal.Equals(RuntimeArtifactJson.Serialize(existing), json)) throw new InvalidOperationException($"Executable activity template '{id}' already exists with different content.");
                await tx.CommitAsync(cancellationToken); return;
            }
            if (claim is not null) { EnsureClaim(claim, template.TemplateHash, id); throw new InvalidOperationException($"Executable activity template hash '{template.TemplateHash}' is owned by another template."); }
            context.ExecutableActivityTemplates.Add(new ExecutableActivityTemplateEntity { Id=id,TemplateId=id,TemplateHash=template.TemplateHash,TemplateIdOrderKey=OrderKey(id),ContentJson=json,SchemaVersion=RuntimeArtifactEfModule.SchemaVersion });
            context.ExecutableActivityTemplateHashClaims.Add(new ExecutableActivityTemplateHashClaimEntity { Id=claimId,TemplateHash=template.TemplateHash,TemplateId=id,ContentJson=RuntimeArtifactJson.Serialize(new HashClaim(template.TemplateHash,id)),SchemaVersion=RuntimeArtifactEfModule.SchemaVersion });
            await context.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(e) || EfRelationalExceptionClassifier.IsTransientWriteConflict(e)) { context.ChangeTracker.Clear(); throw new InvalidOperationException("Executable activity template hash ownership changed concurrently; retry the operation.",e); }
        catch { context.ChangeTracker.Clear(); throw; }
    }
    public async ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId,CancellationToken cancellationToken=default){ArgumentException.ThrowIfNullOrWhiteSpace(templateId);var row=await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==templateId,cancellationToken);return row is null?null:Read(row,templateId);}
    public async ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash,CancellationToken cancellationToken=default){ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);var claim=await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==HashClaimId(templateHash),cancellationToken);if(claim is null)return null;var c=ReadClaim(claim,templateHash);var row=await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==c.TemplateId,cancellationToken)??throw new InvalidDataException("Executable activity template hash claim points to a missing template.");return Read(row,c.TemplateId);}
    public async ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request,CancellationToken cancellationToken=default){ArgumentNullException.ThrowIfNull(request);var cursor=Decode(request.ContinuationToken);var rows=await context.ExecutableActivityTemplates.AsNoTracking().Where(x=>cursor==null||string.CompareOrdinal(x.TemplateIdOrderKey,cursor)>0).OrderBy(x=>x.TemplateIdOrderKey).Take(request.Limit+1).ToArrayAsync(cancellationToken);var more=rows.Length>request.Limit;if(more)rows=rows[..request.Limit];return new(request,rows.Select(x=>Read(x,x.TemplateId)).ToArray(),more?Encode(rows[^1].TemplateIdOrderKey):null);}
    public async ValueTask<bool> DeleteAsync(string templateId,CancellationToken cancellationToken=default){ArgumentException.ThrowIfNullOrWhiteSpace(templateId);var row=await context.ExecutableActivityTemplates.SingleOrDefaultAsync(x=>x.Id==templateId,cancellationToken);if(row is null)return false;var template=Read(row,templateId);var claim=await context.ExecutableActivityTemplateHashClaims.SingleOrDefaultAsync(x=>x.Id==HashClaimId(template.TemplateHash),cancellationToken);if(claim is null)throw new InvalidDataException("Executable activity template has no hash claim.");var owner=ReadClaim(claim,template.TemplateHash);if(owner.TemplateId!=templateId)throw new InvalidDataException("Executable activity template hash claim owner is corrupt.");context.RemoveRange(row,claim);await context.SaveChangesAsync(cancellationToken);return true;}
    private static ExecutableActivityTemplate Read(ExecutableActivityTemplateEntity row,string expected){if(row.Id!=expected||row.TemplateId!=expected||row.SchemaVersion!=RuntimeArtifactEfModule.SchemaVersion||row.TemplateIdOrderKey!=OrderKey(expected))throw new InvalidDataException("The persisted executable activity template row is corrupt.");try{var x=RuntimeArtifactJson.Deserialize<ExecutableActivityTemplate>(row.ContentJson);if(x.TemplateId!=expected||x.TemplateHash!=row.TemplateHash)throw new InvalidDataException("The persisted executable activity template projection is corrupt.");return x;}catch(Exception e)when(e is JsonException or InvalidOperationException or ArgumentException or NotSupportedException){throw new InvalidDataException("The persisted executable activity template payload is corrupt.",e);}}
    private static HashClaim ReadClaim(ExecutableActivityTemplateHashClaimEntity row,string hash){if(row.Id!=HashClaimId(hash)||row.TemplateHash!=hash||row.SchemaVersion!=RuntimeArtifactEfModule.SchemaVersion)throw new InvalidDataException("The persisted executable activity template hash claim is corrupt.");return RuntimeArtifactJson.Deserialize<HashClaim>(row.ContentJson);}
    private static void EnsureClaim(ExecutableActivityTemplateHashClaimEntity row,string hash,string id){var c=ReadClaim(row,hash);if(c.TemplateId!=id)throw new InvalidOperationException($"Executable activity template hash '{hash}' is owned by another template.");}
    private static void Validate(ExecutableActivityTemplate x){ArgumentNullException.ThrowIfNull(x);ArgumentException.ThrowIfNullOrWhiteSpace(x.TemplateId);ArgumentException.ThrowIfNullOrWhiteSpace(x.TemplateHash);if(x.TemplateId.Length>128)throw new ArgumentOutOfRangeException(nameof(x.TemplateId));if(x.TemplateHash.Length>450)throw new ArgumentOutOfRangeException(nameof(x.TemplateHash));_ = HashClaimId(x.TemplateHash);}
    private static string HashClaimId(string hash){ArgumentException.ThrowIfNullOrWhiteSpace(hash);return $"templateHash:{EfRelationalIdentity.Hash(hash)}";}
    private static string OrderKey(string value)=>Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value,RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string Encode(string x)=>Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(x)); private static string? Decode(string? x){if(x is null)return null;try{return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(x));}catch(Exception e)when(e is FormatException or ArgumentException){throw new ArgumentException("The executable activity template continuation token is invalid.",nameof(x),e);}}
    private sealed record HashClaim(string TemplateHash,string TemplateId);
}
