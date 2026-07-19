using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class Update(ITagDefinitionManager manager) : ElsaEndpoint<UpdateTagDefinitionApiRequest, TagDefinition>
{
    public override void Configure()
    {
        Patch(RouteConstants.Definition("{tagDefinitionId}"));
        ConfigurePermissions(TaggingPermissions.Manage);
    }

    public override async Task HandleAsync(UpdateTagDefinitionApiRequest request, CancellationToken cancellationToken)
    {
        var expectedRevision = HttpContext.Request.Headers.IfMatch.ToString().Trim('"');
        if (string.IsNullOrWhiteSpace(expectedRevision))
        {
            ThrowError("A quoted If-Match revision is required.", 400);
            return;
        }

        try
        {
            var updated = await manager.UpdateAsync(request.TagDefinitionId, request.ToCoreRequest(), expectedRevision, cancellationToken);
            HttpContext.Response.Headers.ETag = QuoteRevision(updated.Revision);
            await Send.OkAsync(updated.Definition, cancellationToken);
        }
        catch (TagDefinitionConflictException exception)
        {
            ThrowError(exception, 409);
        }
        catch (InvalidOperationException exception)
        {
            ThrowError(exception, 404);
        }
    }

    private static string QuoteRevision(string revision) => '"' + revision + '"';
}
