using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Models;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class List(ITagDefinitionManager manager) : ElsaEndpointWithoutRequest<TagDefinitionListResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.Definitions);
        ConfigurePermissions(TaggingPermissions.Read);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var permissions = HttpContext.User.FindAll("elsa.identity.permission").Select(x => x.Value).ToArray();
        var canManage = permissions.Contains("*", StringComparer.Ordinal)
                        || permissions.Contains(TaggingPermissions.Manage, StringComparer.Ordinal);
        var definitions = await manager.ListWithRevisionsAsync(
            new TagDefinitionListRequest { ActiveOnly = false },
            cancellationToken);
        var items = definitions.Select(TagDefinitionListItem.From).ToArray();

        await Send.OkAsync(new TagDefinitionListResponse(items, canManage), cancellationToken);
    }
}
