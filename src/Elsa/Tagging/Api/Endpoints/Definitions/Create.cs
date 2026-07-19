using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class Create(ITagDefinitionManager manager) : ElsaEndpoint<CreateTagDefinitionApiRequest, TagDefinition>
{
    public override void Configure()
    {
        Post(RouteConstants.Definitions);
        ConfigurePermissions(TaggingPermissions.Manage);
    }

    public override async Task HandleAsync(CreateTagDefinitionApiRequest request, CancellationToken cancellationToken)
    {
        var definition = await manager.CreateAsync(request.ToCoreRequest(), cancellationToken);
        var revisioned = await manager.FindWithRevisionAsync(definition.Id, cancellationToken);
        HttpContext.Response.Headers.ETag = QuoteRevision(revisioned!.Revision);
        await Send.ResponseAsync(definition, 201, cancellationToken);
    }

    private static string QuoteRevision(string revision) => '"' + revision + '"';
}
