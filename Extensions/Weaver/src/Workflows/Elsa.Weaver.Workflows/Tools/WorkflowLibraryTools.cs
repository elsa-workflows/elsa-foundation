using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Weaver.Workflows.Tools;

/// <summary>
/// The workflow library's reviewed server writes (spec 1193 FR-021, FR-022, FR-025): each goes through
/// <see cref="IWorkflowLibraryCommands"/> under the library's native granular authority for the session's own actor and
/// tenant, returns <c>RequiresApproval</c> from preparation so review holds in every autonomy mode (full-auto included),
/// binds each target by its current organization revision where the library offers one, and reports one outcome per target.
/// A result where only some targets changed is reported as partial, never as applied. Deletion is never offered.
/// </summary>
internal abstract class WorkflowLibraryServerTool(IEnumerable<IWorkflowLibraryCommands>? commands) : IWeaverTool, IWeaverToolAuthorization
{
    internal const string ResultType = "workflows.library-result";
    internal const string RendererId = "weaver.workflows.library-result";
    /// <summary>A target the change never reached because an earlier step of it (a tag creation) failed.</summary>
    internal const string NotApplied = "not-applied";
    protected static readonly JsonSerializerOptions JsonOptions = WorkflowLibraryContext.JsonOptions;

    public abstract WeaverToolDescriptor Descriptor { get; }

    /// <summary>A descriptor of this family: mutating, reviewed, server-executed, disclosed by the library context source.</summary>
    protected static WeaverToolDescriptor Describe(string name, string displayName, string description, string schema, string permission) =>
        new(name, displayName, description, WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired, schema, [permission], ["/workflows"])
        {
            ModuleId = WorkflowLibraryContext.ModuleId, ContractVersion = "1", ExecutionLocation = WeaverExecutionLocation.Server,
            ResultType = ResultType, ResultSchemaVersion = "1", ResultRendererId = RendererId, DisclosureSourceId = WorkflowLibraryContext.ContextSourceId
        };

