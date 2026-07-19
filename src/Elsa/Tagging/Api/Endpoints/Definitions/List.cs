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

    public override async Task HandleAsync(CancellationToken cancellationToken) =>
        await Send.OkAsync(new TagDefinitionListResponse(await manager.ListAsync(new TagDefinitionListRequest(), cancellationToken)), cancellationToken);
}
