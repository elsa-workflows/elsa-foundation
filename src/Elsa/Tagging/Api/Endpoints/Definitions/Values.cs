using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Tagging.Api.Constants;
using Elsa.Tagging.Api.Models;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;

namespace Elsa.Tagging.Api.Endpoints.Definitions;

internal sealed class CreateValue(
    IControlledTagValueManager manager,
    ITagDefinitionCatalogPersistence? catalogPersistence = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null,
    IControlledTagValueAtomicChangeStore? atomicChanges = null)
    : ElsaEndpoint<CreateControlledTagValueApiRequest, ControlledTagValueListItem>
{
    public override void Configure() { Post(RouteConstants.Values("{tagDefinitionId}")); ConfigurePermissions(TaggingPermissions.Manage); }
    public override async Task HandleAsync(CreateControlledTagValueApiRequest request, CancellationToken cancellationToken)
    {
        if (catalogPersistence is null || controlledValuePersistence is null || atomicChanges is null) { ThrowError("The tag catalog is unavailable because atomic durable persistence is not active.", 503); return; }
        try
        {
            var value = await manager.CreateAsync(request.ToCoreRequest(), cancellationToken);
            var record = (await manager.FindWithRevisionAsync(value.Id, cancellationToken))!;
            HttpContext.Response.Headers.ETag = Quote(record.Revision);
            await Send.ResponseAsync(ControlledTagValueListItem.From(record), 201, cancellationToken);
        }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (InvalidOperationException exception) { ThrowError(exception, 409); }
    }
    private static string Quote(string revision) => '"' + revision + '"';
}

internal sealed class ListValues(
    IControlledTagValueManager manager,
    ITagDefinitionCatalogPersistence? catalogPersistence = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null)
    : ElsaEndpoint<ControlledTagValuesListRequest, ControlledTagValueListResponse>
{
    public override void Configure() { Get(RouteConstants.Values("{tagDefinitionId}")); ConfigurePermissions(TaggingPermissions.Read); }
    public override async Task HandleAsync(ControlledTagValuesListRequest request, CancellationToken cancellationToken)
    {
        if (catalogPersistence is null || controlledValuePersistence is null) { ThrowError("The controlled-value catalog is unavailable because durable persistence is not active.", 503); return; }
        try
        {
            var values = await manager.ListWithRevisionsAsync(new() { TagDefinitionId = request.TagDefinitionId, ActiveOnly = request.ActiveOnly }, cancellationToken);
            var permissions = HttpContext.User.FindAll("elsa.identity.permission").Select(x => x.Value).ToArray();
            var canManage = permissions.Contains("*", StringComparer.Ordinal)
                            || permissions.Contains(TaggingPermissions.Manage, StringComparer.Ordinal);
            await Send.OkAsync(
                new ControlledTagValueListResponse(
                    values.Select(ControlledTagValueListItem.From).ToArray(),
                    canManage),
                cancellationToken);
        }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
    }
}

internal sealed class UpdateValue(
    IControlledTagValueManager manager,
    ITagDefinitionCatalogPersistence? catalogPersistence = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null,
    IControlledTagValueAtomicChangeStore? atomicChanges = null)
    : ElsaEndpoint<UpdateControlledTagValueApiRequest, ControlledTagValueListItem>
{
    public override void Configure() { Patch(RouteConstants.Value("{tagDefinitionId}", "{controlledTagValueId}")); ConfigurePermissions(TaggingPermissions.Manage); }
    public override async Task HandleAsync(UpdateControlledTagValueApiRequest request, CancellationToken cancellationToken)
    {
        if (catalogPersistence is null || controlledValuePersistence is null || atomicChanges is null) { ThrowError("The tag catalog is unavailable because atomic durable persistence is not active.", 503); return; }
        var ifMatch = HttpContext.Request.Headers.IfMatch.ToString();
        if (ifMatch.Length < 3 || ifMatch[0] != '"' || ifMatch[^1] != '"') { ThrowError("A quoted If-Match revision is required.", 400); return; }
        try
        {
            var existing = await manager.FindWithRevisionAsync(request.ControlledTagValueId, cancellationToken);
            if (existing is null || !StringComparer.Ordinal.Equals(existing.Value.TagDefinitionId, request.TagDefinitionId))
            {
                ThrowError("The controlled tag value is not available for this tag definition.", 400);
                return;
            }
            var record = await manager.UpdateAsync(request.ControlledTagValueId, request.ToCoreRequest(), ifMatch[1..^1], cancellationToken);
            HttpContext.Response.Headers.ETag = Quote(record.Revision);
            await Send.OkAsync(ControlledTagValueListItem.From(record), cancellationToken);
        }
        catch (TagDefinitionConflictException exception) { ThrowError(exception, 409); }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (InvalidOperationException exception) { ThrowError(exception, 404); }
    }
    private static string Quote(string revision) => '"' + revision + '"';
}

public sealed class ControlledTagValuesListRequest
{
    public string TagDefinitionId { get; set; } = "";
    public bool ActiveOnly { get; set; } = true;
}
