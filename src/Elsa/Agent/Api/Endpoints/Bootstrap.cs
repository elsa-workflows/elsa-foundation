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
        // A single agent harness is active at a time (enforced by the provider registry).
        var diagnostics = providers.Active is null ? null : await providers.Active.GetDiagnosticsAsync(ct);
        var providerStatus = diagnostics?.IsAvailable == true ? "available" : "unavailable";
        var activePolicy = AgentPolicy.Default;
        var availability = await policyEvaluator.EvaluateAvailabilityAsync(activePolicy, ct);
        var listedCapabilities = (await capabilities.ListAsync(ct)).ToList();
        var enabled = availability.Allowed && diagnostics?.IsAvailable == true;
        var modes = enabled ? BuildModes(listedCapabilities) : [];

        var response = new AgentBootstrapResponse(
            enabled,
            providerStatus,
            modes,
            listedCapabilities.Select(x => x.ToResponse()).ToList(),
            diagnostics?.ToResponse(),
            new(
                activePolicy.ContextVisibility,
                activePolicy.AutonomyMode.ToContractString(),
                activePolicy.MaxAutonomyMode.ToContractString(),
                activePolicy.AllowedAutonomyModes.Select(x => x.ToContractString()).ToList(),
                activePolicy.RetentionLabel));

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
