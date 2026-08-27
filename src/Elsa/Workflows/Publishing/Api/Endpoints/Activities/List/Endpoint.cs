using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Activities.List;

/// <summary>
/// Reads the catalog (the Design seam) and projects each row to its identity + descriptor kind.
/// No construction happens here — this is the "what could I build?" half of the bridge.
/// </summary>
[Get("/publishing/activities")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(IActivityDefinitionVersionStore versions) : ApiEndpoint<ListConstructableActivities, IEnumerable<ConstructableActivityView>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "List";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<IEnumerable<ConstructableActivityView>> HandleAsync(ListConstructableActivities request, CancellationToken cancellationToken)
    {
        var rows = await versions.ListAsync(cancellationToken);

        return rows
            .Where(v => request.ConsumerKey is null || v.ConsumerKey == request.ConsumerKey)
            .Select(v => new ConstructableActivityView(
                v.Id,
                v.DefinitionId,
                v.Version,
                v.ProviderKey,
                v.ProviderSchemaVersion,
                v.ConsumerKey,
                v.ConsumerSchemaVersion))
            .ToArray();
    }
}
