using System.Text;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Persistence.Core.Stores;

/// <summary>Finite limits applied to actual stored text before a provider materializes publication JSON.</summary>
public sealed class ActivityPublicationSnapshotLimits
{
    /// <summary>Combined original JSON-column byte budget for one copied owner publication.</summary>
    public int MaxJsonBytes { get; init; } = 32 * 1024 * 1024;
    public int MaxIdentityBytes { get; init; } = 4096;
    public void Validate()
    {
        if (MaxJsonBytes is < 1 or > 64 * 1024 * 1024 || MaxIdentityBytes is < 1 or > 16 * 1024)
            throw new ArgumentOutOfRangeException(nameof(ActivityPublicationSnapshotLimits));
    }
}

/// <summary>One consistent copied owner row; Contract and Provider contain their original stored UTF8 JSON.</summary>
/// <remarks>The selected persistence partition remains independent of this row's nullable tenant. No compiler runs.
/// Copied bytes remain valid after the provider's read snapshot ends. Unknown JSON fields are never projected away.</remarks>
public sealed class ActivityPublicationSnapshot
{
    private readonly byte[] _contract;
    private readonly byte[] _provider;
    public ActivityPublicationSnapshot(string definitionId, string versionId, string version, string activityTypeKey,
        ActivityDefinitionVersionResolutionKind resolutionKind, string templateId, string templateHash, string sourceReferenceId,
        string providerFingerprint, ActivityDefinitionVersionLifecycle lifecycle, string? tenantId, ReadOnlyMemory<byte> contract,
        ReadOnlyMemory<byte> provider, ActivityPublicationSnapshotLimits limits, string? sourceDraftId = null, ActivityPublicationSnapshotDetails? details = null,
        int storedPayloadBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        var utf8 = new UTF8Encoding(false, true);
        try
        {
            foreach (var value in new[] { definitionId, versionId, version, activityTypeKey, templateId, templateHash, sourceReferenceId, providerFingerprint })
                if (string.IsNullOrWhiteSpace(value) || utf8.GetByteCount(value) > limits.MaxIdentityBytes)
                    throw new ActivityPublicationSnapshotException("activity_publication_identity_limit");
            foreach (var optional in new[] { tenantId, sourceDraftId })
                if (optional is not null && utf8.GetByteCount(optional) > limits.MaxIdentityBytes)
                    throw new ActivityPublicationSnapshotException("activity_publication_identity_limit");
            if (!Enum.IsDefined(resolutionKind) || !Enum.IsDefined(lifecycle) || contract.Length is 0 || provider.Length is 0 ||
                (long)contract.Length + provider.Length + (details?.JsonByteLength ?? 0) > limits.MaxJsonBytes)
                throw new ActivityPublicationSnapshotException("activity_publication_snapshot_invalid");
            _ = utf8.GetCharCount(contract.Span); _ = utf8.GetCharCount(provider.Span);
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException)
        { throw new ActivityPublicationSnapshotException("activity_publication_snapshot_unicode_invalid"); }
        DefinitionId = definitionId; VersionId = versionId; Version = version; ActivityTypeKey = activityTypeKey; ResolutionKind = resolutionKind;
        TemplateId = templateId; TemplateHash = templateHash; SourceReferenceId = sourceReferenceId; ProviderFingerprint = providerFingerprint;
        if (storedPayloadBytes < 0) throw new ActivityPublicationSnapshotException("activity_publication_projection_invalid");
        Lifecycle = lifecycle; TenantId = tenantId; SourceDraftId = sourceDraftId; Details = details; StoredPayloadBytes = storedPayloadBytes; _contract = contract.ToArray(); _provider = provider.ToArray();
    }
    public string DefinitionId { get; }
    public string VersionId { get; }
    public string Version { get; }
    public string ActivityTypeKey { get; }
    public ActivityDefinitionVersionResolutionKind ResolutionKind { get; }
    public string TemplateId { get; }
    public string TemplateHash { get; }
    public string SourceReferenceId { get; }
    public string ProviderFingerprint { get; }
    public ActivityDefinitionVersionLifecycle Lifecycle { get; }
    public string? TenantId { get; }
    public string? SourceDraftId { get; }
    public ActivityPublicationSnapshotDetails? Details { get; }
    public int StoredPayloadBytes { get; }
    public byte[] GetContractBytes() => _contract.ToArray();
    public byte[] GetProviderBytes() => _provider.ToArray();
}

