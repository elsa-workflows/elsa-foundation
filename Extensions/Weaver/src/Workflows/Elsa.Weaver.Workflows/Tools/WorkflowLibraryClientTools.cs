using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Microsoft.Extensions.Logging;

namespace Elsa.Weaver.Workflows.Tools;

/// <summary>
/// A reviewed change to a library page's own working state (spec 1193 FR-020, FR-023, FR-025): prepared against one
/// admitted client context and its client binding, reviewed in every mode, applied only by that page, and confirmed by a
/// receipt that repeats exactly the approved change. Nothing is persisted; the page keeps the change revertible.
/// </summary>
internal abstract class WorkflowLibraryClientTool(IWeaverToolAuthorizer authorizer) : IWeaverTool, IWeaverClientTool, IWeaverToolAuthorization
{
    protected static readonly JsonSerializerOptions JsonOptions = WorkflowLibraryContext.JsonOptions;

    public abstract WeaverToolDescriptor Descriptor { get; }
    protected abstract string ContextKind { get; }
    protected abstract string ResultType { get; }
    /// <summary>The property of the receipt's data that must repeat the frozen change.</summary>
    protected abstract string ChangeProperty { get; }

    protected static WeaverToolDescriptor Describe(string name, string displayName, string description, string schema, string resultType, string rendererId) =>
        new(name, displayName, description, WeaverToolMutability.Mutating, WeaverRisk.ReviewRequired, schema, ["workflows/design:read"], ["/workflows"])
        {
            ModuleId = WorkflowLibraryContext.ModuleId, ContractVersion = "1", ExecutionLocation = WeaverExecutionLocation.ClientWorkingState,
            ResultType = resultType, ResultSchemaVersion = "1", ResultRendererId = rendererId, DisclosureSourceId = WorkflowLibraryContext.ContextSourceId
        };

