using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed immutable store for content-addressed reusable-activity execution templates.
/// </summary>
public sealed class GroundworkExecutableActivityTemplateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, boundedStore),
        IExecutableActivityTemplateStore
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public async ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var existingById = await FindAsync(template.TemplateId, cancellationToken);
            if (existingById is not null)
            {
                EnsureSameIdentityAndContent(existingById, template);
                return;
            }

            var existingByHash = await FindByHashAsync(template.TemplateHash, cancellationToken);
            if (existingByHash is not null)
            {
                throw new InvalidOperationException(
                    $"Template hash '{template.TemplateHash}' is already bound to id '{existingByHash.TemplateId}', not '{template.TemplateId}'.");
            }

            var result = await TryCreateAsync(template, cancellationToken);
            if (result == DocumentStoreWriteStatus.Saved)
                return;

            // Another store instance may have won the create-only write after our initial reads. It is
            // idempotent only when that winner persisted the exact same immutable identity and content.
            if (await FindAsync(template.TemplateId, cancellationToken) is { } winnerById)
            {
                EnsureSameIdentityAndContent(winnerById, template);
                return;
            }

            if (await FindByHashAsync(template.TemplateHash, cancellationToken) is { } winnerByHash)
            {
                throw new InvalidOperationException(
                    $"Template hash '{template.TemplateHash}' is already bound to id '{winnerByHash.TemplateId}', not '{template.TemplateId}'.");
            }

            throw new InvalidOperationException(
                $"Template '{template.TemplateId}' could not be created and no winning document was found. Provider status: '{result}'.");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        return await LoadDocumentAsync<TemplateDocument, ExecutableActivityTemplate>(
            templateId,
            document => document.Template,
            cancellationToken);
    }

    public async ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);

        var matches = await QueryDocumentsAsync<TemplateDocument, ExecutableActivityTemplate>(
            ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery,
            ElsaRuntimeStorageManifest.TemplateHashField,
            templateHash,
            document => document.Template,
            cancellationToken);

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Template hash '{templateHash}' is bound to more than one stored template; the content-addressed store is corrupt.")
        };
    }

    public async ValueTask<IReadOnlyCollection<ExecutableActivityTemplate>> ListAsync(CancellationToken cancellationToken = default) =>
        (await QueryDocumentsAsync<TemplateDocument, ExecutableActivityTemplate>(
            ElsaRuntimeStorageManifest.ListExecutableActivityTemplatesQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection,
            document => document.Template,
            cancellationToken)).ToArray();

    public async ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var existingEnvelope = await Store.LoadAsync(DocumentKind, templateId, cancellationToken);
        if (existingEnvelope is null)
            return false;
        var existing = Serializer.Deserialize<TemplateDocument>(existingEnvelope);
        var claimId = TemplateHashClaimId(existing.TemplateHash);

        await using var unitOfWork = await Store.BeginAsync(
            DocumentCommitScope.Of(DocumentKind, ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind),
            cancellationToken);
        var claimEnvelope = await unitOfWork.LoadAsync(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
            claimId,
            cancellationToken);
        var ownsClaim = claimEnvelope is not null &&
                        StringComparer.Ordinal.Equals(
                            Serializer.Deserialize<TemplateHashClaimDocument>(claimEnvelope).TemplateId,
                            templateId);

        var templateDeletion = await unitOfWork.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, templateId, existingEnvelope.Version),
            cancellationToken);
        if (templateDeletion.Status is DocumentStoreWriteStatus.NotFound or DocumentStoreWriteStatus.ConcurrencyConflict)
            return false;
        if (templateDeletion.Status != DocumentStoreWriteStatus.Deleted)
            throw new InvalidOperationException(
                $"Groundwork rejected executable activity template deletion for '{templateId}' with status '{templateDeletion.Status}'.");

        if (ownsClaim)
        {
            var claimDeletion = await unitOfWork.DeleteAsync(
                new DeleteDocumentRequest(
                    ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
                    claimId,
                    claimEnvelope!.Version),
                cancellationToken);
            if (claimDeletion.Status is DocumentStoreWriteStatus.NotFound or DocumentStoreWriteStatus.ConcurrencyConflict)
                return false;
            if (claimDeletion.Status != DocumentStoreWriteStatus.Deleted)
                throw new InvalidOperationException(
                    $"Groundwork rejected executable activity template hash claim deletion for '{templateId}' with status '{claimDeletion.Status}'.");
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return true;
    }

    private async ValueTask<DocumentStoreWriteStatus> TryCreateAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await Store.BeginAsync(
            DocumentCommitScope.Of(DocumentKind, ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind),
            cancellationToken);

        var claim = new TemplateHashClaimDocument(template.TemplateHash, template.TemplateId);
        var claimResult = await SaveAsync(
            unitOfWork,
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
            TemplateHashClaimId(template.TemplateHash),
            claim,
            expectedVersion: 0,
            cancellationToken);
        if (claimResult.Status != DocumentStoreWriteStatus.Saved)
            return claimResult.Status;

        var document = new TemplateDocument(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection,
            template.TemplateHash,
            template);
        var templateResult = await SaveAsync(
            unitOfWork,
            DocumentKind,
            template.TemplateId,
            document,
            expectedVersion: 0,
            cancellationToken);
        if (templateResult.Status != DocumentStoreWriteStatus.Saved)
            return templateResult.Status;

        await unitOfWork.CommitAsync(cancellationToken);
        return DocumentStoreWriteStatus.Saved;
    }

    private async ValueTask<DocumentStoreWriteResult> SaveAsync<TDocument>(
        IDocumentUnitOfWork unitOfWork,
        string documentKind,
        string documentId,
        TDocument document,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(documentKind, document);
        return await unitOfWork.SaveAsync(
            new SaveDocumentRequest(documentKind, documentId, schemaVersion, content, ExpectedVersion: expectedVersion),
            cancellationToken);
    }

    private void EnsureSameIdentityAndContent(ExecutableActivityTemplate existing, ExecutableActivityTemplate candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.TemplateHash, candidate.TemplateHash))
        {
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' is already bound to hash '{existing.TemplateHash}', not '{candidate.TemplateHash}'.");
        }

        var existingJson = ComparableContent(existing);
        var candidateJson = ComparableContent(candidate);
        if (!JsonNode.DeepEquals(existingJson, candidateJson))
        {
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
        }
    }

    private JsonNode? ComparableContent(ExecutableActivityTemplate template)
    {
        var json = JsonNode.Parse(Serializer.SerializeForComparison(template));
        if (json is JsonObject content)
            content.Remove("createdAt");
        return json;
    }

    private static string TemplateHashClaimId(string templateHash) =>
        $"templateHash:{templateHash.Length}:{templateHash}";

    private sealed record TemplateDocument(
        string Collection,
        string TemplateHash,
        ExecutableActivityTemplate Template);

    private sealed record TemplateHashClaimDocument(string TemplateHash, string TemplateId);
}
