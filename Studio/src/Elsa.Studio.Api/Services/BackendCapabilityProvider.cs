using System.Net.Http.Json;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Events;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Api.Services;

public sealed class BackendCapabilityProvider(
    IEnumerable<IStudioEventHandler<OnBackendCapabilitiesCollecting>> handlers,
    IHttpClientFactory httpClientFactory,
    IOptions<StudioApiOptions> options) : IBackendCapabilityProvider
{
    public async Task<IReadOnlySet<string>> GetCapabilityIdsAsync(CancellationToken cancellationToken)
    {
        var capabilityIds = new HashSet<string>(options.Value.BackendCapabilityIds, StringComparer.OrdinalIgnoreCase);
        var collection = new OnBackendCapabilitiesCollecting();

        foreach (var handler in handlers)
            await handler.Handle(collection, cancellationToken);

        foreach (var capabilityId in collection.CapabilityIds)
            capabilityIds.Add(capabilityId);

        if (!string.IsNullOrWhiteSpace(options.Value.BackendCapabilitiesUrl))
            await AddRemoteCapabilities(capabilityIds, options.Value.BackendCapabilitiesUrl, cancellationToken);

        return capabilityIds;
    }

    private async Task AddRemoteCapabilities(ISet<string> capabilityIds, string capabilitiesUrl, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(BackendCapabilityProvider));
        var response = await client.GetFromJsonAsync<BackendCapabilitiesResponse>(capabilitiesUrl, cancellationToken);

        foreach (var capability in response?.Capabilities ?? [])
            capabilityIds.Add(capability.Id);
    }
}
