using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Weaver.Workflows.Services;

/// <summary>
/// The strict, bounded admission of the workflow library's client contexts (spec 1193 FR-020, FR-023, FR-024): the
/// visible list (<c>workflows.library</c>), the open new-workflow create dialog (<c>workflows.create-dialog</c>) and the
/// visible template list (<c>workflows.template-list</c>), each schema <c>1</c>. Every field is declared: an unknown field,
/// an over-long value or a list beyond its bound refuses the whole context. Names and descriptions are untrusted resource
/// content. Each context is bound to its client binding and the revision the page computed for its visible state.
/// </summary>
internal static class WorkflowLibraryContext
{
    internal const string ModuleId = WorkflowWeaverSnapshot.ModuleId;
    internal const string LibraryKind = "workflows.library";
    internal const string CreateDialogKind = "workflows.create-dialog";
    internal const string TemplateListKind = "workflows.template-list";
    /// <summary>The server's own read of the actor's library authority: organization availability and the native flags.</summary>
    internal const string OrganizationKind = "workflows.library-organization";
    internal const string LibraryResourceType = "workflow-library";
    internal const string CreateDialogResourceType = "workflow-create-dialog";
    internal const string TemplateListResourceType = "workflow-template-list";
    internal const string ContextSourceId = "workflows.library-context";

    /// <summary>Each admitted client context's bound on its JSON text, well under the host's 32 KiB per-attachment budget.</summary>
    internal const int ContentLimit = 16 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One admitted client context: its kind, exact target, client binding and the admitted projection.</summary>
    internal sealed record Admitted(string Kind, WeaverResourceTarget Target, WeaverClientBinding Client, JsonElement Content);

    internal static string? ResourceTypeOf(string kind) => kind switch
    {
        LibraryKind => LibraryResourceType,
        CreateDialogKind => CreateDialogResourceType,
        TemplateListKind => TemplateListResourceType,
        _ => null
    };

    internal static string ResourceIdOf(string kind) => kind switch
    {
        LibraryKind => "library", CreateDialogKind => "create-dialog", _ => "templates"
    };

    /// <summary>Reads a caller (or retained) client context strictly. False with a reason when anything is not as declared.</summary>
    internal static bool TryRead(WeaverContextAttachment attachment, [NotNullWhen(true)] out Admitted? admitted, out string error)
    {
        try { return TryReadCore(attachment, out admitted, out error); }
        catch (InvalidOperationException)
        {
            admitted = null;
            error = "The workflow library context holds malformed text.";
            return false;
        }
    }

