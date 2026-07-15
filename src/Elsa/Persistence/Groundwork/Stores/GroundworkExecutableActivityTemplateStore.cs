using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed immutable store for content-addressed reusable-activity execution templates.
/// </summary>
public sealed class GroundworkExecutableActivityTemplateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind),
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

            var document = new TemplateDocument(
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection,
                template.TemplateHash,
                template);
            var result = await SaveDocumentAsync(template.TemplateId, document, cancellationToken, expectedVersion: 0);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return;

            // Another store instance may have won the create-only write after our initial reads. It is
            // idempotent only when that winner persisted the exact same immutable identity and content.
            var winner = await FindAsync(template.TemplateId, cancellationToken)
                         ?? throw new InvalidOperationException(
                             $"Template '{template.TemplateId}' could not be created and no winning document was found.");
            EnsureSameIdentityAndContent(winner, template);
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
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateByHash,
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

    private sealed record TemplateDocument(
        string Collection,
        string TemplateHash,
        ExecutableActivityTemplate Template);
}