/// <summary>Original publication projection facts; JSON fragments are retained before any typed owner projection.</summary>
public sealed class ActivityPublicationSnapshotDetails
{
    private readonly byte[] _requirements;
    private readonly byte[] _measurements;
    public ActivityPublicationSnapshotDetails(string publicationId, string? sourceVersionId, int directDependencyCount, int closedTemplateCount,
        int resumeTargetCount, string publishedAt, ReadOnlyMemory<byte> runtimeRequirements, ReadOnlyMemory<byte> resourceMeasurements,
        ActivityPublicationSnapshotLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate(); var utf8 = new UTF8Encoding(false, true);
        try
        {
            if (string.IsNullOrWhiteSpace(publicationId) || utf8.GetByteCount(publicationId) > limits.MaxIdentityBytes ||
                sourceVersionId is not null && utf8.GetByteCount(sourceVersionId) > limits.MaxIdentityBytes ||
                string.IsNullOrWhiteSpace(publishedAt) || utf8.GetByteCount(publishedAt) > limits.MaxIdentityBytes ||
                directDependencyCount < 0 || closedTemplateCount < 0 || resumeTargetCount < 0 ||
                runtimeRequirements.Length is 0 || resourceMeasurements.Length is 0 || runtimeRequirements.Length > limits.MaxJsonBytes || resourceMeasurements.Length > limits.MaxJsonBytes)
                throw new ActivityPublicationSnapshotException("activity_publication_projection_invalid");
            _ = utf8.GetCharCount(runtimeRequirements.Span); _ = utf8.GetCharCount(resourceMeasurements.Span);
        }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException)
        { throw new ActivityPublicationSnapshotException("activity_publication_snapshot_unicode_invalid"); }
        PublicationId = publicationId; SourceVersionId = sourceVersionId; DirectDependencyCount = directDependencyCount; ClosedTemplateCount = closedTemplateCount;
        ResumeTargetCount = resumeTargetCount; PublishedAt = publishedAt; _requirements = runtimeRequirements.ToArray(); _measurements = resourceMeasurements.ToArray();
    }
    public string PublicationId { get; }
    public string? SourceVersionId { get; }
    public int DirectDependencyCount { get; }
    public int ClosedTemplateCount { get; }
    public int ResumeTargetCount { get; }
    public string PublishedAt { get; }
    public byte[] GetRuntimeRequirementsBytes() => _requirements.ToArray();
    public byte[] GetResourceMeasurementsBytes() => _measurements.ToArray();
    internal long JsonByteLength => (long)_requirements.Length + _measurements.Length;
}

/// <summary>Replacement owner read contract for an exact immutable publication, including original JSON fragments.</summary>
/// <remarks>Before loading text, bound actual stored types and UTF8 lengths in one consistent physical snapshot.
/// Return null only for absence in the selected owner's scope; missing qualification, ambiguity and corrupt data refuse.
/// A read-only host supplies its authorized persistence context. Selector strings never grant access.</remarks>
public interface IActivityPublicationSnapshotReader
{
    /// <summary>Qualifies the actual selected source accessor independently of the physical nullable owner tenant.</summary>
    ValueTask<ActivityPublicationSnapshot?> FindAsync(string versionId, string expectedPartition, ActivityPublicationSnapshotLimits limits,
        CancellationToken cancellationToken = default) => ValueTask.FromException<ActivityPublicationSnapshot?>(new ActivityPublicationSnapshotException("activity_publication_snapshot_scope_profile_unsupported", true));
}

/// <summary>Stable value-free snapshot refusal. Provider failures retain the owning Design persistence boundary.</summary>
public sealed class ActivityPublicationSnapshotException(string code, bool unsupported = false) : Exception("Activity publication snapshot was refused (" + code + ").")
{
    public string Code { get; } = code;
    public bool Unsupported { get; } = unsupported;
}
