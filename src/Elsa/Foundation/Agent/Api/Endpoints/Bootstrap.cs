using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class Bootstrap(IAgentCapabilityCatalog capabilities, IAgentProviderRegistry providers, IAgentPolicyEvaluator policyEvaluator)
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
        var activePolicy = AgentPolicy.Default;
        var availability = await policyEvaluator.EvaluateAvailabilityAsync(activePolicy, ct);
        var listedCapabilities = (await capabilities.ListAsync(ct)).ToList();
        var enabled = availability.Allowed && diagnostics.Any(x => x.IsAvailable);
        var modes = enabled ? BuildModes(listedCapabilities) : [];

        var response = new AgentBootstrapResponse(
            enabled,
            providerStatus,
            modes,
            listedCapabilities.Select(x => x.ToResponse()).ToList(),
            diagnostics,
            new(activePolicy.ContextVisibility, activePolicy.RequireProposalApproval, activePolicy.RetentionLabel));

        await Send.OkAsync(AgentApiResponse<AgentBootstrapResponse>.Success(response), ct);
    }

    private static IReadOnlyCollection<string> BuildModes(IReadOnlyCollection<AgentCapability> capabilities)
    {
        var modes = new List<string>();
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.explain", StringComparison.OrdinalIgnoreCase)))
            modes.Add("explain");
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.troubleshoot", StringComparison.OrdinalIgnoreCase)))
            modes.Add("troubleshoot");
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.propose-change", StringComparison.OrdinalIgnoreCase)))
            modes.Add("build");

        return modes;
    }
}
