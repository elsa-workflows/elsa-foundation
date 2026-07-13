using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(IDocumentStore store) : ISecretRepository
{
    public async ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        var envelope = await store.LoadAsync(
            SecretsStorageManifest.SecretDocumentKind,
            SecretDocumentId.From(tenantId, normalizedName),
            cancellationToken);

        if (envelope is null)
            return null;

        var secret = SecretDocumentSerializer.Map(envelope);
        if (!string.Equals(secret.TenantId, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document tenant does not match its storage identity.");
        return secret;
    }

    public async ValueTask<IReadOnlyCollection<Secret>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                SecretsStorageManifest.SecretDocumentKind,
                SecretsStorageManifest.ByTenantIndex,
                tenantId),
            cancellationToken);

        return envelopes.Select(SecretDocumentSerializer.Map).ToArray();
    }

    public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        Validate(secret);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                SecretsStorageManifest.SecretDocumentKind,
                SecretDocumentId.From(secret.TenantId, secret.Name),
                SecretsStorageManifest.SchemaVersion,
                SecretDocumentSerializer.Serialize(secret),
                ExpectedVersion: 0),
            cancellationToken);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => true,
            DocumentStoreWriteStatus.ConcurrencyConflict => false,
            _ => throw new InvalidOperationException($"Unexpected secret create result '{result.Status}'.")
        };
    }

    public async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Validate(secret);

        var content = SecretDocumentSerializer.Serialize(secret);

        await store.SaveAsync(
            new SaveDocumentRequest(
                SecretsStorageManifest.SecretDocumentKind,
                SecretDocumentId.From(secret.TenantId, secret.Name),
                SecretsStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static void Validate(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateTenantId(secret.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.Name);
    }

    private static void ValidateTenantId(string tenantId) => ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
}

internal static class SecretDocumentId
{
    public static string From(string tenantId, string normalizedName) =>
        $"{tenantId.Length}:{tenantId}{normalizedName}";
}

internal static class SecretDocumentSerializer
{
    public static string Serialize(Secret secret) =>
        JsonSerializer.Serialize(
            new SecretDocument(SecretsStorageManifest.SecretCollection, secret.TenantId, secret),
            SecretsGroundworkJson.Options);

    public static Secret Map(DocumentEnvelope envelope)
    {
        var document = Deserialize(envelope);
        if (!string.IsNullOrWhiteSpace(document.Secret.TenantId)
            && !string.IsNullOrWhiteSpace(document.TenantId)
            && !string.Equals(document.Secret.TenantId, document.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret document contains conflicting tenant identities.");
        if (string.IsNullOrWhiteSpace(document.Secret.TenantId) && !string.IsNullOrWhiteSpace(document.TenantId))
            document.Secret.TenantId = document.TenantId;
        return document.Secret;
    }

    public static SecretDocument Deserialize(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<SecretDocument>(envelope.ContentJson, SecretsGroundworkJson.Options)
        ?? throw new InvalidOperationException("Secret document content is invalid.");
}

internal sealed record SecretDocument(string Collection, string? TenantId, Secret Secret);
