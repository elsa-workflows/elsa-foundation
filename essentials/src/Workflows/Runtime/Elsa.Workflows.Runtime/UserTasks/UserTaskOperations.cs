using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class UserTaskOperations(IUserTaskStore store, IUserTaskAccess access,
    IEnumerable<IUserTaskFormProvider> providers, TimeProvider time, IUserTaskRuntime runtime,
    UserTaskCursorCodec cursors, Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextAccessor persistence) : IUserTaskOperations
{
    public async ValueTask<UserTaskPage> ListAsync(string view, int limit = 25, string? after = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (view is not ("mine" or "queue" or "history" or "all") || limit is < 1 or > 100)
            throw Failure("validation", "Choose a valid inbox and page size (1–100).");
        if (view == "all" && !actor.CanManage) throw Failure("forbidden", "Task management permission is required.");
        var visible = new List<UserTaskRecord>();
        var cursorContext = JsonSerializer.Serialize(new { Scope = persistence.Current.RequireScope().Value, actor.TenantId, actor.UserId, View = view, Search = search });
        string? next = cursors.Decode(after, cursorContext);
        // Bound policy evaluation as well as provider work. Reauthorize each row before disclosure.
        for (var pageIndex = 0; pageIndex < 4; pageIndex++)
        {
            var page = await store.QueryAsync(new(actor.TenantId, view == "all" ? null : actor.UserId, actor.GroupIds, view, limit - visible.Count, next, search), cancellationToken);
            foreach (var task in page.Items)
            {
                try { await access.AuthorizeAsync(task, "read", cancellationToken); visible.Add(task); }
                catch (UserTaskException exception) when (exception.Code is "missing" or "forbidden") { }
            }
            next = page.Next;
            if (visible.Count == limit || next is null) break;
        }
        return new(visible, cursors.Encode(next, cursorContext));
    }

    public async ValueTask<UserTaskRecord> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await ReadAsync(id, actor, cancellationToken);
    }

    public async ValueTask<UserTaskRecord> ClaimAsync(string id, UserTaskMutation request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var task = await ReadAsync(id, actor, cancellationToken);
        RequireAct(actor); await access.AuthorizeAsync(task, "act", cancellationToken); RequireRevision(task, request.Revision, request.RequestId);
        if (task.Status != UserTaskStatus.Open || task.GroupId is null || task.ClaimantId is not null || !actor.GroupIds.Contains(task.GroupId))
            throw Failure("conflict", "This task is no longer available to claim.");
        return await SaveAsync(task, task with { ClaimantId = actor.UserId, Status = UserTaskStatus.Claimed }, "claimed", actor.UserId, cancellationToken);
    }

    public async ValueTask<UserTaskRecord> ReleaseAsync(string id, UserTaskMutation request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var task = await ReadAsync(id, actor, cancellationToken);
        RequireAct(actor); await access.AuthorizeAsync(task, "act", cancellationToken); RequireRevision(task, request.Revision, request.RequestId);
        if (task.Status != UserTaskStatus.Claimed || task.ClaimantId != actor.UserId || task.GroupId is null || !actor.GroupIds.Contains(task.GroupId))
            throw Failure("conflict", "Only the current eligible claimant can release this task.");
        return await SaveAsync(task, task with { ClaimantId = null, Status = UserTaskStatus.Open }, "released", actor.UserId, cancellationToken);
    }

    public async ValueTask<UserTaskRecord> ReassignAsync(string id, UserTaskAssignment request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (!actor.CanManage) throw Failure("forbidden", "Task management permission is required.");
        var task = await ReadAsync(id, actor, cancellationToken);
        await access.AuthorizeAsync(task, "manage", cancellationToken);
        RequireRevision(task, request.Revision, request.RequestId); RequireOpen(task);
        await access.ValidateAssignmentAsync(actor.TenantId, request.AssigneeId, request.GroupId, cancellationToken);
        return await SaveAsync(task, task with { AssigneeId = request.AssigneeId, GroupId = request.GroupId, ClaimantId = null,
            Status = UserTaskStatus.Open }, "reassigned", actor.UserId, cancellationToken);
    }

    public async ValueTask<UserTaskRecord> CompleteAsync(string id, UserTaskCompletion request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var task = await ReadAsync(id, actor, cancellationToken);
        RequireAct(actor);
        await access.AuthorizeAsync(task, "act", cancellationToken);
        if (task.Decision is { } accepted && accepted.RequestId == request.RequestId)
        {
            if (accepted.ActorId != actor.UserId || accepted.Response.Action != request.Action || !Elsa.Serialization.Core.JsonCanonicalForm.Equals(accepted.Response.Data, request.Data))
                throw Failure("conflict", "This request ID already names a different response.");
            return task;
        }
        RequireRevision(task, request.Revision, request.RequestId); RequireOpen(task);
        if (task.AssigneeId != actor.UserId && !(task.Status == UserTaskStatus.Claimed && task.ClaimantId == actor.UserId &&
            task.GroupId is { } group && actor.GroupIds.Contains(group)))
            throw Failure("forbidden", "Claim the task or ask its assignee to complete it.");
        if (!task.Actions.Contains(request.Action, StringComparer.Ordinal)) throw Failure("validation", "Choose one of this task's actions.");
        if (request.Data.ValueKind != JsonValueKind.Object) throw Failure("validation", "The response must be an object.");
        if (task.Form is { } form)
        {
            var errors = await Provider(form.Provider).ValidateResponseAsync(form, task.Prefill, request.Data,
                !task.OptionalFormActions.Contains(request.Action, StringComparer.Ordinal), cancellationToken);
            if (errors.Count != 0) throw new UserTaskException("validation", "Correct the highlighted form values.", errors);
        }
        else if (request.Data.EnumerateObject().Any()) throw Failure("validation", "This task does not accept form fields.");
        var decision = new UserTaskDecision(request.RequestId, actor.UserId,
            new(task.Id, Guid.NewGuid().ToString("N"), request.Action, request.Data.Clone()), time.GetUtcNow());
        return await SaveAsync(task, task with { Status = UserTaskStatus.CompletionPending, Decision = decision }, "accepted", actor.UserId, cancellationToken);
    }

    public async ValueTask<UserTaskFormPage> ListFormsAsync(int limit = 25, string? after = null, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        if (!actor.CanReadForms && !actor.CanManageForms) throw Failure("forbidden", "Form read permission is required.");
        if (limit is < 1 or > 100) throw Failure("validation", "Choose a form page size between 1 and 100.");
        return await store.ListFormsAsync(actor.TenantId, limit, after, cancellationToken);
    }

    public async ValueTask<UserTaskFormPage> ListFormVersionsAsync(string id, int limit = 25, string? after = null, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormRead(actor);
        if (limit is < 1 or > 100) throw Failure("validation", "Choose a form page size between 1 and 100.");
        return await store.ListFormVersionsAsync(actor.TenantId, id, limit, after, cancellationToken);
    }

    public async ValueTask<UserTaskFormVersion> GetFormAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        if (!actor.CanReadForms && !actor.CanManageForms) throw Failure("forbidden", "Form read permission is required.");
        return await store.FindFormAsync(actor.TenantId, id, version, cancellationToken) ?? throw Failure("missing", "Form version not found.");
    }

    public async ValueTask<UserTaskFormVersion> CreateFormAsync(UserTaskForm form, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        if (!actor.CanManageForms) throw Failure("forbidden", "Form management permission is required.");
        if (form is null) throw Failure("validation", "A form definition is required.");
        if (form.Version == UserTaskProtocol.FormDraftVersion) throw Failure("validation", "That form version is reserved for draft storage.");
        if ((await store.FindFormDefinitionAsync(actor.TenantId, form.Id, cancellationToken))?.Deleted == true)
            throw Failure("conflict", "This form has been deleted and its identifier cannot be reused.");
        Provider(form.Provider).ValidateDefinition(form);
        var version = new UserTaskFormVersion(actor.TenantId, form, time.GetUtcNow(), actor.UserId);
        if (!await store.TryCreateFormAsync(version, cancellationToken)) throw Failure("conflict", "That immutable form version already exists.");
        return version;
    }

    public async ValueTask<UserTaskFormDefinitionPage> ListFormDefinitionsAsync(int limit = 25, string? after = null, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormRead(actor);
        if (limit is < 1 or > 100) throw Failure("validation", "Choose a form page size between 1 and 100.");
        return await store.ListFormDefinitionsAsync(actor.TenantId, limit, after, cancellationToken);
    }

    public async ValueTask<UserTaskFormDefinition> GetFormDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormRead(actor);
        var definition = await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken);
        return definition is { Deleted: false } && (definition.LatestPublished is not null || definition.Draft is not null)
            ? definition : throw Failure("missing", "Form not found.");
    }

    public async ValueTask<UserTaskFormDefinition> SaveFormDraftAsync(string id, UserTaskForm form, long revision, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormManage(actor);
        ValidateDraftStructure(id, form);
        Provider(form.Provider);
        var current = await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken);
        if (current?.Deleted == true) throw Failure("conflict", "This form has been deleted and cannot be changed.");
        var expectedRevision = revision;
        var draftForm = form with { Version = current?.LatestPublished?.Form.Version ?? "1" };
        var draft = new UserTaskFormDraft(actor.TenantId, id, draftForm, expectedRevision + 1, time.GetUtcNow(), actor.UserId);
        if (!await store.TryWriteFormDraftAsync(draft, expectedRevision, cancellationToken)) throw Failure("conflict", "This form changed. Refresh the draft before saving again.");
        return await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken) ?? throw Failure("unavailable", "The saved form could not be read back.");
    }

    public async ValueTask<UserTaskFormDefinition> DiscardFormDraftAsync(string id, long revision, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormManage(actor);
        var current = await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken);
        if (current?.Draft is null) throw Failure("conflict", "There is no saved draft to discard.");
        var state = new UserTaskFormDraft(actor.TenantId, id, null, revision + 1, time.GetUtcNow(), actor.UserId);
        if (!await store.TryWriteFormDraftAsync(state, revision, cancellationToken)) throw Failure("conflict", "This form changed. Refresh the draft before discarding it.");
        return await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken) ?? throw Failure("unavailable", "The form could not be read back.");
    }

    public async ValueTask<UserTaskFormDefinition> DeleteFormAsync(string id, long revision, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormManage(actor);
        var current = await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken);
        if (current is null || current.Deleted) throw Failure("missing", "Form not found.");
        if (current.Revision != revision) throw Failure("conflict", "This form changed. Refresh the list before deleting it.");
        var tombstone = new UserTaskFormDraft(actor.TenantId, id, null, revision + 1, time.GetUtcNow(), actor.UserId) { Deleted = true };
        if (!await store.TryWriteFormDraftAsync(tombstone, revision, cancellationToken))
            throw Failure("conflict", "This form changed while it was being deleted. Refresh and try again.");
        return await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken) ?? throw Failure("unavailable", "The deleted form could not be read back.");
    }

    public async ValueTask<UserTaskFormVersion> PublishFormAsync(string id, UserTaskForm form, long revision, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        RequireFormManage(actor);
        if (form is null || form.Id != id) throw Failure("validation", "The form identity does not match the requested form.");
        var provider = Provider(form.Provider);
        var definition = await store.FindFormDefinitionAsync(actor.TenantId, id, cancellationToken);
        if (definition?.Deleted == true) throw Failure("conflict", "This form has been deleted and cannot be published.");
        if ((definition?.Revision ?? 0) != revision) throw Failure("conflict", "This form changed. Refresh the draft before publishing.");
        var latestVersion = definition?.LatestPublished?.Form.Version;
        var hasNumericVersion = int.TryParse(latestVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var currentVersion);
        var nextVersion = !hasNumericVersion ? "1" : currentVersion < int.MaxValue ? (currentVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        if (nextVersion is null) throw Failure("conflict", "The next form version cannot be allocated safely.");
        var publishForm = form with { Version = nextVersion };
        provider.ValidateDefinition(publishForm);
        var now = time.GetUtcNow();
        var publication = new UserTaskFormVersion(actor.TenantId, publishForm, now, actor.UserId);
        var tombstone = new UserTaskFormDraft(actor.TenantId, id, null, revision + 1, now, actor.UserId);
        if (!await store.TryPublishFormAsync(publication, tombstone, revision, cancellationToken)) throw Failure("conflict", "This form changed while it was being published. Refresh and try again.");
        return publication;
    }

    private static void ValidateDraftStructure(string id, UserTaskForm? form)
    {
        if (form is null || string.IsNullOrWhiteSpace(id) || id.Length > 256 || form.Id != id || string.IsNullOrWhiteSpace(form.Provider) ||
            form.Name is null || form.Name.Length > 256 || form.Description is { Length: > 2000 } || form.Fields is null || form.Fields.Count > 100 || form.Fields.Any(field => field is null ||
                field.Key is null || field.Key.Length > 256 || field.Label is null || field.Label.Length > 256 || field.Kind is null || field.Kind.Length > 64 ||
                field.ControlId is { Length: > 128 } || field.ControlId is not null && string.IsNullOrWhiteSpace(field.ControlId) ||
                field.Configuration is { } configuration && (configuration.ValueKind != JsonValueKind.Object || configuration.GetRawText().Length > 16_384 || JsonDepth(configuration, 0) > 16) ||
                field.Help is { Length: > 2000 } || field.Choices is { Count: > 100 } || field.Choices?.Any(choice => choice is null || choice.Length > 256) == true ||
                field.Presentation is { Display.Length: > 32 } || field.Presentation is { TrueLabel.Length: > 256 } ||
                field.Presentation is { FalseLabel.Length: > 256 } || field.Presentation is { UnansweredLabel.Length: > 256 }))
            throw Failure("validation", "The draft exceeds the supported form structure or size limits.");
    }

    private static int JsonDepth(JsonElement value, int depth) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(property => JsonDepth(property.Value, depth + 1)).DefaultIfEmpty(depth).Max(),
        JsonValueKind.Array => value.EnumerateArray().Select(item => JsonDepth(item, depth + 1)).DefaultIfEmpty(depth).Max(),
        _ => depth
    };

    private static void RequireFormRead(UserTaskActor actor)
    {
        if (!actor.CanReadForms && !actor.CanManageForms) throw Failure("forbidden", "Form read permission is required.");
    }

    private static void RequireFormManage(UserTaskActor actor)
    {
        if (!actor.CanManageForms) throw Failure("forbidden", "Form management permission is required.");
    }

    private IUserTaskFormProvider Provider(string name)
    {
        var matches = providers.Where(x => x.Provider == name).ToArray();
        return matches.Length == 1 ? matches[0] : throw Failure("unavailable", "The form integration is unavailable or ambiguous.");
    }

    private async ValueTask<UserTaskActor> ActorAsync(CancellationToken ct)
    {
        var actor = await access.GetActorAsync(ct);
        if (!actor.CanRead && !actor.CanManage) throw Failure("forbidden", "Task read permission is required.");
        return actor;
    }

    private async ValueTask<UserTaskRecord> ReadAsync(string id, UserTaskActor actor, CancellationToken ct)
    {
        var task = await store.FindAsync(actor.TenantId, id, ct);
        if (task is null || task.Status == UserTaskStatus.Staged || !UserTaskRules.CanSee(task, actor.UserId, actor.GroupIds, actor.CanManage))
            throw Failure("missing", "Task not found.");
        await access.AuthorizeAsync(task, "read", ct);
        try { await runtime.ReconcileAsync(task, ct); }
        catch (Exception exception) when (task.Status == UserTaskStatus.CompletionPending &&
            !ct.IsCancellationRequested && (UserTaskAvailability.IsUnavailable(exception, ct) ||
                exception is Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException))
        {
            // Acceptance already owns durable delivery responsibility. Re-read and reauthorize its receipt below.
        }
        task = await store.FindAsync(actor.TenantId, id, ct);
        if (task is null || !UserTaskRules.CanSee(task, actor.UserId, actor.GroupIds, actor.CanManage)) throw Failure("missing", "Task not found.");
        await access.AuthorizeAsync(task, "read", ct);
        return task;
    }

    private async ValueTask<UserTaskRecord> SaveAsync(UserTaskRecord before, UserTaskRecord next, string operation, string actor, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        next = next with { Revision = before.Revision + 1, UpdatedAt = now,
            History = before.History.Append(new UserTaskHistory(operation, actor, now, next.Decision?.Response.Action, next.AssigneeId, next.GroupId)).ToArray() };
        if (!await store.TryWriteAsync(next, before.Revision, ct)) throw Failure("conflict", "This task changed. Refresh and try again.");
        return next;
    }

    private static void RequireAct(UserTaskActor actor) { if (!actor.CanAct) throw Failure("forbidden", "Task action permission is required."); }
    private static void RequireRevision(UserTaskRecord task, long revision, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128) throw Failure("validation", "A stable request ID is required (maximum 128 characters).");
        if (task.Revision != revision) throw Failure("conflict", "This task changed. Refresh and try again.");
    }
    private static void RequireOpen(UserTaskRecord task)
    {
        if (task.Status is not (UserTaskStatus.Open or UserTaskStatus.Claimed)) throw Failure("conflict", "This task no longer accepts changes.");
    }
    private static UserTaskException Failure(string code, string message) => new(code, message);
}
