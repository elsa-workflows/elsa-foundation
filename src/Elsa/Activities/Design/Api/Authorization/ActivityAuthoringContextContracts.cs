using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Commands;

/// <summary>Legacy host adapter retained by the implementation assembly for one compatibility window.</summary>
[Obsolete("Use IActivityAuthoringContextAsync. This interface will be removed in the next major version.")]
public interface IActivityAuthoringContext
{
    string? TenantId { get; }

    string ActorId => AuthorizationProfile;

    string AuthorizationProfile { get; }

    bool CanAuthorProvider(string providerKey);

    bool CanReadProviderPayload(string providerKey);

    bool CanManageActivityDefinitions => false;
}

/// <summary>Asynchronous host adapter used by first-party request handlers.</summary>
[ReplacementContract]
public interface IActivityAuthoringContextAsync
{
    string? TenantId { get; }

    string ActorId { get; }

    ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default);
}