    private static bool TryReadCore(WeaverContextAttachment attachment, [NotNullWhen(true)] out Admitted? admitted, out string error)
    {
        admitted = null;
        error = "The workflow library context requires its declared schema, an exact revision and a client binding.";
        var resourceType = ResourceTypeOf(attachment.ContentType);
        if (attachment.ModuleId != ModuleId || resourceType is null || attachment.SchemaVersion != "1"
            || attachment.Target is not { ModuleId: ModuleId } target || target.ResourceType != resourceType
            || target.ResourceId != ResourceIdOf(attachment.ContentType) || !Valid(target.Revision, 256)
            || attachment.Client is not { } client || !Valid(client.ClientId, 256) || !Valid(client.GenerationId, 256))
            return false;
        JsonElement body;
        try { body = attachment.Content is JsonElement json ? json : JsonSerializer.SerializeToElement(attachment.Content, JsonOptions); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
        if (body.ValueKind != JsonValueKind.Object || body.GetRawText().Length > ContentLimit || Text(body, "revision") != target.Revision)
            return false;
        JsonObject? projection = attachment.ContentType switch
        {
            LibraryKind => ReadLibrary(body),
            CreateDialogKind => ReadCreateDialog(body),
            _ => ReadTemplateList(body)
        };
        if (projection is null) return false;
        admitted = new(attachment.ContentType, new(ModuleId, resourceType, target.ResourceId, target.Revision), client,
            JsonSerializer.SerializeToElement(projection, JsonOptions));
        error = string.Empty;
        return true;
    }

    private static JsonObject? ReadLibrary(JsonElement body)
    {
        if (!Only(body, "revision", "query", "view", "columns", "selection", "items", "itemsOmitted", "totalCount", "permissions", "organization")) return null;
        if (!body.TryGetProperty("query", out var query) || ReadQuery(query) is not { } admittedQuery) return null;
        JsonNode? view = null;
        if (body.TryGetProperty("view", out var viewValue) && viewValue.ValueKind != JsonValueKind.Null)
        {
            if (!Only(viewValue, "id", "name", "ownerKind", "revision", "canEdit") || !Valid(Text(viewValue, "id"), 64)
                || Text(viewValue, "ownerKind") is not ("private" or "shared") || !Valid(Text(viewValue, "revision"), 64)
                || !Bool(viewValue, "canEdit", out var canEdit)) return null;
            view = new JsonObject
            {
                ["id"] = Text(viewValue, "id"), ["name"] = Bounded(Text(viewValue, "name"), 256), ["ownerKind"] = Text(viewValue, "ownerKind"),
                ["revision"] = Text(viewValue, "revision"), ["canEdit"] = canEdit
            };
        }
        if (!Strings(body, "columns", 16, 32, out var columns) || !Strings(body, "selection", 100, 128, out var selection)) return null;
        if (!body.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 50) return null;
        var admittedItems = new JsonArray();
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (!Only(item, "id", "name", "folderId", "tags", "favorite", "publicationState", "organizationRevision")
                || !Valid(Text(item, "id"), 128) || !visible.Add(Text(item, "id")!) || !OptionalText(item, "folderId", 64)
                || !OptionalText(item, "publicationState", 32) || !OptionalText(item, "organizationRevision", 64)
                || !Bool(item, "favorite", out var favorite) || !TagReferences(item, "tags", out var tags)) return null;
            admittedItems.Add(new JsonObject
            {
                ["id"] = Text(item, "id"), ["name"] = Bounded(Text(item, "name"), 256), ["folderId"] = Text(item, "folderId"), ["tags"] = tags,
                ["favorite"] = favorite, ["publicationState"] = Text(item, "publicationState"), ["organizationRevision"] = Text(item, "organizationRevision")
            });
        }
        // The selection is part of what the user sees: every selected definition is a visible row.
        if (selection.Any(id => !visible.Contains(id))) return null;
        if (!body.TryGetProperty("totalCount", out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt32(out var totalCount) || totalCount < 0)
            return null;
        // Rows the page left out of the disclosure to stay within the bound; the selection names disclosed rows only.
        var omitted = 0;
        if (body.TryGetProperty("itemsOmitted", out var omittedValue)
            && (omittedValue.ValueKind != JsonValueKind.Number || !omittedValue.TryGetInt32(out omitted) || omitted is < 0 or > 50)) return null;
        if (!body.TryGetProperty("permissions", out var permissions) || !Only(permissions, "canAssignTags", "canManageFolders", "canManageTags", "canShareViews")
            || !Bool(permissions, "canAssignTags", out var assign) || !Bool(permissions, "canManageFolders", out var folders)
            || !Bool(permissions, "canManageTags", out var tagsFlag) || !Bool(permissions, "canShareViews", out var share)) return null;
        var organization = Text(body, "organization");
        if (organization is not ("enabled" or "disabled")) return null;
        return new JsonObject
        {
            ["revision"] = Text(body, "revision"), ["query"] = admittedQuery, ["view"] = view, ["columns"] = Array(columns), ["selection"] = Array(selection),
            ["items"] = admittedItems, ["itemsOmitted"] = omitted, ["totalCount"] = totalCount,
            ["permissions"] = new JsonObject { ["canAssignTags"] = assign, ["canManageFolders"] = folders, ["canManageTags"] = tagsFlag, ["canShareViews"] = share },
            ["organization"] = organization
        };
    }

    /// <summary>The visible library query: search, folder, tag and value filters, favorites, sort and publication filter.</summary>
    internal static JsonObject? ReadQuery(JsonElement query)
    {
        if (!Only(query, "search", "state", "folderId", "unfiled", "includeDescendants", "tagFilters", "favoritesOnly", "sort", "publicationState")
            || !OptionalText(query, "search", 256) || !OptionalText(query, "folderId", 64) || !OptionalText(query, "publicationState", 32)
            || Text(query, "state") is not ("active" or "deleted" or "all") || Text(query, "sort") is not { Length: > 0 and <= 32 }
            || !Bool(query, "unfiled", out var unfiled) || !Bool(query, "includeDescendants", out var descendants) || !Bool(query, "favoritesOnly", out var favorites))
            return null;
        var filters = new JsonArray();
        if (query.TryGetProperty("tagFilters", out var tagFilters))
        {
            if (tagFilters.ValueKind != JsonValueKind.Array || tagFilters.GetArrayLength() > 16) return null;
            foreach (var filter in tagFilters.EnumerateArray())
            {
                if (!Only(filter, "tagId", "operator", "valueIds") || !Valid(Text(filter, "tagId"), 64) || Text(filter, "operator") is not { Length: > 0 and <= 16 }
                    || !Strings(filter, "valueIds", 32, 64, out var values)) return null;
                filters.Add(new JsonObject { ["tagId"] = Text(filter, "tagId"), ["operator"] = Text(filter, "operator"), ["valueIds"] = Array(values) });
            }
        }
        return new JsonObject
        {
            ["search"] = Text(query, "search"), ["state"] = Text(query, "state"), ["folderId"] = Text(query, "folderId"), ["unfiled"] = unfiled,
            ["includeDescendants"] = descendants, ["tagFilters"] = filters, ["favoritesOnly"] = favorites, ["sort"] = Text(query, "sort"),
            ["publicationState"] = Text(query, "publicationState")
        };
    }

    private static JsonObject? ReadCreateDialog(JsonElement body)
    {
        if (!Only(body, "revision", "name", "description", "rootActivityVersionId", "templateId", "templates")
            || !OptionalText(body, "name", 256) || !OptionalText(body, "description", 2000)
            || !OptionalText(body, "rootActivityVersionId", 256) || !OptionalText(body, "templateId", 128)
            || ReadTemplates(body, 50) is not { } templates) return null;
        return new JsonObject
        {
            ["revision"] = Text(body, "revision"), ["name"] = Text(body, "name"), ["description"] = Text(body, "description"),
            ["rootActivityVersionId"] = Text(body, "rootActivityVersionId"), ["templateId"] = Text(body, "templateId"), ["templates"] = templates
        };
    }

    private static JsonObject? ReadTemplateList(JsonElement body)
    {
        if (!Only(body, "revision", "filter", "selection", "templates") || !OptionalText(body, "selection", 128)
            || ReadTemplates(body, 100) is not { } templates) return null;
        if (!body.TryGetProperty("filter", out var filter) || !Only(filter, "search") || !OptionalText(filter, "search", 256)) return null;
        var selection = Text(body, "selection");
        // The selection is one of the listed templates, never a workflow definition.
        if (selection is not null && !templates.Any(item => item!["id"]!.GetValue<string>() == selection)) return null;
        return new JsonObject
        {
            ["revision"] = Text(body, "revision"), ["filter"] = new JsonObject { ["search"] = Text(filter, "search") }, ["selection"] = selection,
            ["templates"] = templates
        };
    }

    /// <summary>Templates by identity and persisted revision; name and description are untrusted resource content.</summary>
    private static JsonArray? ReadTemplates(JsonElement body, int limit)
    {
        var result = new JsonArray();
        if (!body.TryGetProperty("templates", out var templates)) return result;
        if (templates.ValueKind != JsonValueKind.Array || templates.GetArrayLength() > limit) return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in templates.EnumerateArray())
        {
            if (!Only(template, "id", "name", "description", "revision") || !Valid(Text(template, "id"), 128) || !ids.Add(Text(template, "id")!)
                || !Valid(Text(template, "revision"), 64) || !OptionalText(template, "description", 500)) return null;
            result.Add(new JsonObject
            {
                ["id"] = Text(template, "id"), ["name"] = Bounded(Text(template, "name"), 256), ["description"] = Text(template, "description"),
                ["revision"] = Text(template, "revision")
            });
        }
        return result;
    }

