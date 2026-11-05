using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Connections.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Api.Endpoints;

[Get("/" + SpecialistsApi.ModelProvidersRoute)]
[RequireAnyPermission(SpecialistsApi.AuthorProvider, SpecialistsApi.DesignManage, AgentSourcePermissions.Manage)]
internal sealed class ListModelProviders(IEnumerable<IConnectionTypeProvider> types, IEnumerable<ISpecialistModelCapabilitiesSource> sources) : ApiEndpointWithoutRequest<SpecialistModelProvidersResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSpecialistModelProviders";
        options.Accepts = ["*/*", "application/json"];
    }

    // Only a provider with both a connection type and exactly one capability owner can back a specialist.
    public override Task<SpecialistModelProvidersResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var owners = sources.GroupBy(x => x.ProviderKey, StringComparer.Ordinal).Where(x => x.Count() == 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var items = types.OfType<SpecialistModelConnectionTypeProvider>()
            .Where(x => owners.Contains(x.ProviderKey))
            .GroupBy(x => x.ProviderKey, StringComparer.Ordinal).Where(x => x.Count() == 1).Select(x => x.Single())
            .Select(x => new SpecialistModelProviderView(x.ProviderKey, x.Descriptor.Name, x.Descriptor.DisplayName))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProviderKey, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new SpecialistModelProvidersResponse(items));
    }
}

[Post("/" + SpecialistsApi.ModelCapabilitiesRoute)]
[RequireAnyPermission(SpecialistsApi.AuthorProvider, SpecialistsApi.DesignManage, AgentSourcePermissions.Manage)]
internal sealed class ReadModelCapabilities(IEnumerable<ISpecialistModelCapabilitiesSource> sources) : ApiEndpoint<SpecialistModelCapabilitiesRequest, SpecialistModelCapabilitiesView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSpecialistModelCapabilities";
        options.Accepts = ["application/json"];
        options.StrictTypedParsing = true;
    }

    // The same capability source publication consults, resolved in the caller's scope, so the editor offers only what publication admits.
    public override async Task<SpecialistModelCapabilitiesView> HandleAsync(SpecialistModelCapabilitiesRequest request, CancellationToken cancellationToken)
    {
        var owners = sources.Where(x => x.ProviderKey == request.ProviderKey).ToArray();
        if (owners.Length != 1 || string.IsNullOrWhiteSpace(request.ConnectionName) || string.IsNullOrWhiteSpace(request.ModelId))
            throw new SpecialistModelUnavailableException();
        try
        {
            var capabilities = await owners[0].GetCapabilitiesAsync(new SpecialistModelReference(request.ProviderKey, request.ConnectionName, request.ModelId), cancellationToken);
            return new(capabilities.SettingsSchema.Clone(), capabilities.SupportsJsonSchema, capabilities.MaximumOutputTokens);
        }
        // A schema write refusal keeps its own 409 answer; cancellation by the caller stays a cancellation.
        catch (Exception exception) when (exception is not Elsa.Primitives.Exceptions.SchemaWriteRefusedException &&
            (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            throw new SpecialistModelUnavailableException(exception);
        }
    }
}
