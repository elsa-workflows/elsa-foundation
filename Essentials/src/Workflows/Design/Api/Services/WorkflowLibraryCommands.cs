using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// The workflow library's organization commands with their native granular authority decisions, extracted from the
/// library endpoints (spec 1193 D4). The endpoints call the request-bound methods below, which keep the endpoints' exact
/// order of checks and their exceptions; <see cref="IWorkflowLibraryCommands"/> runs the same methods for an explicit actor
/// and tenant, first applying the permission each endpoint's own route gate applies, and reports outcomes instead of
/// throwing. Only the current request's own actor and tenant are served. A replacement <see cref="IWorkflowLibraryCommands"/>
/// decides Weaver's library writes; the HTTP endpoints always keep these built-in decisions (<see cref="Http"/>).
/// </summary>
internal sealed class WorkflowLibraryCommands(IHttpContextAccessor? accessor, IWorkflowLibraryStore? store = null) : IWorkflowLibraryCommands
{
    private const string NotPermitted = "The requested workflow library operation is not permitted.";
    private const int TagPageSize = 100;
    private const string BlankKey = "An operation key must not be blank.";

    /// <summary>The library endpoints' own logic over their injected store: the HTTP routes always keep these built-in decisions.</summary>
    internal static WorkflowLibraryCommands Http(IWorkflowLibraryStore? store) => new(null, store);

    // ---- The request-bound commands the endpoints call: the endpoints' former bodies, unchanged in order and outcome. ----

