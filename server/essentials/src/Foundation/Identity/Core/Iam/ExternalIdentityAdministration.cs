using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>A trusted server-owned global provider document CAS-saved with an identity mutation.</summary>
public sealed class ProviderConfigurationWrite(ProviderConfigurationRecord configuration, string? expectedRevision)
{
    public ProviderConfigurationRecord Configuration { get; } = configuration ?? throw new ArgumentNullException(nameof(configuration));
    public string? ExpectedRevision { get; } = expectedRevision;
}

/// <summary>Owns a qualified link mutation and all checked IAM roots in one committed transaction.</summary>
[ReplacementContract]
public interface IExternalIdentityAdministrationStore
{
    bool SupportsIdentityRevisionGuards { get; }
    bool SupportsProviderRevisionGuards { get; }
    /// <summary>Existing-user attachment only. A provider write requires explicit privileged-global access; otherwise tenant access is required.</summary>
    ValueTask<ExternalUserAdmissionResult> AttachAsync(ExternalUserAdmission admission, IamRevisionGuard owner,
        IReadOnlyList<IamRevisionGuard> guards, ProviderConfigurationWrite? providerWrite = null, CancellationToken cancellationToken = default);
    ValueTask<ExternalIdentityRemovalResult> RemoveAsync(ExternalIdentityKey key, IamRevisionGuard owner, string expectedLinkRevision,
        IReadOnlyList<IamRevisionGuard> guards, ProviderConfigurationWrite? providerWrite = null, CancellationToken cancellationToken = default);
}
