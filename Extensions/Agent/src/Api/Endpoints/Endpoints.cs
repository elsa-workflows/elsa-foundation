using Elsa.Agent.Connections;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Connections.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Agent.Api.Endpoints;

[Get("/" + AgentApi.ModelProvidersRoute)]
[RequireAnyPermission(AgentApi.AuthorProvider, AgentApi.DesignManage, AgentSourcePermissions.Manage)]
internal sealed class ListModelProviders(IEnumerable<IConnectionTypeProvider> types, IEnumerable<IAgentModelCapabilitiesSource> sources) : ApiEndpointWithoutRequest<AgentModelProvidersResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentModelProviders";
        options.Accepts = ["*/*", "application/json"];
    }

    // Only a provider with both a connection type and exactly one capability owner can back an agent.
    public override Task<AgentModelProvidersResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var owners = sources.GroupBy(x => x.ProviderKey, StringComparer.Ordinal).Where(x => x.Count() == 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var items = types.OfType<AgentModelConnectionTypeProvider>()
            .Where(x => owners.Contains(x.ProviderKey))
            .GroupBy(x => x.ProviderKey, StringComparer.Ordinal).Where(x => x.Count() == 1).Select(x => x.Single())
            .Select(x => new AgentModelProviderView(x.ProviderKey, x.Descriptor.Name, x.Descriptor.DisplayName))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProviderKey, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new AgentModelProvidersResponse(items));
    }
}

[Post("/" + AgentApi.ModelCapabilitiesRoute)]
[RequireAnyPermission(AgentApi.AuthorProvider, AgentApi.DesignManage, AgentSourcePermissions.Manage)]
internal sealed class ReadModelCapabilities(IEnumerable<IAgentModelCapabilitiesSource> sources) : ApiEndpoint<AgentModelCapabilitiesRequest, AgentModelCapabilitiesView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentModelCapabilities";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
    }

    // The same capability source publication consults, resolved in the caller's scope, so the editor offers only what publication admits.
    public override async Task<AgentModelCapabilitiesView> HandleAsync(AgentModelCapabilitiesRequest request, CancellationToken cancellationToken)
    {
        var owners = sources.Where(x => x.ProviderKey == request.ProviderKey).ToArray();
        if (owners.Length != 1 || string.IsNullOrWhiteSpace(request.ConnectionName) || string.IsNullOrWhiteSpace(request.ModelId))
            throw new AgentModelUnavailableException();
        try
        {
            var capabilities = await owners[0].GetCapabilitiesAsync(new AgentModelReference(request.ProviderKey, request.ConnectionName, request.ModelId), cancellationToken);
            return new(capabilities.SettingsSchema.Clone(), capabilities.SupportsJsonSchema, capabilities.MaximumOutputTokens);
        }
        // A schema write refusal keeps its own 409 answer; cancellation by the caller stays a cancellation.
        catch (Exception exception) when (exception is not Elsa.Primitives.Exceptions.SchemaWriteRefusedException &&
            (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            throw new AgentModelUnavailableException(exception);
        }
    }
}
