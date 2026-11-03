using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Workflows.Services;

/// <summary>
/// Reads the owning Activities Design catalog through its Core contract and projects it into the bounded, server-produced
/// <c>workflow.catalog</c> schema <c>1</c> attachment. Nothing here invents, narrows or caches catalog entries.
/// </summary>
internal static class WorkflowActivityCatalog
{
    internal const string ContentType = "workflow.catalog";
    internal const string AttachmentId = "workflows:catalog";
    internal const string UnavailableDiagnostic = "agent.workflow.catalog_unavailable";

    /// <summary>The serialized size the entries may take, below the host's 32 KiB per-attachment candidate budget.</summary>
    internal const int EntryBudget = 28 * 1024;

    private const string Missing = "The activity authoring catalog is not installed: no IActivityAuthoringCatalog (Activities Design API) is registered.";
    private const string Ambiguous = "The activity authoring catalog is ambiguous: several IActivityAuthoringCatalog implementations are registered.";
    private const string Failed = "The activity authoring catalog could not be read.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal enum Status { Available, Denied, Unavailable }

    internal sealed record Read(Status Status, IReadOnlyList<ActivityAuthoringCatalogEntry> Entries, string? Reason);

    /// <summary>The owner's current catalog for the request actor and tenant, or why it is denied or unavailable.</summary>
    /// <remarks>An owner failure is logged with its cause for operators; the model only ever sees the fixed unavailable text.</remarks>
    internal static async Task<Read> ReadAsync(IEnumerable<IActivityAuthoringCatalog>? catalogs, CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = catalogs?.ToArray() ?? [];
        if (current.Length != 1) return new(Status.Unavailable, [], current.Length == 0 ? Missing : Ambiguous);
        try
        {
            var result = await current[0].ListAsync(cancellationToken);
            return result.Allowed ? new(Status.Available, result.Entries, null) : new(Status.Denied, [], "The actor may not read the activity authoring catalog.");
        }
        catch (ActivityAuthoringCatalogUnavailableException error)
        {
            logger?.LogWarning(error.InnerException ?? error, "The activity authoring catalog could not be read for the workflow context.");
            return new(Status.Unavailable, [], Failed);
        }
    }

    /// <summary>The owner's current read gate only; reads no entries. Null when the gate is unavailable.</summary>
    internal static async Task<bool?> CanReadAsync(IEnumerable<IActivityAuthoringCatalog>? catalogs, CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = catalogs?.ToArray() ?? [];
        if (current.Length != 1) return null;
        try { return await current[0].CanReadAsync(cancellationToken); }
        catch (ActivityAuthoringCatalogUnavailableException error)
        {
            logger?.LogWarning(error.InnerException ?? error, "The activity authoring catalog read gate could not be decided for the workflow context.");
            return null;
        }
    }

    /// <summary>Whether a retained catalog attachment disclosed catalog entries; an unreadable one counts as disclosed (fail closed).</summary>
    internal static bool DisclosedEntries(AgentContextAttachment attachment) =>
        attachment.Content is not JsonElement { ValueKind: JsonValueKind.Object } content
        || !content.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 0;

    /// <summary>
    /// The bounded attachment for one read. Entries keep the owner's order; when they do not fit the budget the attachment
    /// says so (<c>truncated</c>, with the total and included counts) instead of silently dropping entries.
    /// </summary>
    internal static AgentContextAttachment Attachment(Read read)
    {
        var entries = new List<object>();
        var size = 0;
        int? next = null;
        foreach (var entry in read.Entries)
        {
            var projected = Project(entry);
            var length = JsonSerializer.SerializeToUtf8Bytes(projected, JsonOptions).Length + 1;
            if (size + length > EntryBudget)
            {
                // The first entry left out is what did not fit; its size lets a reader check the prefix is maximal.
                next = length;
                break;
            }
            size += length;
            entries.Add(projected);
        }
        var truncated = entries.Count < read.Entries.Count;
        var status = read.Status switch { Status.Available => "available", Status.Denied => "denied", _ => "unavailable" };
        var content = JsonSerializer.SerializeToElement(new
        {
            status, reason = read.Reason, totalCount = read.Entries.Count, includedCount = entries.Count, truncated,
            budgetBytes = EntryBudget, includedBytes = size, nextEntryBytes = next, entries
        }, JsonOptions);
        var summary = read.Status switch
        {
            Status.Available when truncated => $"The host's activity design catalog for this actor and tenant: {entries.Count} of {read.Entries.Count} entries; the rest did not fit and were left out.",
            Status.Available => $"The host's activity design catalog for this actor and tenant: {entries.Count} entries, with the availability the designer offers.",
            Status.Denied => "The actor may not read the activity design catalog; no entries are disclosed.",
            _ => "The activity design catalog is unavailable; no entries are disclosed."
        };
        return new(AttachmentId, "workflows.catalog", null, "Workflow activity catalog", ContentType, AgentContextSensitivity.Internal,
            "catalog", summary, content, new Dictionary<string, string>())
        {
            ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Origin = AgentContextOrigin.AuthoritativeRead, AgentScopes = []
        };
    }

    private static object Project(ActivityAuthoringCatalogEntry entry) => new
    {
        activityVersionId = Bounded(entry.ActivityVersionId), activityTypeKey = Bounded(entry.ActivityTypeKey), version = Bounded(entry.Version),
        displayName = Bounded(entry.DisplayName), category = BoundedOrNull(entry.Category, 256), description = BoundedOrNull(entry.Description, 200),
        available = entry.Available, availabilityReason = BoundedOrNull(entry.AvailabilityReason, 256),
        inputs = entry.Inputs.Select(input => new
        {
            referenceKey = Bounded(input.ReferenceKey), name = Bounded(input.Name), type = Bounded(input.Type),
            displayName = BoundedOrNull(input.DisplayName, 256), isRequired = input.IsRequired, isSensitive = input.IsSensitive,
            isCredential = input.IsCredential, uiHint = BoundedOrNull(input.UiHint, 64),
            // What the designer masks (sensitive, credential or password-hinted), so the catalog and the snapshot's withheld
            // values agree for a password-hinted input the owner does not declare sensitive.
            isMasked = input.IsMasked
        }).ToArray()
    };

    private static string Bounded(string? value) => BoundedOrNull(value, 256) ?? string.Empty;
    private static string? BoundedOrNull(string? value, int limit) => value is null ? null : value[..Math.Min(value.Length, limit)];
}
