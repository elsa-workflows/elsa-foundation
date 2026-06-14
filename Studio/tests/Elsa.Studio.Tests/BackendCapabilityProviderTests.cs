using System.Net;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Services;
using Elsa.Studio.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests;

public sealed class BackendCapabilityProviderTests
{
    [Fact]
    public async Task GetCapabilityIdsAsync_MergesConfiguredLocalAndRemoteCapabilities()
    {
        var options = Options.Create(new StudioApiOptions
        {
            BackendCapabilitiesUrl = "https://backend/default/_elsa/capabilities"
        });
        options.Value.BackendCapabilityIds.Add("configured");

        var provider = new BackendCapabilityProvider(
            [new ContributeLocalCapability()],
            new StubHttpClientFactory("""{"capabilities":[{"id":"remote"}]}"""),
            NullLogger<BackendCapabilityProvider>.Instance,
            options);

        var capabilityIds = await provider.GetCapabilityIdsAsync(CancellationToken.None);

        Assert.Contains("configured", capabilityIds);
        Assert.Contains("local", capabilityIds);
        Assert.Contains("remote", capabilityIds);
    }

    [Fact]
    public async Task GetCapabilityIdsAsync_ContinuesWhenRemoteCapabilitiesFail()
    {
        var options = Options.Create(new StudioApiOptions
        {
            BackendCapabilitiesUrl = "https://backend/default/_elsa/capabilities"
        });
        options.Value.BackendCapabilityIds.Add("configured");

        var provider = new BackendCapabilityProvider(
            [],
            new StubHttpClientFactory("not-json"),
            NullLogger<BackendCapabilityProvider>.Instance,
            options);

        var capabilityIds = await provider.GetCapabilityIdsAsync(CancellationToken.None);

        Assert.Contains("configured", capabilityIds);
    }

    private sealed class ContributeLocalCapability : IStudioEventHandler<OnBackendCapabilitiesCollecting>
    {
        public Task Handle(OnBackendCapabilitiesCollecting @event, CancellationToken cancellationToken)
        {
            @event.CapabilityIds.Add("local");
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpClientFactory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler(json));
    }

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };

            return Task.FromResult(response);
        }
    }
}
