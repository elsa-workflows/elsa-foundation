using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Decisions.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Decisions.Persistence.EntityFrameworkCore;

internal sealed class EfDecisionStore<TContext>(IDbContextFactory<TContext> factory) : IDecisionReader, IDecisionWriter where TContext : DbContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaximumInventory = 1000;

    public ValueTask<IReadOnlyList<DecisionAsset>> ListAssetsAsync(string tenantId, CancellationToken ct = default) => List<DecisionAsset>(tenantId, "asset", null, ct);
    public ValueTask<DecisionAsset?> FindAssetAsync(string tenantId, string assetId, CancellationToken ct = default) => Find<DecisionAsset>(tenantId, "asset", assetId, null, ct);
    public ValueTask<IReadOnlyList<DecisionVersion>> ListVersionsAsync(string tenantId, string assetId, CancellationToken ct = default) => List<DecisionVersion>(tenantId, "version", assetId, ct);
    public ValueTask<DecisionVersion?> FindVersionAsync(string tenantId, string assetId, string versionId, CancellationToken ct = default) => Find<DecisionVersion>(tenantId, "version", versionId, assetId, ct);
    public ValueTask<IReadOnlyList<DecisionCase>> ListCasesAsync(string tenantId, string assetId, CancellationToken ct = default) => List<DecisionCase>(tenantId, "case", assetId, ct);
    public ValueTask<DecisionCase?> FindCaseAsync(string tenantId, string assetId, string caseId, CancellationToken ct = default) => Find<DecisionCase>(tenantId, "case", caseId, assetId, ct);
    public ValueTask<IReadOnlyList<DecisionTestReceipt>> ListRunsAsync(string tenantId, string assetId, CancellationToken ct = default) => List<DecisionTestReceipt>(tenantId, "run", assetId, ct);
    public ValueTask<DecisionTestReceipt?> FindRunAsync(string tenantId, string assetId, string runId, CancellationToken ct = default) => Find<DecisionTestReceipt>(tenantId, "run", runId, assetId, ct);

    public ValueTask<DecisionOperationReceipt?> FindOperationAsync(DecisionOperation operation, CancellationToken ct = default)
    {
        ValidateOperation(operation);
        return Find<DecisionOperationReceipt>(operation.TenantId, "operation", OperationId(operation), null, ct);
    }

    public async ValueTask<DecisionTestReceipt?> FindCompletedOperationAsync(DecisionOperation operation, string assetId, CancellationToken ct = default)
    {
        ValidateOperation(operation);
        var prior = await Find<DecisionOperationReceipt>(operation.TenantId, "operation", OperationId(operation), null, ct);
        if (prior is null) return null;
        if (prior.Kind != "test") throw Conflict();
        return await FindRunAsync(operation.TenantId, assetId, prior.Confirmation.Id, ct)
            ?? throw new DecisionDesignException("not-found", "The completed decision receipt is no longer retained.");
    }

    public ValueTask<DecisionWriteConfirmation> SaveAssetAsync(DecisionOperation operation, SaveDecision command, CancellationToken ct = default) =>
        Mutate(operation, "save", command, async (db, token) =>
        {
            Text(command.Name, 2048); Text(command.Xml, 1024 * 1024); Text(command.SourceHash, 64);
            if (Encoding.UTF8.GetByteCount(command.Xml) > 1024 * 1024 || Hash(command.Xml) != command.SourceHash) throw Invalid();
            if (command.Description?.Length > 4096) throw Invalid();
            var id = command.Id ?? "decision-" + Guid.NewGuid().ToString("N");
            var existing = command.Id is null ? null : await Required(db, operation.TenantId, "asset", id, null, token);
            if (existing is not null && existing.Revision != command.ExpectedRevision) throw Conflict();
            if (existing is null && command.ExpectedRevision is not null) throw Conflict();
            if (existing is null && await Query(db, operation.TenantId, "asset").CountAsync(token) >= MaximumInventory) throw Invalid();
            var current = existing is null ? null : Read<DecisionAsset>(existing);
            var now = DateTimeOffset.UtcNow; var revision = Revision();
            var asset = new DecisionAsset(id, command.Name.Trim(), command.Description, revision, command.Xml, command.SourceHash,
                command.IsSensitive, current?.PublishedVersionId, current?.CreatedAt ?? now, now);
            Upsert(db, existing, operation.TenantId, "asset", id, id, revision, asset);
            return new(id, revision);
        }, ct);

    public ValueTask<DecisionWriteConfirmation> PublishAsync(DecisionOperation operation, PublishDecision command, CancellationToken ct = default) =>
        Mutate(operation, "publish", new { command.AssetId, command.ExpectedRevision }, async (db, token) =>
        {
            var row = await Required(db, operation.TenantId, "asset", command.AssetId, null, token);
            if (row.Revision != command.ExpectedRevision) throw Conflict();
            var asset = Read<DecisionAsset>(row);
            if (asset.SourceHash != command.Artifact.SourceHash) throw Conflict();
            var versions = await Query(db, operation.TenantId, "version").Where(x => x.OwnerId == command.AssetId).ToArrayAsync(token);
            if (versions.Length >= MaximumInventory) throw Invalid();
            var next = versions.Select(Read<DecisionVersion>).Select(v => v.Number).DefaultIfEmpty(0).Max() + 1;
            var id = "version-" + Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
            var version = new DecisionVersion(id, asset.Id, next, asset.Name, asset.Xml, asset.SourceHash, command.Artifact, asset.IsSensitive, now);
            Upsert(db, null, operation.TenantId, "version", id, asset.Id, "1", version);
            var revision = Revision();
            Upsert(db, row, operation.TenantId, "asset", asset.Id, asset.Id, revision, asset with { Revision = revision, PublishedVersionId = id, UpdatedAt = now });
            return new(id, revision);
        }, ct);

    public ValueTask<DecisionWriteConfirmation> SaveCaseAsync(DecisionOperation operation, SaveDecisionCase command, CancellationToken ct = default) =>
        Mutate(operation, "case.save", command, async (db, token) =>
        {
            _ = await Required(db, operation.TenantId, "asset", command.AssetId, null, token);
            Text(command.Name, 2048); Text(command.ExpectedStatus, 64);
            if (command.Inputs.Count > 32 || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(command.Inputs, Json)) > 256 * 1024 ||
                command.ExpectedResult.ValueKind == JsonValueKind.Undefined || command.ExpectedResult.GetRawText().Length > 256 * 1024) throw Invalid();
            var id = command.Id ?? "case-" + Guid.NewGuid().ToString("N");
            var row = command.Id is null ? null : await Required(db, operation.TenantId, "case", id, command.AssetId, token);
            if (row is not null && row.Revision != command.ExpectedRevision || row is null && command.ExpectedRevision is not null) throw Conflict();
            if (row is null && await Query(db, operation.TenantId, "case").CountAsync(x => x.OwnerId == command.AssetId, token) >= MaximumInventory) throw Invalid();
            var current = row is null ? null : Read<DecisionCase>(row); var revision = Revision(); var now = DateTimeOffset.UtcNow;
            var saved = new DecisionCase(id, command.AssetId, command.Name, revision, command.Inputs, command.ExpectedStatus,
                command.ExpectedResult.Clone(), command.IsSensitive, current?.CreatedAt ?? now, now);
            Upsert(db, row, operation.TenantId, "case", id, command.AssetId, revision, saved);
            return new(id, revision);
        }, ct);

    public ValueTask<DecisionWriteConfirmation> DeleteCaseAsync(DecisionOperation operation, string assetId, string caseId, string expectedRevision, CancellationToken ct = default) =>
        Mutate(operation, "case.delete", new { assetId, caseId, expectedRevision }, async (db, token) =>
        {
            var row = await Required(db, operation.TenantId, "case", caseId, assetId, token);
            if (row.Revision != expectedRevision) throw Conflict();
            db.Remove(row); return new(caseId, expectedRevision);
        }, ct);

    public ValueTask<DecisionWriteConfirmation> SaveCompletedRunAsync(DecisionOperation operation, DecisionTestReceipt receipt, CancellationToken ct = default) =>
        Mutate(operation, "test", new { receipt.AssetId, receipt.RequestFingerprint }, async (db, token) =>
        {
            if (receipt.ActorId != operation.ActorId || receipt.EvaluatedAt > receipt.CompletedAt || receipt.Comparisons.Count > MaximumInventory) throw Invalid();
            _ = await Required(db, operation.TenantId, "asset", receipt.AssetId, null, token);
            if (await Query(db, operation.TenantId, "run").CountAsync(row => row.OwnerId == receipt.AssetId, token) >= MaximumInventory)
                throw new DecisionDesignException("limit", "Retained decision test history reached its limit of 1000 runs. Delete a retained run before testing again.");
            Upsert(db, null, operation.TenantId, "run", receipt.Id, receipt.AssetId, "1", receipt);
            return new(receipt.Id, "1");
        }, ct);

    public ValueTask<DecisionWriteConfirmation> DeleteRunAsync(DecisionOperation operation, string assetId, string runId, CancellationToken ct = default) =>
        Mutate(operation, "run.delete", new { assetId, runId }, async (db, token) =>
        {
            var row = await Required(db, operation.TenantId, "run", runId, assetId, token);
            // Keep only the idempotency tombstone: deleted history is never reconstructed by replay.
            db.Remove(row); return new(runId, "deleted");
        }, ct);

    private async ValueTask<IReadOnlyList<T>> List<T>(string tenant, string kind, string? owner, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var query = Query(db, tenant, kind).AsNoTracking();
            if (owner is not null) { Text(owner, 128); query = query.Where(x => x.OwnerId == owner); }
            var rows = await query.OrderBy(x => x.Id).Take(MaximumInventory + 1).ToArrayAsync(ct);
            if (rows.Length > MaximumInventory) throw new DecisionDesignException("limit", "The bounded Decisions inventory limit was reached.");
            return rows.Select(Read<T>).ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (DecisionDesignException) { throw; }
        catch (Exception ex) { throw Unavailable(ex); }
    }

    private async ValueTask<T?> Find<T>(string tenant, string kind, string id, string? owner, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            Text(id, 128); if (owner is not null) Text(owner, 128);
            var row = await Query(db, tenant, kind).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (owner == null || x.OwnerId == owner), ct);
            return row is null ? default : Read<T>(row);
        }
        catch (OperationCanceledException) { throw; }
        catch (DecisionDesignException) { throw; }
        catch (Exception ex) { throw Unavailable(ex); }
    }

    private async ValueTask<DecisionWriteConfirmation> Mutate(DecisionOperation operation, string kind, object command,
        Func<TContext, CancellationToken, Task<DecisionWriteConfirmation>> action, CancellationToken ct)
    {
        ValidateOperation(operation);
        var fingerprint = Fingerprint(new { kind, command }); var id = OperationId(operation);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var prior = await Query(db, operation.TenantId, "operation").SingleOrDefaultAsync(x => x.Id == id, ct);
            if (prior is not null)
            {
                var saved = Read<DecisionOperationReceipt>(prior);
                if (saved.Fingerprint != fingerprint) throw Conflict();
                return saved.Confirmation with { Replayed = true };
            }
            var confirmation = await action(db, ct);
            Upsert(db, null, operation.TenantId, "operation", id, operation.ActorId, "1", new DecisionOperationReceipt(kind, fingerprint, confirmation));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return confirmation;
        }
        catch (OperationCanceledException) { throw; }
        catch (DecisionDesignException) { throw; }
        catch (DbUpdateException ex)
        {
            // A concurrent equivalent operation wins by its unique receipt, not by timing.
            var winner = await Find<DecisionOperationReceipt>(operation.TenantId, "operation", id, null, ct);
            if (winner is not null && winner.Fingerprint == fingerprint) return winner.Confirmation with { Replayed = true };
            throw new DecisionDesignException("conflict", "The decision changed concurrently or the operation identity was reused.", ex);
        }
        catch (Exception ex) { throw Unavailable(ex); }
    }

    private static IQueryable<DecisionRecord> Query(DbContext db, string tenant, string kind)
    { Text(tenant, 256); var key = Hash(tenant); return db.Set<DecisionRecord>().Where(x => x.TenantKey == key && x.Kind == kind); }
    private static async Task<DecisionRecord> Required(DbContext db, string tenant, string kind, string id, string? owner, CancellationToken ct)
    {
        Text(id, 128); if (owner is not null) Text(owner, 128);
        var row = await Query(db, tenant, kind).SingleOrDefaultAsync(x => x.Id == id && (owner == null || x.OwnerId == owner), ct)
            ?? throw new DecisionDesignException("not-found", "The decision resource is unavailable.");
        EfSchemaVersion.EnsureCurrent(DecisionsEfModule.Chain, row.SchemaVersion);
        return row;
    }
    private static void Upsert<T>(DbContext db, DecisionRecord? row, string tenant, string kind, string id, string owner, string revision, T value)
    {
        if (row is null) { row = new() { TenantKey = Hash(tenant), Kind = kind, Id = id, OwnerId = owner }; db.Add(row); }
        EfSchemaVersion.EnsureCurrent(DecisionsEfModule.Chain, row.SchemaVersion);
        row.Revision = revision; row.SchemaVersion = DecisionsEfModule.SchemaVersion; row.Json = JsonSerializer.Serialize(value, Json);
    }
    private static T Read<T>(DecisionRecord row)
    {
        var content = DecisionsEfModule.Chain.Upcast<DecisionRecord>(row.SchemaVersion, (nameof(DecisionRecord.Json), row.Json));
        var value = JsonSerializer.Deserialize<T>(content[nameof(DecisionRecord.Json)]!, Json)
            ?? throw new DecisionDesignException("corrupt", "The retained decision record is invalid.");
        var consistent = value switch
        {
            DecisionAsset asset => row.Kind == "asset" && asset.Id == row.Id && asset.Revision == row.Revision && Hash(asset.Xml) == asset.SourceHash,
            DecisionVersion version => row.Kind == "version" && version.Id == row.Id && version.AssetId == row.OwnerId &&
                Hash(version.Xml) == version.SourceHash && version.Artifact.SourceHash == version.SourceHash,
            DecisionCase item => row.Kind == "case" && item.Id == row.Id && item.AssetId == row.OwnerId && item.Revision == row.Revision,
            DecisionTestReceipt run => row.Kind == "run" && run.Id == row.Id && run.AssetId == row.OwnerId,
            DecisionOperationReceipt operation => row.Kind == "operation" && operation.Fingerprint is { Length: 64 } && operation.Confirmation is not null &&
                operation.Kind is "save" or "publish" or "case.save" or "case.delete" or "test" or "run.delete",
            _ => false
        };
        if (!consistent) throw new DecisionDesignException("corrupt", "The retained decision content does not match its physical identity.");
        return value;
    }
    private static string Revision() => Guid.NewGuid().ToString("N");
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string OperationId(DecisionOperation operation) => Hash(JsonSerializer.Serialize(new[] { operation.ActorId, operation.OperationId }, Json));
    private static void ValidateOperation(DecisionOperation operation) { Text(operation.TenantId, 256); Text(operation.ActorId, 128); Text(operation.OperationId, 128); }
    private static void Text(string value, int max) { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw Invalid(); }
    private static DecisionDesignException Invalid() => new("invalid", "The decision request exceeds a supported bound or is malformed.");
    private static DecisionDesignException Conflict() => new("conflict", "The expected decision revision or operation identity does not match.");
    private static DecisionDesignException Unavailable(Exception ex) => new("unavailable", "The Decisions store is unavailable.", ex);
    private static string Fingerprint(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonical(element, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Canonical(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { writer.WriteStartObject(); foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(property.Value, writer); } writer.WriteEndObject(); }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}
