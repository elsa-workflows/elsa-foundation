using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Models;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class Create(
    ITagDefinitionManager manager,
    ITagDefinitionCatalogPersistence? catalogPersistence = null)
    : ElsaEndpoint<CreateTagDefinitionApiRequest, TagDefinitionListItem>
{
    public override void Configure()
    {
        Post(RouteConstants.Definitions);
        ConfigurePermissions(TaggingPermissions.Manage);
    }

    public override async Task HandleAsync(CreateTagDefinitionApiRequest request, CancellationToken cancellationToken)
    {
        if (catalogPersistence is null)
        {
            ThrowError("The tag catalog is unavailable because durable persistence is not active.", 503);
            return;
        }

        try
        {
            var definition = await manager.CreateAsync(request.ToCoreRequest(), cancellationToken);
            var revisioned = await manager.FindWithRevisionAsync(definition.Id, cancellationToken);
            HttpContext.Response.Headers.ETag = QuoteRevision(revisioned!.Revision);
            await Send.ResponseAsync(TagDefinitionListItem.From(revisioned), 201, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (InvalidOperationException exception)
        {
            ThrowError(exception, 409);
        }
    }

    private static string QuoteRevision(string revision) => '"' + revision + '"';
}