    // The host decides the route permission; the library commands make every granular decision at preparation and again
    // at execution. With no (or several) installed commands the tool is unavailable, never a guess.
    public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkflowLibraryContext.Commands(commands, out var reason) is null)
            return ValueTask.FromResult(new WeaverPolicyDecision(false, [new("weaver.workflow.library_unavailable", reason!) { OwnerUnavailable = true }]));
        if (target is not null && target is not { ModuleId: WorkflowLibraryContext.ModuleId, ResourceType: WorkflowLibraryContext.LibraryResourceType })
            return ValueTask.FromResult(new WeaverPolicyDecision(false, [new("weaver.workflow.resource_required", "The workflow library list is this tool's only target.")]));
        return ValueTask.FromResult(new WeaverPolicyDecision(true, []));
    }

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        if (WorkflowLibraryContext.Commands(commands, out var reason) is not { } library)
            return Failure("weaver.workflow.library_unavailable", reason!, 503);
        var contexts = invocation.Context.Where(item => item.ContentType == WorkflowLibraryContext.LibraryKind && item.Origin == WeaverContextOrigin.ClientSnapshot).ToArray();
        if (contexts.Length != 1 || !WorkflowLibraryContext.TryRead(contexts[0], out var list, out _))
            return Failure("weaver.workflow.library_context_required", "One admitted workflow library list is required; nothing was changed.", 409);
        if (invocation.Client is not null && invocation.Client != list.Client)
            return Failure("weaver.workflow.client_mismatch", "The library list belongs to another page; nothing was changed.", 409);
        var actor = new WorkflowLibraryActor(invocation.ActorId, invocation.TenantId);
        // The library's own current answer, never the list's copy: organization disabled is unavailable with its reason.
        var permissions = await library.GetPermissionsAsync(actor, cancellationToken);
        if (!permissions.Succeeded || permissions.Value is null) return Outcome(permissions.Status, permissions.Message);
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("weaver.workflow.library_arguments_invalid", "The arguments must be JSON; nothing was changed.", 400); }
        return await PrepareCoreAsync(library, actor, permissions.Value, list, arguments, cancellationToken);
    }

    public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        if (WorkflowLibraryContext.Commands(commands, out var reason) is not { } library)
            return WeaverResult<WeaverToolResult>.Failure("weaver.workflow.library_unavailable", reason!, 503);
        if (invocation.Target is not { ModuleId: WorkflowLibraryContext.ModuleId, ResourceType: WorkflowLibraryContext.LibraryResourceType } target)
            return WeaverResult<WeaverToolResult>.Failure("weaver.workflow.prepared_invalid", "The prepared library change is invalid; nothing was changed.", 409);
        var (status, summary, data, targets) = await ExecuteCoreAsync(library, new(invocation.ActorId, invocation.TenantId), invocation.Arguments, cancellationToken);
        if (status == "refused")
            return WeaverResult<WeaverToolResult>.Failure("weaver.workflow.library_refused", summary, 409);
        var payload = new WeaverPayload(ResultType, "1", data);
        var after = new WeaverResourceTarget(target.ModuleId, target.ResourceType, target.ResourceId, Digest(targets));
        return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, summary, payload)
        { Receipt = new(invocation.InvocationId, target, after, WeaverEffectScope.ServerPersisted, payload) });
    }

    protected abstract Task<WeaverResult<WeaverToolPreparation>> PrepareCoreAsync(IWorkflowLibraryCommands library, WorkflowLibraryActor actor,
        WorkflowLibraryPermissionFlags permissions, WorkflowLibraryContext.Admitted list, JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>Runs the frozen change: <c>refused</c> when nothing changed, otherwise <c>applied</c> or <c>partial</c> with per-target outcomes.</summary>
    protected abstract Task<(string Status, string Summary, JsonElement Data, IReadOnlyList<WorkflowLibraryTargetOutcome> Targets)> ExecuteCoreAsync(
        IWorkflowLibraryCommands library, WorkflowLibraryActor actor, JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>A reviewed preparation in every mode, bound to the library target whose revision digests the bound target revisions.</summary>
    protected WeaverResult<WeaverToolPreparation> Prepared(WorkflowLibraryContext.Admitted list, IEnumerable<string> boundRevisions, object frozen, object preview) =>
        WeaverResult<WeaverToolPreparation>.Success(new(
            new(WorkflowLibraryContext.ModuleId, WorkflowLibraryContext.LibraryResourceType, "library", Digest(boundRevisions)),
            JsonSerializer.SerializeToElement(frozen, JsonOptions), WeaverRisk.ReviewRequired,
            new(ResultType, "1", JsonSerializer.SerializeToElement(preview, JsonOptions)), RequiresApproval: true) { Client = list.Client });

    /// <summary>The definitions the change targets: explicit, distinct, and each a row the user sees in the admitted list.</summary>
    protected static string[]? Targets(JsonElement arguments, WorkflowLibraryContext.Admitted list, int limit = 50)
    {
        if (!arguments.TryGetProperty("definitionIds", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 || ids.GetArrayLength() > limit)
            return null;
        var visible = list.Content.GetProperty("items").EnumerateArray().Select(item => WorkflowWeaverSnapshot.Text(item, "id")).ToHashSet(StringComparer.Ordinal);
        var result = ids.EnumerateArray().Select(WorkflowWeaverSnapshot.String).ToArray();
        return result.Any(id => id is null || !visible.Contains(id)) || result.Distinct(StringComparer.Ordinal).Count() != result.Length ? null : result!;
    }

    protected static bool Only(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));

    protected static string NewKey(string kind) => $"weaver-{kind}-{Guid.NewGuid():N}";

    /// <summary>The per-target outcomes as data and the overall status: applied only when every target applied.</summary>
    protected static (string Status, object Targets) Summarize(IReadOnlyList<WorkflowLibraryTargetOutcome> outcomes) =>
        (outcomes.All(outcome => outcome.Status == WorkflowLibraryCommandStatus.Applied) ? "applied"
            : outcomes.Any(outcome => outcome.Status == WorkflowLibraryCommandStatus.Applied) ? "partial" : "refused",
         outcomes.Select(outcome => new { outcome.DefinitionId, outcome.Status, outcome.Revision, outcome.Message }).ToArray());

    protected static string Describe(string status, int applied, int total, string action) => status switch
    {
        "applied" => $"The workflow library {action} for all {total} workflow(s) and reread the result.",
        "partial" => $"The workflow library {action} for {applied} of {total} workflow(s) only; the others were not changed. This is not a complete change.",
        _ => $"The workflow library changed nothing: no workflow could be {action.Replace("applied the change", "changed")}."
    };

    protected static WeaverResult<WeaverToolPreparation> Failure(string code, string message, int status) =>
        WeaverResult<WeaverToolPreparation>.Failure(code, message, status);

    /// <summary>A library refusal at preparation, with the library's own cause-free message and the matching status.</summary>
    protected static WeaverResult<WeaverToolPreparation> Outcome(string status, string? message) => status switch
    {
        WorkflowLibraryCommandStatus.Unavailable => Failure("weaver.workflow.library_unavailable", message ?? "Workflow organization is unavailable.", 503),
        WorkflowLibraryCommandStatus.Unsupported => Failure("weaver.workflow.library_unsupported", message ?? "This library backend does not support the change.", 501),
        WorkflowLibraryCommandStatus.Denied => Failure("weaver.workflow.permission_denied", message ?? "The workflow library withholds this change.", 403),
        WorkflowLibraryCommandStatus.NotFound => Failure("weaver.workflow.library_not_found", message ?? "A workflow library item is unavailable.", 404),
        WorkflowLibraryCommandStatus.Conflict => Failure("weaver.workflow.library_conflict", message ?? "The workflow library changed; reload it.", 409),
        _ => Failure("weaver.workflow.library_invalid", message ?? "The workflow library refused the change.", 400)
    };

    protected static string Digest(IEnumerable<string?> revisions) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", revisions.Select(item => item ?? ""))))).ToLowerInvariant();

    protected static string Digest(IEnumerable<WorkflowLibraryTargetOutcome> outcomes) =>
        Digest(outcomes.Select(outcome => $"{outcome.DefinitionId}\0{outcome.Status}\0{outcome.Revision}"));
}

/// <summary>Assigns and removes tags on visible workflows, each target bound by its organization revision. A tag that does not
/// exist is created explicitly, under tag-management authority and a client operation key, and shown as a creation in review.</summary>
internal sealed class WorkflowLibraryAssignTagsTool(IEnumerable<IWorkflowLibraryCommands>? commands = null) : WorkflowLibraryServerTool(commands)
{
    public const string ToolName = "workflow_library_assign_tags";
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Tag workflows",
        "Proposes a reviewed tag change for visible workflows in the library: add or remove tags (by name; a controlled tag's value by name). " +
        "A tag that does not exist is created as a marker tag first, which needs tag-management permission and is shown in the review. Reports one outcome per workflow.",
        """{"type":"object","required":["definitionIds"],"properties":{"definitionIds":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string","maxLength":128}},"add":{"type":"array","maxItems":16,"items":{"type":"object","required":["tag"],"properties":{"tag":{"type":"string","maxLength":256},"value":{"type":"string","maxLength":256}},"additionalProperties":false}},"remove":{"type":"array","maxItems":16,"items":{"type":"object","required":["tag"],"properties":{"tag":{"type":"string","maxLength":256},"value":{"type":"string","maxLength":256}},"additionalProperties":false}}},"additionalProperties":false}""",
        "workflows/design/tag-assignments:manage");

    public override WeaverToolDescriptor Descriptor => Contract;

    protected override async Task<WeaverResult<WeaverToolPreparation>> PrepareCoreAsync(IWorkflowLibraryCommands library, WorkflowLibraryActor actor,
        WorkflowLibraryPermissionFlags permissions, WorkflowLibraryContext.Admitted list, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Only(arguments, "definitionIds", "add", "remove") || Targets(arguments, list) is not { } ids)
            return Failure("weaver.workflow.library_arguments_invalid", "Name between 1 and 50 distinct workflows from the visible list; nothing was changed.", 400);
        var add = Changes(arguments, "add"); var remove = Changes(arguments, "remove");
        if (add is null || remove is null || add.Count + remove.Count == 0)
            return Failure("weaver.workflow.library_arguments_invalid", "Name at least one tag to add or remove, each as {tag, value?}; nothing was changed.", 400);
        if (!permissions.CanAssignTags)
            return Failure("weaver.workflow.permission_denied", "Tag assignment permission is required to change workflow tags; nothing was changed.", 403);
        // Each named tag is looked up by its own name, never by the first page of the catalog: the library searches by the
        // normalized name's prefix, ordered by normalized name, so the exact name is the first of its matches.
        var found = new Dictionary<string, WorkflowLibraryTagSnapshot?>(StringComparer.Ordinal);
        foreach (var name in add.Select(item => item.Tag).Concat(remove.Select(item => item.Tag)))
        {
            var normalized = Normalize(name);
            if (found.ContainsKey(normalized)) continue;
            var page = await library.ListTagsAsync(actor, name, cancellationToken);
            if (!page.Succeeded || page.Value is null) return Outcome(page.Status, page.Message);
            var exact = page.Value.Where(tag => Same(tag.Name, name)).ToArray();
            if (exact.Length > 1) return Failure("weaver.workflow.library_clarify", $"Several tags are named '{name}'; ask which one. Nothing was changed.", 400);
            found[normalized] = exact.SingleOrDefault();
        }
        var frozenAdd = new List<object>(); var frozenRemove = new List<object>(); var creates = new List<object>(); var preview = new List<object>();
        // The same tag (or tag value) named twice, in any letter case or Unicode form, is one change, and a new tag is created once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value, adding) in add.Select(item => (item.Tag, item.Value, true)).Concat(remove.Select(item => (item.Tag, item.Value, false))))
        {
            var identity = (adding ? "+" : "-") + Normalize(name) + "\0" + (value is null ? "" : Normalize(value));
            if (!seen.Add(identity)) continue;
            if (seen.Contains((adding ? "-" : "+") + identity[1..]))
                return Failure("weaver.workflow.library_clarify", $"The tag '{name}' is both added and removed; ask which is meant. Nothing was changed.", 400);
            var existing = found[Normalize(name)];
            if (existing is null)
            {
                // Only an assignment may create, only a marker tag, and only under tag-management authority.
                if (!adding) return Failure("weaver.workflow.library_not_found", $"No tag named '{name}' exists to remove; nothing was changed.", 404);
                if (value is not null)
                    return Failure("weaver.workflow.library_clarify", $"The controlled tag '{name}' does not exist. Create it with its values in the library first; nothing was changed.", 400);
                if (!permissions.CanManageTags)
                    return Failure("weaver.workflow.permission_denied", $"The tag '{name}' does not exist, and creating it needs tag-management permission; nothing was changed.", 403);
                var key = NewKey("tag");
                creates.Add(new { name, operationKey = key });
                frozenAdd.Add(new { tagId = (string?)null, valueId = (string?)null, create = key });
                preview.Add(new { change = "create-and-add", tag = name, value = (string?)null });
                continue;
            }
            var tag = existing;
            string? valueId = null;
            if (tag.Mode == "controlled")
            {
                var values = tag.Values.Where(candidate => Same(candidate.Name, value)).ToArray();
                if (value is null || values.Length != 1)
                    return Failure("weaver.workflow.library_clarify", $"Choose one existing value of the controlled tag '{tag.Name}'; nothing was changed.", 400);
                valueId = values[0].Id;
            }
            else if (value is not null)
                return Failure("weaver.workflow.library_clarify", $"The marker tag '{tag.Name}' takes no value; nothing was changed.", 400);
            (adding ? frozenAdd : frozenRemove).Add(new { tagId = tag.Id, valueId, create = (string?)null });
            preview.Add(new { change = adding ? "add" : "remove", tag = tag.Name, value });
        }
        // Each target is bound by its current organization revision: a later change makes that target a conflict, never an overwrite.
        var targets = new List<object>(); var revisions = new List<string>();
        foreach (var id in ids)
        {
            var organization = await library.GetOrganizationAsync(actor, id, cancellationToken);
            if (!organization.Succeeded || organization.Value is null) return Outcome(organization.Status, organization.Message);
            targets.Add(new { definitionId = id, revision = organization.Value.Revision });
            revisions.Add($"{id}\0{organization.Value.Revision}");
        }
        return Prepared(list, revisions,
            new { targets, add = frozenAdd, remove = frozenRemove, creates, operationKey = NewKey("tags") },
            new { kind = "tags", status = "proposed", targets, changes = preview, createsTags = creates.Count,
                summary = creates.Count == 0 ? "Change tags on the listed workflows." : "Create the named tag(s) first, then change tags on the listed workflows." });
    }

    protected override async Task<(string Status, string Summary, JsonElement Data, IReadOnlyList<WorkflowLibraryTargetOutcome> Targets)> ExecuteCoreAsync(
        IWorkflowLibraryCommands library, WorkflowLibraryActor actor, JsonElement arguments, CancellationToken cancellationToken)
    {
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var creations = new List<object>();
        foreach (var create in arguments.GetProperty("creates").EnumerateArray())
        {
            var key = create.GetProperty("operationKey").GetString()!; var name = create.GetProperty("name").GetString()!;
            WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot> result;
            try { result = await library.CreateTagAsync(actor, new(name), key, cancellationToken); }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The response was lost: an authoritative reread by the key decides whether the one tag exists (ADR 0051).
                result = await library.FindTagByOperationKeyAsync(actor, key, cancellationToken);
                if (result is { Succeeded: true, Value: null }) result = new(WorkflowLibraryCommandStatus.Conflict, "The tag creation did not complete.");
            }
            creations.Add(new { name, status = result.Status, tagId = result.Value?.Id, result.Message });
            if (!result.Succeeded || result.Value is null)
            {
                // An earlier creation in this change is persisted, so the change is partial; otherwise nothing changed. Every
                // frozen target says it was not changed, so the outcome (and the receipt's after-revision) names them all.
                var stopped = created.Count == 0 ? "refused" : "partial";
                var untouched = arguments.GetProperty("targets").EnumerateArray().Select(target => new WorkflowLibraryTargetOutcome(
                    target.GetProperty("definitionId").GetString()!, NotApplied, target.GetProperty("revision").GetString(),
                    $"Not changed: creating the tag '{name}' was {result.Status}.")).ToArray();
                var (_, targetData) = Summarize(untouched);
                var none = JsonSerializer.SerializeToElement(new { kind = "tags", status = stopped, creations, targets = targetData }, JsonOptions);
                return (stopped, $"Creating the tag '{name}' was {result.Status}: {result.Message} No workflow tags were changed on "
                    + string.Join(", ", untouched.Select(outcome => outcome.DefinitionId)) + ".", none, untouched);
            }
            created[key] = result.Value.Id;
        }
        WorkflowLibraryTagReference Reference(JsonElement item) => item.GetProperty("create").GetString() is { } key
            ? new(created[key]) : new(item.GetProperty("tagId").GetString()!, item.TryGetProperty("valueId", out var value) ? value.GetString() : null);
        var edit = new WorkflowLibraryTagEdit(
            arguments.GetProperty("targets").EnumerateArray().Select(target => new WorkflowLibraryRevisionTarget(target.GetProperty("definitionId").GetString()!, target.GetProperty("revision").GetString()!)).ToArray(),
            arguments.GetProperty("add").EnumerateArray().Select(Reference).ToArray(), arguments.GetProperty("remove").EnumerateArray().Select(Reference).ToArray(),
            arguments.GetProperty("operationKey").GetString()!);
        var edited = await library.EditTagsAsync(actor, edit, cancellationToken);
        var outcomes = edited.Succeeded && edited.Value is not null ? edited.Value
            : edit.Targets.Select(target => new WorkflowLibraryTargetOutcome(target.DefinitionId, edited.Status, null, edited.Message)).ToArray();
        var (status, targets) = Summarize(outcomes);
        // A created tag is a persisted effect even when no assignment applied.
        if (status == "refused" && created.Count > 0) status = "partial";
        var data = JsonSerializer.SerializeToElement(new { kind = "tags", status, creations, targets }, JsonOptions);
        return (status, Describe(status, outcomes.Count(outcome => outcome.Status == WorkflowLibraryCommandStatus.Applied), outcomes.Count, "applied the change"), data, outcomes);
    }

    private static List<(string Tag, string? Value)>? Changes(JsonElement arguments, string property)
    {
        var result = new List<(string, string?)>();
        if (!arguments.TryGetProperty(property, out var items)) return result;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 16) return null;
        foreach (var item in items.EnumerateArray())
        {
            if (!Only(item, "tag", "value") || WorkflowWeaverSnapshot.Text(item, "tag") is not { Length: > 0 and <= 256 } tag) return null;
            var value = WorkflowWeaverSnapshot.Text(item, "value");
            if (item.TryGetProperty("value", out var raw) && raw.ValueKind != JsonValueKind.Null && value is not { Length: > 0 and <= 256 }) return null;
            result.Add((tag.Trim(), value?.Trim()));
        }
        return result;
    }

    // The library compares tag and value names by their normalized form (NFKC, upper invariant).
    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    private static bool Same(string? left, string? right) => left is not null && right is not null && string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}

