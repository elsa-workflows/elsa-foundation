using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore
{
    bool IWorkflowExecutableStore.SupportsModelMaterial => true;

    /// <inheritdoc />
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">The material store failed; its original infrastructure cause and context are retained.</exception>
    async ValueTask<RuntimeExecutionMaterial?> IWorkflowExecutableStore.FindMaterialAsync(string artifactId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        try
        {
            var pair = await LoadPairAsync(scope, artifactId, cancellationToken);
            return pair is null ? null : ReadMaterial(pair.Value.Artifact, scope, artifactId, CreateId(scope, artifactId));
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            cancellationToken.ThrowIfCancellationRequested();
            throw NormalizeProviderFailure("reading model/native material", artifactId, exception, scope);
        }
    }

    /// <inheritdoc />
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">The material store failed; its original infrastructure cause and context are retained.</exception>
    async ValueTask IWorkflowExecutableStore.SaveModelAsync(RuntimeModelMaterial material, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(material);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateModel(material);
        var scope = RequireScope();
        var artifactId = material.Identity.ArtifactId;
        var id = CreateId(scope, artifactId);
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Saving immutable model material requires a clean context and its own transaction.");
        context.ChangeTracker.Clear();
        try
        {
            if (await LoadPairAsync(scope, artifactId, cancellationToken) is { } current)
            {
                EnsureEquivalent(current.Artifact);
                return;
            }
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(context, "saving model material", artifactId,
                () => context.Database.BeginTransactionAsync(cancellationToken));
            var incarnation = NewIncarnationId();
            context.WorkflowExecutables.Add(new WorkflowExecutableEntity
            {
                Id = id, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), IsModelMaterial = true,
                ArtifactId = Encode(artifactId), ArtifactIdHash = Hash(artifactId), ArtifactIdOrderKey = OrderKey(artifactId),
                ArtifactHash = material.Identity.ArtifactHash, IncarnationId = incarnation,
                ContentJson = RuntimeArtifactJson.Serialize(new ModelMaterialDocument(1, DateTimeOffset.UtcNow, material)),
                SchemaVersion = RuntimeArtifactEfModule.SchemaVersion
            });
            context.WorkflowExecutableCoordinations.Add(ToCoordinationEntity(artifactId, scope, id, incarnation));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(context, "saving model material", artifactId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(context, "saving model material", artifactId, () => transaction.CommitAsync(cancellationToken));
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
        {
            context.ChangeTracker.Clear();
            var winner = await LoadPairAsync(scope, artifactId, cancellationToken);
            if (winner is null) throw NormalizeProviderFailure("saving model material", artifactId, exception);
            EnsureEquivalent(winner.Value.Artifact);
        }
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw NormalizeProviderFailure("saving model material", artifactId, exception, scope);
        }
        finally { context.ChangeTracker.Clear(); }

        void EnsureEquivalent(WorkflowExecutableEntity row)
        {
            var existing = ReadMaterial(row, scope, artifactId, id);
            if (existing.Model is null || existing.Identity.ArtifactHash != material.Identity.ArtifactHash)
                throw new InvalidOperationException($"Artifact '{artifactId}' is already bound to different material.");
        }
    }

    /// <inheritdoc />
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">The material store failed; its original infrastructure cause and context are retained.</exception>
    async ValueTask<RuntimeStorePage<RuntimeExecutionMaterial>> IWorkflowExecutableStore.ListMaterialPageAsync(
        RuntimeStorePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var cursor = Decode(request.ContinuationToken, scope);
        try
        {
            var rows = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(context, "listing model/native material", scope,
                () => context.WorkflowExecutables.AsNoTracking()
                    .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && (cursor == null || x.ArtifactIdOrderKey.CompareTo(cursor) > 0))
                    .OrderBy(x => x.ArtifactIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken));
            var more = rows.Length > request.Limit;
            if (more) rows = rows[..request.Limit];
            var items = rows.Select(row => ReadMaterial(row, scope, Decode(row.ArtifactId), CreateId(scope, Decode(row.ArtifactId)))).ToArray();
            return new(request, items, more ? Encode(rows[^1].ArtifactIdOrderKey, scope) : null);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            cancellationToken.ThrowIfCancellationRequested();
            throw NormalizeProviderFailure("listing model/native material", scope, exception, scope);
        }
    }

    internal static RuntimeExecutionMaterial ReadMaterial(WorkflowExecutableEntity row, string scope, string expected, string id)
    {
        if (!row.IsModelMaterial) return new(Read(row, scope, expected, id), row.IncarnationId);
        if (EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, row.SchemaVersion) || row.Id != id ||
            row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.ArtifactId != Encode(expected) ||
            row.ArtifactIdHash != Hash(expected) || row.ArtifactIdOrderKey != OrderKey(expected) || string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted model material envelope is corrupt.");
        try
        {
            var content = RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson));
            var document = RuntimeArtifactJson.Deserialize<ModelMaterialDocument>(content[nameof(row.ContentJson)]!);
            if (document.RuntimeModelMaterialVersion != 1) throw new NotSupportedException("Unsupported model material document.");
            ValidateModel(document.Material);
            if (document.Material.Identity.ArtifactId != expected || document.Material.Identity.ArtifactHash != row.ArtifactHash)
                throw new InvalidDataException("The model material identity differs from its envelope.");
            return new(document.Material, row.IncarnationId, document.CreatedAt);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException or NullReferenceException)
        { throw new InvalidDataException("The persisted model material payload is corrupt.", exception); }
    }

    private static void ValidateModel(RuntimeModelMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Identity.ArtifactId.Length > RuntimeArtifactEfModule.IdentityMaximumLength ||
            material.Identity.ArtifactHash.Length > RuntimeArtifactEfModule.HashMaximumLength ||
            material.Identity.ArtifactHash != material.ComputeContentHash())
            throw new ArgumentException("Model material requires its complete behavioral content hash and a supported artifact ID.", nameof(material));
    }

    private sealed record ModelMaterialDocument(int RuntimeModelMaterialVersion, DateTimeOffset CreatedAt, RuntimeModelMaterial Material);
}
