using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Activities.Design.Api.Commands;

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
