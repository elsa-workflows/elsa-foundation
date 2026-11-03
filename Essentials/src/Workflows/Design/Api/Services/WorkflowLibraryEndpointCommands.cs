using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// How a library endpoint with a Core counterpart reaches the installed <see cref="IWorkflowLibraryCommands"/> (spec 1193 D4:
/// one decision for the endpoints and Weaver). With the built-in implementation installed, the endpoint runs its
/// request-bound methods, so routes, checks and bodies are exactly as before. With a replacement installed, the endpoint
/// calls the Core contract for the request's own actor and tenant and renders its outcomes with this module's exceptions:
/// denied 403, unavailable 501, unsupported 501, conflict 409, not found 404, invalid 400. Endpoints without a Core
/// counterpart (folder hierarchy, tag updates, tag pages, organization reads and full tag replacement, view deletion) keep
/// the built-in logic.
/// </summary>
internal static class WorkflowLibraryEndpointCommands
{
    internal static IWorkflowLibraryCommands Installed(HttpContext http) => http.RequestServices.GetRequiredService<IWorkflowLibraryCommands>();

    internal static async Task<WorkflowTag> CreateTagAsync(HttpContext http, IWorkflowLibraryCommands commands, WorkflowTagInput input, string? operationKey,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, cancellationToken);
        // The whole HTTP input, and the key exactly as sent: no key is the library's ordinary create, as on the built-in route.
        var tag = Unwrap(await commands.CreateTagAsync(actor, new(input.Name, input.Description, input.Mode, input.Cardinality,
            input.Values?.Select(value => new WorkflowLibraryTagValueCreate(value.Name, value.Deprecated)).ToArray(), input.Deprecated), operationKey, cancellationToken));
        return new(tag.Id, tag.Name, tag.Description, tag.Mode, tag.Cardinality, tag.Deprecated, tag.Revision,
            tag.Values.Select(value => new WorkflowTagValue(value.Id, value.Name, value.Deprecated)).ToArray());
    }

    internal static async Task<IReadOnlyList<WorkflowTagEditOutcome>> EditTagsAsync(HttpContext http, IWorkflowLibraryCommands commands, WorkflowTagBulkEdit request,
        CancellationToken cancellationToken)
    {
        if (request.Targets is null || request.Targets.Any(target => target is null || string.IsNullOrWhiteSpace(target.Revision)))
            throw new ArgumentException("Every selected workflow requires its current organization revision.");
        // The same target validation as the built-in route, before any actor or command work.
        WorkflowLibraryAccess.ValidateTargets(request.Targets.Select(target => target.DefinitionId).ToArray());
        var actor = await ActorAsync(http, cancellationToken);
        var reported = Unwrap(await commands.EditTagsAsync(actor, new(request.Targets.Select(target => new WorkflowLibraryRevisionTarget(target.DefinitionId, target.Revision)).ToArray(),
            (request.Add ?? []).Select(Reference).ToArray(), (request.Remove ?? []).Select(Reference).ToArray(), request.OperationKey), cancellationToken));
        return InRequestOrder(request.Targets.Select(target => target.DefinitionId).ToArray(), reported).Select(outcome => new WorkflowTagEditOutcome(outcome.DefinitionId, outcome.Status switch
        {
            WorkflowLibraryCommandStatus.Applied => "success",
            WorkflowLibraryCommandStatus.Denied => "denied",
            WorkflowLibraryCommandStatus.Conflict => "conflict",
            WorkflowLibraryCommandStatus.NotFound => "not-found",
            _ => "invalid"
        }, outcome.Revision, outcome.Message)).ToArray();
    }

    internal static async Task SetFavoriteAsync(HttpContext http, IWorkflowLibraryCommands commands, string definitionId, bool favorite, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, cancellationToken);
        RequireApplied(InRequestOrder([definitionId], Unwrap(await commands.SetFavoritesAsync(actor, [definitionId], favorite, cancellationToken))));
    }

    internal static async Task MoveDefinitionsAsync(HttpContext http, IWorkflowLibraryCommands commands, IReadOnlyList<string> definitionIds, string? folderId,
        string? operationKey, CancellationToken cancellationToken)
    {
        WorkflowLibraryAccess.ValidateTargets(definitionIds);
        var actor = await ActorAsync(http, cancellationToken);
        // The move's own outcome and then each target's, the same way favorites are read: any target not applied is the route's refusal.
        RequireApplied(InRequestOrder(definitionIds, Unwrap(await commands.MoveDefinitionsAsync(actor, definitionIds, folderId, operationKey, cancellationToken))));
    }

    internal static async Task<WorkflowLibraryViewsView> ListViewsAsync(HttpContext http, IWorkflowLibraryCommands commands, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, cancellationToken);
        var views = Unwrap(await commands.ListViewsAsync(actor, cancellationToken));
        var flags = Unwrap(await commands.GetPermissionsAsync(actor, cancellationToken));
        return new(views.Select(ToView).ToArray(), new(flags.CanShareViews));
    }

    internal static async Task<WorkflowLibraryViewView> SaveViewAsync(HttpContext http, IWorkflowLibraryCommands commands, string? viewId, WorkflowLibraryViewInput input,
        string? operationKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input.Query);
        var actor = await ActorAsync(http, cancellationToken);
        var query = input.Query;
        var save = new WorkflowLibraryViewSave(input.Name, new(query.SearchTerm, query.State, query.FolderId, query.Unfiled, query.IncludeDescendants,
            query.TagFilters?.Select(filter => new WorkflowLibraryTagFilterSnapshot(filter.TagId, filter.Operator, filter.ValueIds)).ToArray(),
            query.FavoritesOnly, query.Sort, query.PageSize, query.PublicationState), input.Shared, input.Columns, input.Revision);
        // The key exactly as sent (none for an update, which is bound by its revision).
        return ToView(Unwrap(await commands.SaveViewAsync(actor, viewId, save, operationKey, cancellationToken)));
    }

    /// <summary>The request's own authenticated subject and tenant, as every library route requires them.</summary>
    private static async ValueTask<WorkflowLibraryActor> ActorAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var session = await http.RequestServices.GetRequiredService<IAuthSessionService>().GetAsync(http.User, cancellationToken);
        if (!string.Equals(session.Status, "authenticated", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(session.TenantId) || string.IsNullOrWhiteSpace(session.Subject))
            throw new WorkflowLibraryAuthorizationException("An authenticated subject and tenant are required for this operation.");
        return new(session.Subject, session.TenantId);
    }

    private static T Unwrap<T>(WorkflowLibraryCommandResult<T> result) => result.Status switch
    {
        WorkflowLibraryCommandStatus.Applied when result.Value is not null => result.Value,
        WorkflowLibraryCommandStatus.Applied => throw new InvalidOperationException("The installed workflow library commands applied a change without reporting it."),
        WorkflowLibraryCommandStatus.Denied => throw new WorkflowLibraryAuthorizationException(result.Message ?? "The requested workflow library operation is not permitted."),
        WorkflowLibraryCommandStatus.Unavailable => throw new WorkflowLibraryUnavailableException(result.Message ?? "Workflow library organization is not supported by this backend."),
        WorkflowLibraryCommandStatus.Unsupported => throw new WorkflowLibraryOperationKeyUnsupportedException(),
        WorkflowLibraryCommandStatus.Conflict => throw new WorkflowLibraryConflictException(result.Message ?? "This item changed. Reload it before applying your changes."),
        WorkflowLibraryCommandStatus.NotFound => throw new EntityNotFoundException(result.Message ?? "The workflow library item is unavailable."),
        _ => throw new ArgumentException(result.Message ?? "The workflow library refused the request.")
    };

    /// <summary>Exactly one outcome per requested target, in request order; anything else is the installed service's defect.</summary>
    private static IReadOnlyList<WorkflowLibraryTargetOutcome> InRequestOrder(IReadOnlyList<string> requested, IReadOnlyList<WorkflowLibraryTargetOutcome> reported)
    {
        var byTarget = reported.GroupBy(outcome => outcome.DefinitionId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (reported.Count != requested.Count || requested.Any(id => !byTarget.TryGetValue(id, out var outcomes) || outcomes.Length != 1))
            throw new InvalidOperationException("The installed workflow library commands did not report exactly one outcome per requested target.");
        return requested.Select(id => byTarget[id][0]).ToArray();
    }

    /// <summary>The route's refusal for the first target that was not applied, as its status maps to this module's errors.</summary>
    private static void RequireApplied(IReadOnlyList<WorkflowLibraryTargetOutcome> outcomes)
    {
        if (outcomes.FirstOrDefault(outcome => outcome.Status != WorkflowLibraryCommandStatus.Applied) is { } refused)
            Unwrap(new WorkflowLibraryCommandResult<bool>(refused.Status, refused.Message, true));
    }

    private static WorkflowLibraryTagReference Reference(WorkflowTagAssignment assignment) => new(assignment.TagId, assignment.ValueId);

    private static WorkflowLibraryViewView ToView(WorkflowLibraryViewSnapshot view) => new(view.Id, view.Name, view.Shared, view.OwnerId,
        new(view.Query.SearchTerm, view.Query.State, view.Query.FolderId, view.Query.Unfiled, view.Query.IncludeDescendants,
            view.Query.TagFilters?.Select(filter => new WorkflowTagFilter(filter.TagId, filter.Operator, filter.ValueIds)).ToArray(),
            view.Query.FavoritesOnly, view.Query.Sort, view.Query.PageSize, null, view.Query.PublicationState),
        view.Columns, view.Revision, view.SchemaVersion, view.CanEdit, view.CanEdit);
}