    internal async Task MoveDefinitionsAsync(HttpContext http, IReadOnlyList<string> definitionIds, string? folderId, string? operationKey,
        CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        WorkflowLibraryAccess.ValidateTargets(definitionIds);
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: folderId), cancellationToken);
        foreach (var definitionId in definitionIds)
            await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(DefinitionId: definitionId), cancellationToken);
        await library.MoveDefinitionsAsync(definitionIds, folderId, actor!, operationKey ?? Guid.NewGuid().ToString("N"), cancellationToken);
    }

    internal async Task<(WorkflowOrganization Organization, WorkflowLibraryPermissions Permissions)> GetOrganizationAsync(HttpContext http,
        string definitionId, IWorkflowDefinitionStore? definitions, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        var resource = new WorkflowLibraryResource(DefinitionId: definitionId);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Read, resource, cancellationToken);
        var organization = await library.GetOrganizationAsync(definitionId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowOrganization), definitionId);
        await WorkflowLibraryAccess.RequireOrganizationReadAsync(http, organization, cancellationToken);
        var permissions = await WorkflowLibraryAccess.PermissionsAsync(http, resource, cancellationToken);
        var definition = definitions is null ? null : await definitions.FindByIdAsync(definitionId, cancellationToken);
        if (definition is null || definition.DeletedAt is not null)
            permissions = permissions with { CanAssignTags = false, CanManageFolders = false };
        return (organization, permissions);
    }

    internal async Task<WorkflowOrganizationView> ReplaceTagsAsync(HttpContext http, ReplaceWorkflowTags request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        var resource = new WorkflowLibraryResource(DefinitionId: request.DefinitionId);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Read, resource, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.AssignTags, resource, cancellationToken);
        if (await library.GetOrganizationAsync(request.DefinitionId, cancellationToken) is { } current)
            await WorkflowLibraryAccess.RequireOrganizationReadAsync(http, current, cancellationToken);
        var organization = await library.ReplaceTagsAsync(request.DefinitionId, request.Revision, request.Tags, actor!,
            request.OperationKey ?? Guid.NewGuid().ToString("N"), cancellationToken);
        await WorkflowLibraryAccess.RequireOrganizationReadAsync(http, organization, cancellationToken);
        return WorkflowOrganizationView.From(organization, await WorkflowLibraryAccess.PermissionsAsync(http, resource, cancellationToken));
    }

    internal async Task<WorkflowTagPageView> ListTagsAsync(HttpContext http, string? search, int pageSize, string? continuationToken,
        CancellationToken cancellationToken)
    {
        var page = await WorkflowLibraryAccess.RequireOrganization(store, http).ListTagsAsync(search, pageSize, continuationToken, cancellationToken);
        return new(page.Items, page.NextContinuationToken, await WorkflowLibraryAccess.PermissionsAsync(http, null, cancellationToken));
    }

    /// <summary>Creates a tag; with an operation key, through the store's keyed create, which a store without key support refuses.</summary>
    internal async Task<WorkflowTag> CreateTagAsync(HttpContext http, WorkflowTagInput input, string? operationKey, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Read, null, cancellationToken);
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        return operationKey is null
            ? await library.CreateTagAsync(input, actor!, cancellationToken)
            : await library.CreateTagAsync(input, actor!, operationKey, cancellationToken);
    }

    internal async Task<IReadOnlyList<WorkflowTagEditOutcome>> EditTagsAsync(HttpContext http, WorkflowTagBulkEdit request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        if (request.Targets is null || request.Targets.Any(target => target is null || string.IsNullOrWhiteSpace(target.Revision)))
            throw new ArgumentException("Every selected workflow requires its current organization revision.");
        WorkflowLibraryAccess.ValidateTargets(request.Targets?.Select(target => target.DefinitionId).ToArray());
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        var allowed = new List<WorkflowTagEdit>();
        var outcomes = new Dictionary<string, WorkflowTagEditOutcome>(StringComparer.Ordinal);
        foreach (var target in request.Targets!)
        {
            var resource = new WorkflowLibraryResource(DefinitionId: target.DefinitionId);
            if (await WorkflowLibraryAccess.AllowsAsync(http, WorkflowDesignPermissions.Read, resource, cancellationToken) &&
                await WorkflowLibraryAccess.AllowsAsync(http, WorkflowDesignPermissions.AssignTags, resource, cancellationToken))
                allowed.Add(target);
            else outcomes.Add(target.DefinitionId, new(target.DefinitionId, "denied", Message: "This workflow cannot be changed."));
        }
        if (allowed.Count > 0)
            foreach (var outcome in await library.EditTagsAsync(request with { Targets = allowed }, actor!, cancellationToken))
                outcomes.Add(outcome.DefinitionId, outcome);
        return request.Targets!.Select(target => outcomes[target.DefinitionId]).ToArray();
    }

    internal async Task<WorkflowLibraryViewsView> ListViewsAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        var canShare = await WorkflowLibraryAccess.AllowsAsync(http, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
        return new((await library.ListViewsAsync(actor!, cancellationToken)).Select(view => WorkflowLibraryViewView.From(view, actor!, canShare)).ToArray(), new(canShare));
    }

    /// <summary>Saves a view: a create (null id) optionally under an operation key, or an update bound by its revision.</summary>
    internal async Task<WorkflowLibraryViewView> SaveViewAsync(HttpContext http, string? viewId, WorkflowLibraryViewInput input, string? operationKey,
        CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        var actor = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
        var canShare = await WorkflowLibraryAccess.AllowsAsync(http, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
        if (input.Shared && !canShare) throw new WorkflowLibraryAuthorizationException("Sharing a workflow view requires permission.");
        var saved = operationKey is null
            ? await library.SaveViewAsync(viewId, input, actor!, canShare, cancellationToken)
            : await library.SaveViewAsync(viewId, input, actor!, canShare, operationKey, cancellationToken);
        return WorkflowLibraryViewView.From(saved, actor!, canShare);
    }

    internal async Task SetFavoriteAsync(HttpContext http, string definitionId, bool favorite, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, http);
        await WorkflowLibraryAccess.RequireAsync(http, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: definitionId), cancellationToken);
        await library.SetFavoriteAsync(definitionId, favorite, (await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken))!, cancellationToken);
    }

    // ---- The Core contract: the same decisions for an explicit actor and tenant, reported as outcomes. ----

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>> GetPermissionsAsync(WorkflowLibraryActor actor,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
        {
            var flags = await WorkflowLibraryAccess.PermissionsAsync(http, null, cancellationToken);
            return new WorkflowLibraryPermissionFlags(flags.CanAssignTags, flags.CanManageFolders, flags.CanManageTags, flags.CanShareViews);
        }, cancellationToken);

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryOrganizationSnapshot>> GetOrganizationAsync(WorkflowLibraryActor actor, string definitionId,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
        {
            var (organization, _) = await GetOrganizationAsync(http, definitionId, http.RequestServices.GetService<IWorkflowDefinitionStore>(), cancellationToken);
            return new WorkflowLibraryOrganizationSnapshot(organization.DefinitionId, organization.FolderId,
                organization.Tags.Select(tag => new WorkflowLibraryTagReference(tag.TagId, tag.ValueId)).ToArray(), organization.Revision);
        }, cancellationToken);

    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTagSnapshot>>> ListTagsAsync(WorkflowLibraryActor actor, string? search,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
            (IReadOnlyList<WorkflowLibraryTagSnapshot>)(await ListTagsAsync(http, search, TagPageSize, null, cancellationToken)).Items.Select(ToSnapshot).ToArray(),
            cancellationToken);

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> CreateTagAsync(WorkflowLibraryActor actor, WorkflowLibraryTagCreate input,
        string? operationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        // A blank key is a defect, never a silent unkeyed create; no key at all is the library's ordinary create.
        if (operationKey is not null && string.IsNullOrWhiteSpace(operationKey))
            return ValueTask.FromResult(new WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>(WorkflowLibraryCommandStatus.Invalid, BlankKey));
        var tag = new WorkflowTagInput(input.Name, input.Description, input.Mode, input.Cardinality, input.Deprecated,
            // Each value exactly as given, identity included, so the store refuses a supplied identity (or a null value) as it
            // does on its own create.
            input.Values?.Select(value => value is null ? null! : new WorkflowTagValue(value.Id ?? "", value.Name, value.Deprecated)).ToArray());
        return RunAsync(actor, WorkflowDesignPermissions.ManageTags, async http => ToSnapshot(await CreateTagAsync(http, tag, operationKey, cancellationToken)),
            cancellationToken);
    }

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> FindTagByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
        {
            var tag = await WorkflowLibraryAccess.RequireOrganization(store, http).FindTagByOperationKeyAsync(operationKey, cancellationToken);
            return tag is null ? null! : ToSnapshot(tag);
        }, cancellationToken);

    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> EditTagsAsync(WorkflowLibraryActor actor, WorkflowLibraryTagEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var request = new WorkflowTagBulkEdit(edit.Targets.Select(target => new WorkflowTagEdit(target.DefinitionId, target.Revision)).ToArray(),
            edit.Add.Select(ToAssignment).ToArray(), edit.Remove.Select(ToAssignment).ToArray(), edit.OperationKey);
        return RunAsync(actor, WorkflowDesignPermissions.AssignTags, async http =>
            (IReadOnlyList<WorkflowLibraryTargetOutcome>)(await EditTagsAsync(http, request, cancellationToken))
                .Select(outcome => new WorkflowLibraryTargetOutcome(outcome.DefinitionId, TargetStatus(outcome.Status), outcome.Revision, outcome.Message)).ToArray(),
            cancellationToken);
    }

    public async ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> SetFavoritesAsync(WorkflowLibraryActor actor,
        IReadOnlyList<string> definitionIds, bool favorite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        // Each target is its own idempotent set (as each favorite route is), so each reports its own outcome.
        return await RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
        {
            WorkflowLibraryAccess.ValidateTargets(definitionIds);
            var outcomes = new List<WorkflowLibraryTargetOutcome>();
            foreach (var definitionId in definitionIds)
            {
                var outcome = await CaptureAsync(async () => { await SetFavoriteAsync(http, definitionId, favorite, cancellationToken); return true; });
                outcomes.Add(new(definitionId, outcome.Status, Message: outcome.Message));
            }
            return (IReadOnlyList<WorkflowLibraryTargetOutcome>)outcomes;
        }, cancellationToken);
    }

    public async ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> MoveDefinitionsAsync(WorkflowLibraryActor actor,
        IReadOnlyList<string> definitionIds, string? folderId, string? operationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        if (operationKey is not null && string.IsNullOrWhiteSpace(operationKey))
            return new(WorkflowLibraryCommandStatus.Invalid, BlankKey,
                definitionIds.Select(id => new WorkflowLibraryTargetOutcome(id, WorkflowLibraryCommandStatus.Invalid, Message: BlankKey)).ToArray());
        // The store moves the set atomically: every target shares the one outcome, never a partial result.
        var result = await RunAsync(actor, WorkflowDesignPermissions.Manage, async http =>
        {
            await MoveDefinitionsAsync(http, definitionIds, folderId, operationKey, cancellationToken);
            return true;
        }, cancellationToken);
        var targets = definitionIds.Select(id => new WorkflowLibraryTargetOutcome(id, result.Status, Message: result.Message)).ToArray();
        return new(result.Status, result.Message, targets);
    }

    public ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryViewSnapshot>>> ListViewsAsync(WorkflowLibraryActor actor,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
            (IReadOnlyList<WorkflowLibraryViewSnapshot>)(await ListViewsAsync(http, cancellationToken)).Items.Select(ToSnapshot).ToArray(), cancellationToken);

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> SaveViewAsync(WorkflowLibraryActor actor, string? viewId,
        WorkflowLibraryViewSave input, string? operationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        // A create takes an optional key (a blank one is a defect); an update is bound by its revision and takes no key.
        if (operationKey is not null && (viewId is not null || string.IsNullOrWhiteSpace(operationKey)))
            return ValueTask.FromResult(new WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>(WorkflowLibraryCommandStatus.Invalid,
                viewId is null ? BlankKey : "An update of a workflow view is bound by its revision and takes no operation key."));
        var view = new WorkflowLibraryViewInput(input.Name, ToQuery(input.Query), input.Shared, input.Columns, input.Revision);
        return RunAsync(actor, WorkflowDesignPermissions.Read, async http => ToSnapshot(await SaveViewAsync(http, viewId, view, operationKey, cancellationToken)),
            cancellationToken);
    }

    public ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> FindViewByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey,
        CancellationToken cancellationToken = default) =>
        RunAsync(actor, WorkflowDesignPermissions.Read, async http =>
        {
            var library = WorkflowLibraryAccess.RequireOrganization(store, http);
            var subject = await WorkflowLibraryAccess.SubjectAsync(http, true, cancellationToken);
            var view = await library.FindViewByOperationKeyAsync(operationKey, subject!, cancellationToken);
            if (view is null) return null!;
            var canShare = await WorkflowLibraryAccess.AllowsAsync(http, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
            return ToSnapshot(WorkflowLibraryViewView.From(view, subject!, canShare));
        }, cancellationToken);

    /// <summary>
    /// Serves only the current request's own actor and tenant, applies the route gate the matching endpoint applies, and
    /// reports the endpoints' refusals as outcomes. Infrastructure failures and cancellation propagate.
    /// </summary>
    private async ValueTask<WorkflowLibraryCommandResult<T>> RunAsync<T>(WorkflowLibraryActor actor, string gate, Func<HttpContext, Task<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        var http = accessor?.HttpContext;
        if (http is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.TenantId))
            return new(WorkflowLibraryCommandStatus.Denied, NotPermitted);
        return await CaptureAsync(async () =>
        {
            // Organization first, as every organization route reports it before any actor or permission decision.
            WorkflowLibraryAccess.RequireOrganization(store, http);
            var session = await http.RequestServices.GetRequiredService<IAuthSessionService>().GetAsync(http.User, cancellationToken);
            if (!string.Equals(session.Status, "authenticated", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(session.Subject, actor.ActorId, StringComparison.Ordinal)
                || !string.Equals(session.TenantId, actor.TenantId, StringComparison.Ordinal))
                throw new WorkflowLibraryAuthorizationException("The workflow library command's actor and tenant must be the current request's own.");
            await WorkflowLibraryAccess.RequireAsync(http, gate, null, cancellationToken);
            return await body(http);
        });
    }

    private static async Task<WorkflowLibraryCommandResult<T>> CaptureAsync<T>(Func<Task<T>> body)
    {
        try { return new(WorkflowLibraryCommandStatus.Applied, null, await body()); }
        catch (WorkflowLibraryUnavailableException exception) { return new(WorkflowLibraryCommandStatus.Unavailable, exception.Message); }
        catch (WorkflowLibraryAuthorizationException exception) { return new(WorkflowLibraryCommandStatus.Denied, exception.Message); }
        catch (UnauthorizedAccessException) { return new(WorkflowLibraryCommandStatus.Denied, NotPermitted); }
        catch (WorkflowLibraryOperationKeyUnsupportedException) { return new(WorkflowLibraryCommandStatus.Unsupported, WorkflowLibraryOperationKeyUnsupportedException.FixedMessage); }
        catch (WorkflowLibraryConflictException exception) { return new(WorkflowLibraryCommandStatus.Conflict, exception.Message); }
        // The library's own not-found text names the missing item and kind, as its routes answer; it carries no cause.
        catch (EntityNotFoundException exception) { return new(WorkflowLibraryCommandStatus.NotFound, exception.Message); }
        catch (ArgumentException exception) { return new(WorkflowLibraryCommandStatus.Invalid, exception.Message); }
    }

    private static string TargetStatus(string status) => status switch
    {
        "success" => WorkflowLibraryCommandStatus.Applied,
        "denied" => WorkflowLibraryCommandStatus.Denied,
        "conflict" => WorkflowLibraryCommandStatus.Conflict,
        "not-found" => WorkflowLibraryCommandStatus.NotFound,
        _ => WorkflowLibraryCommandStatus.Invalid
    };

    private static WorkflowTagAssignment ToAssignment(WorkflowLibraryTagReference reference) => new(reference.TagId, reference.ValueId);

    private static WorkflowLibraryTagSnapshot ToSnapshot(WorkflowTag tag) => new(tag.Id, tag.Name, tag.Description, tag.Mode, tag.Cardinality,
        tag.Deprecated, tag.Revision, tag.Values.Select(value => new WorkflowLibraryTagValueSnapshot(value.Id, value.Name, value.Deprecated)).ToArray());

    private static WorkflowLibraryViewSnapshot ToSnapshot(WorkflowLibraryViewView view) => new(view.Id, view.Name, view.Shared, view.OwnerId,
        new(view.Query.SearchTerm, view.Query.State, view.Query.FolderId, view.Query.Unfiled, view.Query.IncludeDescendants,
            view.Query.TagFilters?.Select(filter => new WorkflowLibraryTagFilterSnapshot(filter.TagId, filter.Operator, filter.ValueIds)).ToArray(),
            view.Query.FavoritesOnly, view.Query.Sort, view.Query.PageSize, view.Query.PublicationState),
        view.Columns, view.Revision, view.CanEdit) { SchemaVersion = view.SchemaVersion };

    private static WorkflowLibraryQuery ToQuery(WorkflowLibraryViewQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new(query.SearchTerm, query.State, query.FolderId, query.Unfiled, query.IncludeDescendants,
            query.TagFilters?.Select(filter => new WorkflowTagFilter(filter.TagId, filter.Operator, filter.ValueIds)).ToArray(),
            query.FavoritesOnly, query.Sort, query.PageSize, null, query.PublicationState);
    }
}

/// <summary>Rejects ambiguous workflow library commands before the shell accepts requests.</summary>
/// <remarks>Captures only the configuring collection's metadata; a copied child shell rebinds it to its own collection.</remarks>
internal sealed class WorkflowLibraryCommandsStartupTask(IServiceCollection registrations) : Elsa.Tasks.Core.IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    /// <summary>Throws when the collection holds more than one command registration.</summary>
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IWorkflowLibraryCommands)) > 1)
            throw new InvalidOperationException("IWorkflowLibraryCommands has multiple registrations; select one explicit workflow library command service.");
    }
}
