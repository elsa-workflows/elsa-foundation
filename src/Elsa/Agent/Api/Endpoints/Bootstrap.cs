using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

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
        var diagnostics = new List<Elsa.Agent.Core.Models.AgentProviderDiagnostics>();
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
            diagnostics.Select(x => x.ToResponse()).ToList(),
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
