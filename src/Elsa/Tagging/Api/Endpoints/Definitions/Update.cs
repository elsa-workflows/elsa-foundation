using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Models;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class Update(
    ITagDefinitionManager manager,
    ITagDefinitionCatalogPersistence? catalogPersistence = null)
    : ElsaEndpoint<UpdateTagDefinitionApiRequest, TagDefinitionListItem>
{
    public override void Configure()
    {
        Patch(RouteConstants.Definition("{tagDefinitionId}"));
        ConfigurePermissions(TaggingPermissions.Manage);
    }

    public override async Task HandleAsync(UpdateTagDefinitionApiRequest request, CancellationToken cancellationToken)
    {
        if (catalogPersistence is null)
        {
            ThrowError("The tag catalog is unavailable because durable persistence is not active.", 503);
            return;
        }

        var ifMatch = HttpContext.Request.Headers.IfMatch.ToString();
        if (ifMatch.Length < 3 || ifMatch[0] != '"' || ifMatch[^1] != '"')
        {
            ThrowError("A quoted If-Match revision is required.", 400);
            return;
        }
        var expectedRevision = ifMatch[1..^1];

        try
        {
            var updated = await manager.UpdateAsync(request.TagDefinitionId, request.ToCoreRequest(), expectedRevision, cancellationToken);
            HttpContext.Response.Headers.ETag = QuoteRevision(updated.Revision);
            await Send.OkAsync(TagDefinitionListItem.From(updated), cancellationToken);
        }
        catch (TagDefinitionConflictException exception)
        {
            ThrowError(exception, 409);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (InvalidOperationException exception)
        {
            ThrowError(exception, 404);
        }
    }

    private static string QuoteRevision(string revision) => '"' + revision + '"';
}
