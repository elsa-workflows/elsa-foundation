using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Applies a durable destination policy to a value envelope and rewrites its external payload when
/// the existing reference does not satisfy that policy.
/// </summary>
internal sealed class RuntimeExternalEnvelopeStorage(IExternalPayloadStore? externalPayloadStore)
{
    public async ValueTask<ValueEnvelope> RewriteAsync(
        RuntimeExternalEnvelopeRewriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PayloadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ValueRole);
        ArgumentNullException.ThrowIfNull(request.Value);
        ArgumentNullException.ThrowIfNull(request.SourcePolicy);
        ArgumentNullException.ThrowIfNull(request.EffectivePolicy);

        if (request.Value.Presence != ValuePresence.Present ||
            !request.ForceExternal && request.EffectivePolicy.Storage == DurableValueStorage.Inline)
            return request.Value;
        if (request.EffectivePolicy.Storage == DurableValueStorage.Custom)
            throw new InvalidOperationException($"VF-ACT-005: {request.ValueRole} requires custom storage, but no canonical custom value-storage seam is configured.");

        var storageProfile = ResolveStorageProfile(
            request.EffectivePolicy,
            request.FallbackStorageProfile ?? request.Value.ExternalReference?.StorageProfile,
            request.ValueRole);
        if (!request.ForceExternal &&
            request.Value.ExternalReference is { } existing &&
            StringComparer.Ordinal.Equals(existing.StorageProfile, storageProfile) &&
            request.SourcePolicy.Satisfies(request.EffectivePolicy))
            return request.Value;
        if (externalPayloadStore is null)
            throw new InvalidOperationException($"VF-ACT-005: {request.ValueRole} requires an IExternalPayloadStore.");

        var payload = request.Value.InlineValue ?? await externalPayloadStore.ReadAsync(request.Value.ExternalReference!, cancellationToken);
        var reference = await externalPayloadStore.WriteAsync(
            new ExternalPayloadWriteRequest(
                request.WorkflowExecutionId,
                request.PayloadId,
                storageProfile,
                request.Value.Type,
                payload.Clone(),
                request.EffectivePolicy),
            cancellationToken);
        if (reference is null)
            throw new InvalidOperationException($"VF-ACT-005: External payload storage returned no reference for {request.ValueRole}.");
        if (string.IsNullOrWhiteSpace(reference.Locator))
            throw new InvalidOperationException($"VF-ACT-005: External payload storage returned a blank locator for {request.ValueRole}.");
        if (!StringComparer.Ordinal.Equals(reference.StorageProfile, storageProfile))
        {
            throw new InvalidOperationException(
                $"VF-ACT-005: External payload storage returned storage profile '{reference.StorageProfile}' for {request.ValueRole}, " +
                $"but effective policy requires '{storageProfile}'.");
        }

        return ValueEnvelope.External(request.Value.Type, reference, request.EffectivePolicy);
    }

    private static string ResolveStorageProfile(
        ValueProtectionPolicy effectivePolicy,
        string? fallbackStorageProfile,
        string valueRole) =>
        effectivePolicy.Metadata.TryGetValue(ValuePolicyCombiner.StorageProfileMetadataKey, out var profile) && !string.IsNullOrWhiteSpace(profile)
            ? profile
            : !string.IsNullOrWhiteSpace(fallbackStorageProfile)
                ? fallbackStorageProfile
                : throw new InvalidOperationException($"VF-ACT-005: External {valueRole} has no storage profile.");
}

internal readonly record struct RuntimeExternalEnvelopeRewriteRequest(
    string WorkflowExecutionId,
    string PayloadId,
    string ValueRole,
    ValueEnvelope Value,
    ValueProtectionPolicy SourcePolicy,
    ValueProtectionPolicy EffectivePolicy,
    string? FallbackStorageProfile = null,
    bool ForceExternal = false);