    /// <summary>The server's read of the actor's library authority, as an authoritative attachment.</summary>
    internal static WeaverContextAttachment Organization(WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>? result, string? reason)
    {
        var available = result is { Succeeded: true, Value: not null };
        var content = JsonSerializer.SerializeToElement(new
        {
            status = available ? "available" : result?.Status == WorkflowLibraryCommandStatus.Denied ? "denied" : "unavailable",
            reason = available ? null : reason ?? result?.Message,
            permissions = available ? result!.Value : null,
            // Deletion is never a Weaver action; the library's own delete actions remain the user's.
            refused = new[] { "delete", "permanent-delete" }
        }, JsonOptions);
        var summary = available
            ? "Workflow organization is available; the permission flags are the library's own answer for this actor."
            : $"Workflow organization is unavailable: {reason ?? result?.Message}. Ordinary list support remains.";
        return new("workflows:library-organization", "workflows.library", null, "Workflow library authority", OrganizationKind,
            WeaverContextSensitivity.Internal, "permissions", summary, content, new Dictionary<string, string>())
        { ModuleId = ModuleId, SchemaVersion = "1", Origin = WeaverContextOrigin.AuthoritativeRead, AgentScopes = [] };
    }

    /// <summary>The installed library commands, or why there is none to use.</summary>
    internal static IWorkflowLibraryCommands? Commands(IEnumerable<IWorkflowLibraryCommands>? commands, out string? reason)
    {
        var current = commands?.ToArray() ?? [];
        reason = current.Length switch
        {
            0 => "The workflow library commands are not installed: no IWorkflowLibraryCommands (Workflows Design API) is registered.",
            1 => null,
            _ => "The workflow library commands are ambiguous: several IWorkflowLibraryCommands implementations are registered."
        };
        return current.Length == 1 ? current[0] : null;
    }