/// <summary>Sets or clears the actor's favorite on visible workflows: an idempotent set, reported per workflow.</summary>
internal sealed class WorkflowLibrarySetFavoriteTool(IEnumerable<IWorkflowLibraryCommands>? commands = null) : WorkflowLibraryServerTool(commands)
{
    public const string ToolName = "workflow_library_set_favorite";
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Favorite workflows",
        "Proposes a reviewed favorite change (an idempotent set) for visible workflows in the library. Reports one outcome per workflow.",
        """{"type":"object","required":["definitionIds","favorite"],"properties":{"definitionIds":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string","maxLength":128}},"favorite":{"type":"boolean"}},"additionalProperties":false}""",
        "workflows/design:read");

    public override WeaverToolDescriptor Descriptor => Contract;

    protected override Task<WeaverResult<WeaverToolPreparation>> PrepareCoreAsync(IWorkflowLibraryCommands library, WorkflowLibraryActor actor,
        WorkflowLibraryPermissionFlags permissions, WorkflowLibraryContext.Admitted list, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Only(arguments, "definitionIds", "favorite") || Targets(arguments, list) is not { } ids
            || !arguments.TryGetProperty("favorite", out var favorite) || favorite.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Task.FromResult(Failure("weaver.workflow.library_arguments_invalid", "Name visible workflows and favorite true or false; nothing was changed.", 400));
        var set = favorite.GetBoolean();
        return Task.FromResult(Prepared(list, ids.Select(id => $"{id}\0favorite\0{set}"), new { definitionIds = ids, favorite = set },
            new { kind = "favorite", status = "proposed", targets = ids.Select(id => new { definitionId = id }), favorite = set,
                summary = set ? "Mark the listed workflows as your favorites." : "Remove the listed workflows from your favorites." }));
    }

    protected override async Task<(string Status, string Summary, JsonElement Data, IReadOnlyList<WorkflowLibraryTargetOutcome> Targets)> ExecuteCoreAsync(
        IWorkflowLibraryCommands library, WorkflowLibraryActor actor, JsonElement arguments, CancellationToken cancellationToken)
    {
        var ids = arguments.GetProperty("definitionIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        var favorite = arguments.GetProperty("favorite").GetBoolean();
        var result = await library.SetFavoritesAsync(actor, ids, favorite, cancellationToken);
        var outcomes = result.Succeeded && result.Value is not null ? result.Value
            : ids.Select(id => new WorkflowLibraryTargetOutcome(id, result.Status, null, result.Message)).ToArray();
        var (status, targets) = Summarize(outcomes);
        var data = JsonSerializer.SerializeToElement(new { kind = "favorite", status, favorite, targets }, JsonOptions);
        return (status, Describe(status, outcomes.Count(outcome => outcome.Status == WorkflowLibraryCommandStatus.Applied), outcomes.Count, "applied the change"), data, outcomes);
    }
}

