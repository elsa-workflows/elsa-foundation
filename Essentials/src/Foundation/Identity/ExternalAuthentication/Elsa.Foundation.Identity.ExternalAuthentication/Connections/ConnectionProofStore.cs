using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal sealed record ConnectionProof(string RecordId, string MaterialRevision, string RedirectUri, string Issuer, string Subject,
    string UserId, string CredentialBinding, DateTimeOffset ObservedAt)
{
    public string? AuthenticationSessionId { get; init; }
    public string? SecurityVersion { get; init; }
    public string? SessionProvider { get; init; }
    public string? ExternalLinkRevision { get; init; }
}

/// <summary>Private, protected successful-login observations; deliberately separate from authentication material.</summary>
internal sealed class ConnectionProofStore(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<ExternalAuthenticationOptions> options)
{
    private const string Kind = "external-recovery-proofs/v1";
    private const string Setting = "protectedProofs";
    internal const int MaximumProofs = 16;
    private const int MaximumCharacters = 131072;

    public async ValueTask<IReadOnlyList<ConnectionProof>> ReadAsync(string key, CancellationToken token)
    {
        await using var scope = CreateScope(false);
        var store = scope.ServiceProvider.GetService<IRevisionAwareProviderConfigurationStore>();
        if (store is null) return [];
        var found = await store.FindGlobalWithRevisionAsync(Provider(key), token);
        return found is null ? [] : Read(found.Record, key);
    }

    public async ValueTask RecordAsync(string key, ConnectionProof proof, DateTimeOffset oldest, CancellationToken token)
    {
        if (!Valid(proof) || proof.AuthenticationSessionId is null || proof.SecurityVersion is null || proof.SessionProvider is null || proof.ExternalLinkRevision is null)
            throw new InvalidOperationException("Invalid recovery observation.");
        await using var scope = CreateScope(true);
        var store = scope.ServiceProvider.GetService<IRevisionAwareProviderConfigurationStore>();
        if (store is null) return; // Normal sign-in also works without database management/persistence.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await store.FindGlobalWithRevisionAsync(Provider(key), token);
            var previous = current is null ? [] : Read(current.Record, key);
            var merged = previous.Where(item => item.ObservedAt >= oldest &&
                !(item.RecordId == proof.RecordId && item.UserId == proof.UserId && item.Issuer == proof.Issuer && item.Subject == proof.Subject))
                .Append(proof).OrderByDescending(item => item.ObservedAt).ThenBy(item => item.UserId, StringComparer.Ordinal)
                .Take(MaximumProofs).ToArray();
            var payload = Protector(key).Protect(JsonSerializer.Serialize(new Envelope(1, options.Value.TenantId, key, merged)));
            if (payload.Length > MaximumCharacters) throw new InvalidOperationException("Recovery observations exceed their storage bound.");
            var record = new ProviderConfigurationRecord(Provider(key), null, Kind, true, false, ProviderCapabilities.FoundationReference,
                new Dictionary<string, string> { [Setting] = payload });
            var saved = await store.SaveWithRevisionAsync(record, current?.Revision, token);
            if (saved.Status == IamRevisionSaveStatus.Saved) return;
        }
        throw new InvalidOperationException("Recovery observation changed concurrently.");
    }

    private IReadOnlyList<ConnectionProof> Read(ProviderConfigurationRecord record, string key)
    {
        if (record.Provider != Provider(key) || record.TenantId is not null || record.Kind != Kind || !record.Enabled ||
            record.Settings.Count != 1 || !record.Settings.TryGetValue(Setting, out var payload) || string.IsNullOrEmpty(payload) || payload.Length > MaximumCharacters)
            throw new InvalidOperationException("Recovery observations are invalid or unsupported.");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(Protector(key).Unprotect(payload));
            if (envelope is null || envelope.Format != 1 || envelope.Tenant != options.Value.TenantId || envelope.Key != key ||
                envelope.Proofs is null || envelope.Proofs.Length > MaximumProofs || envelope.Proofs.Any(proof => proof is null || !Valid(proof)))
                throw new InvalidOperationException("Recovery observations are invalid or unsupported.");
            return envelope.Proofs;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException)
        { throw new InvalidOperationException("Recovery observations cannot be read."); }
    }

    private IDataProtector Protector(string key) => protection.CreateProtector("Elsa.ExternalAuthentication.Recovery.v1", options.Value.TenantId, key);
    private string Provider(string key)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4]; Span<byte> unit = stackalloc byte[2];
        foreach (var value in new[] { options.Value.TenantId, key })
        {
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length);
            foreach (var character in value) { BinaryPrimitives.WriteUInt16BigEndian(unit, character); hash.AppendData(unit); }
        }
        return "external-recovery:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private AsyncServiceScope CreateScope(bool write)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(write
            ? PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("external-connection-recovery-evidence"))
            : PersistenceAccessContext.Global);
        return scope;
    }
    private static bool Valid(ConnectionProof proof) => Text(proof.RecordId, 128) && Text(proof.MaterialRevision, 128) && Text(proof.Issuer, 8192) &&
        Text(proof.RedirectUri, 8192) && Text(proof.Subject, 8192) && Text(proof.UserId, 400) && Text(proof.CredentialBinding, 512) && proof.ObservedAt != default &&
        (proof.AuthenticationSessionId is null && proof.SecurityVersion is null && proof.SessionProvider is null ||
            Hex(proof.AuthenticationSessionId) && Text(proof.SecurityVersion, 400) && Text(proof.SessionProvider, 400)) &&
        (proof.ExternalLinkRevision is null || Text(proof.ExternalLinkRevision, 128));
    private static bool Hex(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    private sealed record Envelope(int Format, string Tenant, string Key, ConnectionProof[] Proofs);
}