    public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = WorkflowLibraryContext.ResourceTypeOf(ContextKind);
        return ValueTask.FromResult(target is null || target.ModuleId == WorkflowLibraryContext.ModuleId && target.ResourceType == type
            ? new WeaverPolicyDecision(true, [])
            : new WeaverPolicyDecision(false, [new("weaver.workflow.resource_required", "This tool changes only its own page's working state.")]));
    }

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var contexts = invocation.Context.Where(item => item.ContentType == ContextKind && item.Origin == WeaverContextOrigin.ClientSnapshot).ToArray();
        if (contexts.Length != 1 || !WorkflowLibraryContext.TryRead(contexts[0], out var page, out _))
            return Failure("weaver.workflow.page_context_required", "The page's admitted working state is required; nothing was changed.", 409);
        if (invocation.Client is not null && invocation.Client != page.Client)
            return Failure("weaver.workflow.client_mismatch", "The working state belongs to another page; nothing was changed.", 409);
        var host = await authorizer.AuthorizeAsync(Descriptor, invocation.ActorId, invocation.TenantId, page.Target, cancellationToken);
        if (!host.Allowed) return Failure("weaver.workflow.permission_denied", "Current workflow design read permission is required; nothing was changed.", 403);
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("weaver.workflow.page_arguments_invalid", "The arguments must be JSON; nothing was changed.", 400); }
        var (change, preview, failure) = await FreezeAsync(page, arguments, cancellationToken);
        if (failure is not null) return failure;
        return WeaverResult<WeaverToolPreparation>.Success(new(page.Target, JsonSerializer.SerializeToElement(new { baseRevision = page.Target.Revision, change }, JsonOptions),
            WeaverRisk.ReviewRequired, new(ResultType, "1", JsonSerializer.SerializeToElement(preview, JsonOptions)), RequiresApproval: true) { Client = page.Client });
    }

    /// <summary>The validated, frozen change and its review preview, or the refusal naming what is wrong.</summary>
    protected abstract Task<(JsonObject? Change, object? Preview, WeaverResult<WeaverToolPreparation>? Failure)> FreezeAsync(WorkflowLibraryContext.Admitted page,
        JsonElement arguments, CancellationToken cancellationToken);

    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        => Task.FromResult(WeaverResult<WeaverToolResult>.Failure("weaver.workflow.client_execution_required", "This change must be applied by the originating page.", 409));

    public Task<WeaverResult<WeaverToolResult>> CompleteAsync(WeaverPreparedToolInvocation invocation, WeaverClientAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        var before = invocation.Target; var after = acknowledgement.After; var payload = acknowledgement.Result; var data = payload?.Data ?? default;
        var frozen = invocation.Arguments.TryGetProperty("change", out var change) ? change : default;
        if (acknowledgement.Outcome != WeaverClientOutcome.Applied || before is null || after is null || frozen.ValueKind != JsonValueKind.Object
            || before.ModuleId != WorkflowLibraryContext.ModuleId || before.ResourceType != WorkflowLibraryContext.ResourceTypeOf(ContextKind)
            || before.ModuleId != after.ModuleId || before.ResourceType != after.ResourceType || before.ResourceId != after.ResourceId
            || string.IsNullOrWhiteSpace(after.Revision) || after.Revision == before.Revision || after.Revision.Length > 256
            || invocation.Client != acknowledgement.Client || invocation.ApprovalDigest != acknowledgement.ApprovalDigest
            || payload is not { SchemaVersion: "1" } || payload.TypeId != ResultType || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("observed", out var observed) || observed.ValueKind != JsonValueKind.True
            || WorkflowWeaverSnapshot.Text(data, "revision") != after.Revision || WorkflowWeaverSnapshot.Text(data, "baseRevision") != before.Revision
            || !data.TryGetProperty(ChangeProperty, out var applied) || Canonical(applied) != Canonical(frozen)
            || WorkflowWeaverSnapshot.Text(data, "undoToken") is not { Length: > 0 and <= 256 } undoToken)
            return Task.FromResult(WeaverResult<WeaverToolResult>.Failure("weaver.workflow.observation_invalid",
                "The page did not confirm the exact approved change in its working state.", 409));
        // The admitted result repeats the server's frozen change, never the client's account of it.
        var admitted = new WeaverPayload(ResultType, "1", JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["revision"] = after.Revision, ["baseRevision"] = before.Revision, [ChangeProperty] = frozen, ["undoToken"] = undoToken, ["observed"] = true
        }, JsonOptions));
        var observedTarget = new WeaverResourceTarget(before.ModuleId, before.ResourceType, before.ResourceId, after.Revision);
        return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "The originating page applied the approved change to its working state, where the user can revert it. Nothing was saved.", admitted)
        { Receipt = new(invocation.InvocationId, before, observedTarget, WeaverEffectScope.ClientWorkingState, admitted) }));
    }

    protected static WeaverResult<WeaverToolPreparation> Failure(string code, string message, int status) => WeaverResult<WeaverToolPreparation>.Failure(code, message, status);

    protected static bool Only(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));

    private static string Canonical(JsonElement value) => JsonNode.Parse(value.GetRawText()) is { } node ? Sort(node).ToJsonString() : "null";

    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value is null ? null : Sort(pair.Value.DeepClone())))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Sort(item.DeepClone())).ToArray()),
        _ => node.DeepClone()
    };
}

