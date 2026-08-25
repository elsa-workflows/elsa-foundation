using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Endpoints.IncidentStrategies.List;

/// <summary>
/// Projects descriptor-only incident-strategy catalog data for publishing clients. The catalog owns
/// registration and default validation; this endpoint intentionally never resolves strategy
/// implementations from the service provider.
/// </summary>
[Get("/publishing/incident-strategies")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IIncidentStrategyCatalog catalog) : ApiEndpoint<ListIncidentStrategies, IncidentStrategiesResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "ListIncidentStrategies";

    public override Task<IncidentStrategiesResponse> HandleAsync(ListIncidentStrategies request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = catalog.List()
            .Select(descriptor => new IncidentStrategyDescriptorView(
                descriptor.Reference.Alias,
                descriptor.Reference.Version,
                descriptor.DisplayName,
                descriptor.Description))
            .OrderBy(descriptor => descriptor.Alias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Alias, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Version, StringComparer.Ordinal)
            .ToArray();
        var defaultStrategy = catalog.DefaultStrategy;

        return Task.FromResult(new IncidentStrategiesResponse(
            items,
            new IncidentStrategyReferenceView(defaultStrategy.Alias, defaultStrategy.Version)));
    }
}