    internal static string? Text(JsonElement body, string property) => WorkflowWeaverSnapshot.Text(body, property);

    private static bool Only(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));

    private static bool Valid(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);

    /// <summary>Absent, null, or a string within its bound; any other value refuses the context.</summary>
    private static bool OptionalText(JsonElement body, string property, int limit) =>
        !body.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String && WorkflowWeaverSnapshot.String(value) is { } text && text.Length <= limit;

    private static string? Bounded(string? value, int limit) => value is null ? null : value[..Math.Min(value.Length, limit)];

    private static bool Bool(JsonElement body, string property, out bool value)
    {
        value = false;
        if (!body.TryGetProperty(property, out var element)) return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    private static bool Strings(JsonElement body, string property, int count, int length, out string[] values)
    {
        values = [];
        if (!body.TryGetProperty(property, out var array)) return true;
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > count) return false;
        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (WorkflowWeaverSnapshot.String(item) is not { } text || !Valid(text, length) || result.Contains(text, StringComparer.Ordinal)) return false;
            result.Add(text);
        }
        values = result.ToArray();
        return true;
    }

    private static bool TagReferences(JsonElement body, string property, out JsonArray tags)
    {
        tags = [];
        if (!body.TryGetProperty(property, out var array)) return true;
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 16) return false;
        foreach (var item in array.EnumerateArray())
        {
            if (!Only(item, "tagId", "valueId") || !Valid(Text(item, "tagId"), 64) || !OptionalText(item, "valueId", 64)) return false;
            tags.Add(new JsonObject { ["tagId"] = Text(item, "tagId"), ["valueId"] = Text(item, "valueId") });
        }
        return true;
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
