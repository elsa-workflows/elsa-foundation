using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class Bootstrap(IAgentCapabilityCatalog capabilities, IAgentProviderRegistry providers)
    : ElsaEndpointWithoutRequest<AgentApiResponse<AgentBootstrapResponse>>
{
    public override void Configure()
    {
        Get(AgentRouteConstants.GetRoute("bootstrap"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var diagnostics = new List<Abstractions.Models.AgentProviderDiagnostics>();
        foreach (var provider in providers.Providers)
            diagnostics.Add(await provider.GetDiagnosticsAsync(ct));

        var providerStatus = diagnostics.Count == 0
            ? "unavailable"
            : diagnostics.Any(x => x.IsAvailable)
                ? "available"
                : "unavailable";

        var response = new AgentBootstrapResponse(
            Enabled: true,
            providerStatus,
            ["explain", "build", "troubleshoot"],
            (await capabilities.ListAsync(ct)).Select(x => x.ToResponse()).ToList(),
            diagnostics,
            new(true, true, "Configured by administrator"));

        await Send.OkAsync(AgentApiResponse<AgentBootstrapResponse>.Success(response), ct);
    }
}