/// <summary>Moves visible workflows into a folder (or unfiled) as one atomic, idempotent set under folder-management authority.</summary>
internal sealed class WorkflowLibraryMoveToFolderTool(IEnumerable<IWorkflowLibraryCommands>? commands = null) : WorkflowLibraryServerTool(commands)
{
    public const string ToolName = "workflow_library_move_to_folder";
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Move workflows to folder",
        "Proposes a reviewed move of visible workflows into one folder (folderId null: unfiled), applied to all of them or none. Needs folder-management permission.",
        """{"type":"object","required":["definitionIds","folderId"],"properties":{"definitionIds":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string","maxLength":128}},"folderId":{"type":["string","null"],"maxLength":64}},"additionalProperties":false}""",
        "workflows/design:manage");

    public override WeaverToolDescriptor Descriptor => Contract;

    protected override Task<WeaverResult<WeaverToolPreparation>> PrepareCoreAsync(IWorkflowLibraryCommands library, WorkflowLibraryActor actor,
        WorkflowLibraryPermissionFlags permissions, WorkflowLibraryContext.Admitted list, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Only(arguments, "definitionIds", "folderId") || Targets(arguments, list) is not { } ids || !arguments.TryGetProperty("folderId", out var folder)
            || folder.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || folder.ValueKind == JsonValueKind.String && WorkflowWeaverSnapshot.String(folder) is not { Length: > 0 and <= 64 })
            return Task.FromResult(Failure("weaver.workflow.library_arguments_invalid", "Name visible workflows and one folder id (or null for unfiled); nothing was changed.", 400));
        if (!permissions.CanManageFolders)
            return Task.FromResult(Failure("weaver.workflow.permission_denied", "Folder-management permission is required to move workflows; nothing was changed.", 403));
        var folderId = folder.ValueKind == JsonValueKind.Null ? null : folder.GetString();
        return Task.FromResult(Prepared(list, ids.Select(id => $"{id}\0folder\0{folderId}"), new { definitionIds = ids, folderId, operationKey = NewKey("move") },
            new { kind = "folder", status = "proposed", targets = ids.Select(id => new { definitionId = id }), folderId,
                summary = "Move the listed workflows together; if any cannot move, none moves." }));
    }

    protected override async Task<(string Status, string Summary, JsonElement Data, IReadOnlyList<WorkflowLibraryTargetOutcome> Targets)> ExecuteCoreAsync(
        IWorkflowLibraryCommands library, WorkflowLibraryActor actor, JsonElement arguments, CancellationToken cancellationToken)
    {
        var ids = arguments.GetProperty("definitionIds").EnumerateArray().Select(id => id.GetString()!).ToArray();
        var folderId = arguments.GetProperty("folderId").ValueKind == JsonValueKind.Null ? null : arguments.GetProperty("folderId").GetString();
        var result = await library.MoveDefinitionsAsync(actor, ids, folderId, arguments.GetProperty("operationKey").GetString()!, cancellationToken);
        var outcomes = result.Value ?? ids.Select(id => new WorkflowLibraryTargetOutcome(id, result.Status, null, result.Message)).ToArray();
        var (status, targets) = Summarize(outcomes);
        var data = JsonSerializer.SerializeToElement(new { kind = "folder", status, folderId, targets }, JsonOptions);
        return (status, status == "applied" ? $"The workflow library moved all {ids.Length} workflow(s) and reread the result."
            : $"The workflow library moved nothing ({result.Status}): {result.Message}", data, outcomes);
    }
}