/// <summary>Narrows, sorts or changes the columns of the visible workflow list: a reviewed, revertible change to the list page only.</summary>
internal sealed class WorkflowLibrarySetListStateTool(IWeaverToolAuthorizer authorizer) : WorkflowLibraryClientTool(authorizer)
{
    public const string ToolName = "workflow_library_set_list_state";
    internal const string Result = "workflows.list-state";
    internal static readonly string[] Columns = ["name", "id", "state", "folder", "location", "tags", "modified", "version", "favorite"];
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Adjust workflow list",
        "Proposes a reviewed change to the visible workflow list only: its search, folder, tag filters, favorites-only, sort, publication filter or columns. " +
        "The list page applies it and the user can revert it; nothing is saved.",
        """{"type":"object","properties":{"search":{"type":["string","null"],"maxLength":256},"folderId":{"type":["string","null"],"maxLength":64},"favoritesOnly":{"type":"boolean"},"sort":{"type":"string","enum":["name","name-desc","modified"]},"publicationState":{"type":["string","null"],"enum":["published",null]},"tagFilters":{"type":"array","maxItems":16,"items":{"type":"object","required":["tagId"],"properties":{"tagId":{"type":"string","maxLength":64},"operator":{"type":"string","enum":["exists","any","all"]},"valueIds":{"type":"array","maxItems":32,"items":{"type":"string","maxLength":64}}},"additionalProperties":false}},"columns":{"type":"array","maxItems":9,"items":{"type":"string","enum":["name","id","state","folder","location","tags","modified","version","favorite"]}}},"additionalProperties":false}""",
        Result, "weaver.workflows.list-state-result");

    public override WeaverToolDescriptor Descriptor => Contract;
    protected override string ContextKind => WorkflowLibraryContext.LibraryKind;
    protected override string ResultType => Result;
    protected override string ChangeProperty => "change";

    protected override Task<(JsonObject? Change, object? Preview, WeaverResult<WeaverToolPreparation>? Failure)> FreezeAsync(WorkflowLibraryContext.Admitted page,
        JsonElement arguments, CancellationToken cancellationToken)
    {
        (JsonObject?, object?, WeaverResult<WeaverToolPreparation>?) Refuse(string message) =>
            (null, null, Failure("weaver.workflow.list_state_invalid", message + " Nothing was changed.", 400));
        if (!Only(arguments, "search", "folderId", "favoritesOnly", "sort", "publicationState", "tagFilters", "columns") || !arguments.EnumerateObject().Any())
            return Task.FromResult(Refuse("Name at least one of the list's search, folder, tag filters, favorites-only, sort, publication filter or columns."));
        var organizationOff = WorkflowWeaverSnapshot.Text(page.Content, "organization") == "disabled";
        // With organization disabled only ordinary list support remains: no folder, tag or favorite narrowing.
        if (organizationOff && (arguments.TryGetProperty("folderId", out _) || arguments.TryGetProperty("tagFilters", out _) || arguments.TryGetProperty("favoritesOnly", out _)))
            return Task.FromResult<(JsonObject?, object?, WeaverResult<WeaverToolPreparation>?)>((null, null,
                Failure("weaver.workflow.library_unavailable", "Workflow organization is disabled for this backend, so folder, tag and favorite filters are unavailable. Nothing was changed.", 503)));
        var change = new JsonObject();
        foreach (var property in arguments.EnumerateObject())
        {
            var value = property.Value;
            var ok = property.Name switch
            {
                "search" => value.ValueKind == JsonValueKind.Null || WorkflowWeaverSnapshot.String(value) is { Length: <= 256 },
                "folderId" => value.ValueKind == JsonValueKind.Null || WorkflowWeaverSnapshot.String(value) is { Length: > 0 and <= 64 },
                "favoritesOnly" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "sort" => WorkflowWeaverSnapshot.String(value) is "name" or "name-desc" or "modified",
                "publicationState" => value.ValueKind == JsonValueKind.Null || WorkflowWeaverSnapshot.String(value) is "published",
                "tagFilters" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 16 && value.EnumerateArray().All(filter =>
                    Only(filter, "tagId", "operator", "valueIds") && WorkflowWeaverSnapshot.Text(filter, "tagId") is { Length: > 0 and <= 64 }
                    && (!filter.TryGetProperty("operator", out var op) || WorkflowWeaverSnapshot.String(op) is "exists" or "any" or "all")
                    && (!filter.TryGetProperty("valueIds", out var values) || values.ValueKind == JsonValueKind.Array && values.GetArrayLength() <= 32
                        && values.EnumerateArray().All(item => WorkflowWeaverSnapshot.String(item) is { Length: > 0 and <= 64 }))),
                "columns" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is > 0 and <= 9
                    && value.EnumerateArray().Select(WorkflowWeaverSnapshot.String).ToArray() is var names
                    && names.All(name => name is not null && Columns.Contains(name)) && names.Distinct().Count() == names.Length,
                _ => false
            };
            if (!ok) return Task.FromResult(Refuse($"The list's '{property.Name}' is not a supported value."));
            change[property.Name] = JsonNode.Parse(value.GetRawText());
        }
        var query = page.Content.GetProperty("query");
        var preview = new
        {
            kind = "list-state", status = "proposed", revision = page.Target.Revision, change,
            before = new { query, columns = page.Content.GetProperty("columns") },
            summary = "Change only the visible workflow list on this page; you can revert it. Nothing is saved."
        };
        return Task.FromResult<(JsonObject?, object?, WeaverResult<WeaverToolPreparation>?)>((change, preview, null));
    }
}

/// <summary>Prefills the open new-workflow dialog: name, description, and a root activity from the authorized catalog or a
/// Reusable Workflow Template the dialog lists. Creating the definition remains the user's explicit Create action.</summary>
internal sealed class WorkflowCreateDialogPrefillTool(IWeaverToolAuthorizer authorizer, IEnumerable<IActivityAuthoringCatalog>? catalogs = null,
    ILogger<WorkflowCreateDialogPrefillTool>? logger = null) : WorkflowLibraryClientTool(authorizer)
{
    public const string ToolName = "workflow_create_dialog_prefill";
    internal const string Result = "workflows.create-dialog-prefill";
    internal static readonly WeaverToolDescriptor Contract = Describe(ToolName, "Prefill new workflow",
        "Proposes reviewed values for the open new-workflow dialog only: name, description, and either a root activity version from the workflow.catalog " +
        "or a template the dialog lists. The user still creates the workflow with the dialog's own Create action.",
        """{"type":"object","required":["name"],"properties":{"name":{"type":"string","minLength":1,"maxLength":256},"description":{"type":"string","maxLength":2000},"rootActivityVersionId":{"type":"string","maxLength":256},"templateId":{"type":"string","maxLength":128}},"additionalProperties":false}""",
        Result, "weaver.workflows.create-dialog-result");

    public override WeaverToolDescriptor Descriptor => Contract;
    protected override string ContextKind => WorkflowLibraryContext.CreateDialogKind;
    protected override string ResultType => Result;
    protected override string ChangeProperty => "change";

    protected override async Task<(JsonObject? Change, object? Preview, WeaverResult<WeaverToolPreparation>? Failure)> FreezeAsync(WorkflowLibraryContext.Admitted page,
        JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!Only(arguments, "name", "description", "rootActivityVersionId", "templateId") || WorkflowWeaverSnapshot.Text(arguments, "name") is not { Length: > 0 and <= 256 } name
            || arguments.TryGetProperty("description", out var description) && WorkflowWeaverSnapshot.String(description) is not { Length: <= 2000 })
            return (null, null, Failure("weaver.workflow.create_dialog_invalid", "A name (and optionally a description) is required; nothing was changed.", 400));
        var root = WorkflowWeaverSnapshot.Text(arguments, "rootActivityVersionId"); var template = WorkflowWeaverSnapshot.Text(arguments, "templateId");
        if (root is not null && template is not null)
            return (null, null, Failure("weaver.workflow.create_dialog_invalid", "Choose a root activity or a template, not both; nothing was changed.", 400));
        string? rootName = null, templateName = null;
        if (root is not null)
        {
            // The root comes from the owner's current authorized catalog, read for this preparation: only an addable entry.
            var catalog = await WorkflowActivityCatalog.ReadAsync(catalogs, cancellationToken, logger);
            if (catalog.Status != WorkflowActivityCatalog.Status.Available)
                return (null, null, Failure("weaver.workflow.catalog_unavailable", catalog.Reason ?? "The activity catalog is unavailable; nothing was changed.", 503));
            var entry = catalog.Entries.SingleOrDefault(item => item.ActivityVersionId == root);
            if (entry is not { Available: true })
                return (null, null, Failure("weaver.workflow.create_dialog_clarify", "That root activity is not an available catalog entry; ask which activity to start with. Nothing was changed.", 400));
            rootName = entry.DisplayName;
        }
        if (template is not null)
        {
            var listed = page.Content.GetProperty("templates").EnumerateArray().FirstOrDefault(item => WorkflowWeaverSnapshot.Text(item, "id") == template);
            if (listed.ValueKind != JsonValueKind.Object)
                return (null, null, Failure("weaver.workflow.create_dialog_clarify", "That template is not listed in the open dialog; ask which template to use. Nothing was changed.", 400));
            templateName = WorkflowWeaverSnapshot.Text(listed, "name");
        }
        var change = new JsonObject { ["name"] = name.Trim() };
        if (arguments.TryGetProperty("description", out _)) change["description"] = WorkflowWeaverSnapshot.Text(arguments, "description");
        if (root is not null) change["rootActivityVersionId"] = root;
        if (template is not null) change["templateId"] = template;
        var preview = new
        {
            kind = "create-dialog", status = "proposed", revision = page.Target.Revision, change, rootDisplayName = rootName, templateName,
            summary = "Fill in the open New workflow dialog. Creating the workflow remains your Create action."
        };
        return (change, preview, null);
    }
}