/// <summary>Saves the visible list as a workflow library view: a new view under a client operation key, or the applied view
/// bound by its revision. Sharing needs view-sharing authority. A lost response is decided by rereading the view by its key.</summary>
internal sealed class WorkflowLibrarySaveViewTool(IEnumerable<IWorkflowLibraryCommands>? commands = null) : WorkflowLibraryServerTool(commands)
{
    public const string ToolName = "workflow_library_save_view";
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Save library view",
        "Proposes saving the visible workflow list (its query and columns) as a library view: a new view by name, or the applied view (update: true), private or shared. Sharing needs view-sharing permission.",
        """{"type":"object","required":["name","shared"],"properties":{"name":{"type":"string","minLength":1,"maxLength":256},"shared":{"type":"boolean"},"update":{"type":"boolean"}},"additionalProperties":false}""",
        "workflows/design:read");

    public override WeaverToolDescriptor Descriptor => Contract;

    protected override Task<WeaverResult<WeaverToolPreparation>> PrepareCoreAsync(IWorkflowLibraryCommands library, WorkflowLibraryActor actor,
        WorkflowLibraryPermissionFlags permissions, WorkflowLibraryContext.Admitted list, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Only(arguments, "name", "shared", "update") || WorkflowWeaverSnapshot.Text(arguments, "name") is not { Length: > 0 and <= 256 } name
            || !arguments.TryGetProperty("shared", out var sharedValue) || sharedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || arguments.TryGetProperty("update", out var updateValue) && updateValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Task.FromResult(Failure("weaver.workflow.library_arguments_invalid", "Name the view and say whether it is shared; nothing was changed.", 400));
        var shared = sharedValue.GetBoolean();
        var update = arguments.TryGetProperty("update", out var flag) && flag.GetBoolean();
        if (shared && !permissions.CanShareViews)
            return Task.FromResult(Failure("weaver.workflow.permission_denied", "View-sharing permission is required to save a shared view; nothing was changed.", 403));
        string? viewId = null, revision = null;
        if (update)
        {
            // An update is the applied view, bound by the revision the user sees; only a view the actor may edit.
            if (list.Content.GetProperty("view") is not { ValueKind: JsonValueKind.Object } view || !view.GetProperty("canEdit").GetBoolean())
                return Task.FromResult(Failure("weaver.workflow.library_clarify", "No view the actor may edit is applied to the list; save a new view instead. Nothing was changed.", 400));
            viewId = view.GetProperty("id").GetString(); revision = view.GetProperty("revision").GetString();
            if (string.IsNullOrWhiteSpace(viewId) || string.IsNullOrWhiteSpace(revision))
                return Task.FromResult(Failure("weaver.workflow.library_clarify", "The applied view has no identity or revision to update; save a new view instead. Nothing was changed.", 400));
        }
        var query = list.Content.GetProperty("query"); var columns = list.Content.GetProperty("columns");
        var key = update ? null : NewKey("view");
        return Task.FromResult(Prepared(list, [$"view\0{viewId}\0{revision}\0{list.Target.Revision}"],
            new { name, shared, viewId, revision, query, columns, operationKey = key },
            new { kind = "view", status = "proposed", name, shared, viewId, query, columns, ownerKind = shared ? "shared" : "private",
                summary = update ? "Save the visible list over the applied view." : "Save the visible list as a new view." }));
    }

    protected override async Task<(string Status, string Summary, JsonElement Data, IReadOnlyList<WorkflowLibraryTargetOutcome> Targets)> ExecuteCoreAsync(
        IWorkflowLibraryCommands library, WorkflowLibraryActor actor, JsonElement arguments, CancellationToken cancellationToken)
    {
        var query = arguments.GetProperty("query");
        var input = new WorkflowLibraryViewSave(arguments.GetProperty("name").GetString()!, new(
                WorkflowWeaverSnapshot.Text(query, "search"), WorkflowWeaverSnapshot.Text(query, "state") ?? "active", WorkflowWeaverSnapshot.Text(query, "folderId"),
                query.GetProperty("unfiled").GetBoolean(), query.GetProperty("includeDescendants").GetBoolean(),
                query.GetProperty("tagFilters").EnumerateArray().Select(filter => new WorkflowLibraryTagFilterSnapshot(filter.GetProperty("tagId").GetString()!,
                    filter.GetProperty("operator").GetString()!, filter.GetProperty("valueIds").EnumerateArray().Select(value => value.GetString()!).ToArray())).ToArray(),
                query.GetProperty("favoritesOnly").GetBoolean(), WorkflowWeaverSnapshot.Text(query, "sort") ?? "name", 50, WorkflowWeaverSnapshot.Text(query, "publicationState")),
            arguments.GetProperty("shared").GetBoolean(), arguments.GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray(),
            WorkflowWeaverSnapshot.Text(arguments, "revision"));
        var viewId = WorkflowWeaverSnapshot.Text(arguments, "viewId"); var key = WorkflowWeaverSnapshot.Text(arguments, "operationKey");
        // Weaver creates only under a key, so a lost response can be resolved by rereading it; an update names its view.
        if (viewId is null && key is null)
            return ("refused", "The view change names neither an existing view nor an operation key for a new one; nothing was saved.",
                JsonSerializer.SerializeToElement(new { kind = "view", status = "refused", view = (object?)null, targets = Array.Empty<object>() }, JsonOptions), []);
        WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot> result;
        try { result = await library.SaveViewAsync(actor, viewId, input, key, cancellationToken); }
        catch (Exception) when (key is not null && !cancellationToken.IsCancellationRequested)
        {
            // The response was lost: an authoritative reread by the key decides whether the one view exists (ADR 0051).
            result = await library.FindViewByOperationKeyAsync(actor, key, cancellationToken);
            if (result is { Succeeded: true, Value: null }) result = new(WorkflowLibraryCommandStatus.Conflict, "The view save did not complete.");
        }
        var outcome = new WorkflowLibraryTargetOutcome(result.Value?.Id ?? viewId ?? "new-view", result.Status, result.Value?.Revision, result.Message);
        var status = result.Succeeded ? "applied" : "refused";
        var data = JsonSerializer.SerializeToElement(new
        {
            kind = "view", status, view = result.Value is { } view ? new { view.Id, view.Name, ownerKind = view.Shared ? "shared" : "private", view.Revision } : null,
            targets = new[] { new { outcome.DefinitionId, outcome.Status, outcome.Revision, outcome.Message } }
        }, JsonOptions);
        return (status, result.Succeeded ? $"The workflow library saved the view '{result.Value!.Name}' and reread it." : $"The view was not saved ({result.Status}): {result.Message}",
            data, [outcome]);
    }
}
