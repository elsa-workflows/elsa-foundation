using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;

namespace Elsa.Studio.Dashboard.Management.Resources;

internal sealed class DashboardResourceService(IDashboardResourceStore store, IDashboardDefinitionValidator definitions,
    TimeProvider clock, IDashboardAudienceDirectory? directory = null) : IDashboardResourceService
{
    public async ValueTask<DashboardResourcePage> ListAsync(DashboardActor actor, string kind, string? ownership = null,
        string? search = null, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ValidateActor(actor); ValidateKind(kind); Require(actor, kind, "read");
        if (ownership is not (null or "mine" or "shared")) Invalid("Ownership must be mine or shared.");
        var documents = await store.ListAsync(actor.TenantId, kind, cancellationToken);
        var visible = documents.Where(document => CanRead(actor, document))
            .Where(document => ownership is null || (ownership == "mine") == IsOwner(actor, document))
            .Where(document => string.IsNullOrWhiteSpace(search) || document.Content.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(document => document.Content.Name, StringComparer.OrdinalIgnoreCase).ThenBy(document => document.Id, StringComparer.Ordinal).ToArray();
        var scope = Fingerprint(new { actor.TenantId, actor.UserId, kind, ownership, search });
        var offset = 0;
        if (continuationToken is not null)
        {
            try
            {
                if (continuationToken.Length > 512) Invalid("The continuation is invalid. Reload this collection.");
                var cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(continuationToken));
                if (cursor is null || cursor.Scope != scope) Invalid("The continuation belongs to another collection scope.");
                var position = Array.FindIndex(visible, document => document.Id == cursor.Id);
                if (position < 0) Invalid("The continuation expired after collection or access changes. Reload this collection.");
                offset = position + 1;
            }
            catch (Exception error) when (error is JsonException or FormatException) { Invalid("The continuation is invalid. Reload this collection."); }
        }
        var result = new List<DashboardResourceView>();
        foreach (var document in visible.Skip(offset).Take(100)) result.Add(await ProjectAsync(actor, document, cancellationToken, includeUses: false));
        var continuation = offset + result.Count < visible.Length ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(scope, result[^1].Id))) : null;
        return new(result, continuation);
    }

    public async ValueTask<DashboardResourceView> GetAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default) =>
        await ProjectAsync(actor, await ReadAsync(actor, kind, id, cancellationToken), cancellationToken);

    public async ValueTask<DashboardResourceDocument> GetAuthorizedQuestionAsync(DashboardActor actor, string id, CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(actor, DashboardResourceKinds.Question, id, cancellationToken);
        if (document.Content.Definition is null || !await definitions.CanDiscloseAsync(actor, document.Content.Definition, cancellationToken))
            throw new DashboardResourceException("denied", "The question definition is not available under your current field access.");
        return document;
    }

    public async ValueTask<DashboardResourceView> CreateAsync(DashboardActor actor, string kind, DashboardResourceInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor); ValidateKind(kind); Require(actor, kind, "create"); Require(actor, kind, "read");
        var key = OperationKey(input.OperationKey);
        var fingerprint = Fingerprint(new { kind, input });
        if (await store.FindCreatedAsync(actor.TenantId, actor.UserId, kind, key, fingerprint, cancellationToken) is { } replay)
            return await ProjectAsync(actor, replay, cancellationToken);
        var content = await NormalizeAsync(actor, kind, input, null, cancellationToken);
        var now = clock.GetUtcNow();
        var document = new DashboardResourceDocument($"{kind}-{Guid.NewGuid():N}", kind, actor.TenantId, actor.UserId,
            NewRevision(), content, [], [], now, now) { UpdatedBy = actor.UserId };
        var saved = await WriteAsync(new(document, null, key, fingerprint), cancellationToken);
        return await ProjectAsync(actor, saved, cancellationToken);
    }

    public async ValueTask<DashboardResourceView> UpdateAsync(DashboardActor actor, string kind, string id, string expectedRevision,
        DashboardResourceInput input, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken);
        RequireEdit(actor, current); CheckRevision(current, expectedRevision);
        if (input.OperationKey is not null) Invalid("Operation keys apply only to creation and copy.");
        var content = await NormalizeAsync(actor, kind, input, current, cancellationToken);
        var next = Next(current, content, actor.UserId);
        return await ProjectAsync(actor, await WriteAsync(new(next, current.Revision), cancellationToken), cancellationToken);
    }

    public async ValueTask DeleteAsync(DashboardActor actor, string kind, string id, string expectedRevision, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken);
        RequireOwner(actor, current, "delete"); CheckRevision(current, expectedRevision);
        await WriteAsync(new(Next(current, current.Content, actor.UserId), current.Revision, Delete: true), cancellationToken);
    }

    public async ValueTask<DashboardAudiencePage> GetAudienceAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(actor, kind, id, cancellationToken);
        RequireOwner(actor, document, "share"); return new(document.Audience);
    }

    public async ValueTask<DashboardResourceView> SetAudienceAsync(DashboardActor actor, string kind, string id, string expectedRevision,
        IReadOnlyList<DashboardAudienceGrant> audience, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken);
        RequireOwner(actor, current, "share"); CheckRevision(current, expectedRevision);
        if (audience is null || audience.Count > 100 || audience.Any(grant => grant is null) ||
            audience.Select(grant => (grant.Kind, grant.Id)).Distinct().Count() != audience.Count)
            Invalid("An audience has at most 100 distinct user or role entries.");
        foreach (var grant in audience)
        {
            if (grant.Kind is not ("user" or "role") || grant.Access is not ("viewer" or "editor") || string.IsNullOrWhiteSpace(grant.Id) || grant.Id.Length > 256)
                Invalid("Audience entries require a supported identity and viewer or editor access.");
            if (directory is null)
                throw new DashboardResourceException("unavailable", "Audience resolution is not configured for this host.");
            if (!await directory.IsValidAsync(actor, grant, cancellationToken))
                Invalid("An audience entry is unavailable or cannot be shared with in the current tenant.");
        }
        var next = Next(current, current.Content, actor.UserId) with { Audience = audience.ToArray() };
        return await ProjectAsync(actor, await WriteAsync(new(next, current.Revision), cancellationToken), cancellationToken);
    }

    public async ValueTask<DashboardResourceView> CopyAsync(DashboardActor actor, string kind, string id, string expectedRevision,
        DashboardCopyInput input, CancellationToken cancellationToken = default)
    {
        ValidateActor(actor); ValidateKind(kind); Require(actor, kind, "create"); Require(actor, kind, "read");
        var key = OperationKey(input.OperationKey);
        var fingerprint = Fingerprint(new { kind, id, expectedRevision, input });
        if (await store.FindCreatedAsync(actor.TenantId, actor.UserId, kind, key, fingerprint, cancellationToken) is { } replay)
            return await ProjectAsync(actor, replay, cancellationToken);
        var source = await ReadAsync(actor, kind, id, cancellationToken); CheckRevision(source, expectedRevision);
        if (source.Content.Definition is { } definition && !await definitions.CanDiscloseAsync(actor, definition, cancellationToken))
            throw new DashboardResourceException("denied", "Copy requires access to the complete question definition.");
        var content = source.Content with { Name = Name(input.Name), Description = Description(input.Description),
            Tiles = source.Content.Tiles.Select(tile => tile with { Id = $"tile-{Guid.NewGuid():N}" }).ToArray() };
        ValidateContentSize(kind, content);
        var now = clock.GetUtcNow();
        var copy = new DashboardResourceDocument($"{kind}-{Guid.NewGuid():N}", kind, actor.TenantId, actor.UserId, NewRevision(),
            content, [], [], now, now) { UpdatedBy = actor.UserId };
        return await ProjectAsync(actor, await WriteAsync(new(copy, null, key, fingerprint), cancellationToken), cancellationToken);
    }

    public async ValueTask<DashboardHistoryPage> HistoryAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken);
        return new(new[] { new DashboardHistoryItem(current.Revision, current.Content.Name, current.Content.Description, LastModifier(current), current.UpdatedAt) }
            .Concat(current.History.Select(item => new DashboardHistoryItem(item.Revision, item.Content.Name, item.Content.Description, item.ActorId, item.UpdatedAt))).ToArray());
    }

    public async ValueTask<DashboardResourceView> GetRevisionAsync(DashboardActor actor, string kind, string id, string revision, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken);
        if (revision == current.Revision) return await ProjectAsync(actor, current, cancellationToken);
        var previous = current.History.SingleOrDefault(item => item.Revision == revision)
            ?? throw new DashboardResourceException("notFound", "This revision is unavailable or has expired.");
        return await ProjectAsync(actor, current with { Content = previous.Content, Revision = previous.Revision, UpdatedAt = previous.UpdatedAt }, cancellationToken);
    }

    public async ValueTask<DashboardResourceView> RestoreAsync(DashboardActor actor, string kind, string id, string expectedRevision,
        string revision, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(actor, kind, id, cancellationToken); RequireEdit(actor, current); CheckRevision(current, expectedRevision);
        var history = current.History.SingleOrDefault(item => item.Revision == revision)
            ?? throw new DashboardResourceException("notFound", "This revision is unavailable or has expired.");
        var content = await NormalizeAsync(actor, kind, new() { Name = history.Content.Name, Description = history.Content.Description,
            Definition = history.Content.Definition, Tiles = history.Content.Tiles.Select(tile => new DashboardTileInput {
                Id = tile.Id, QuestionId = tile.QuestionId, Size = tile.Size, Presentation = tile.Presentation, Parameters = tile.Parameters }).ToArray() }, null, cancellationToken);
        return await ProjectAsync(actor, await WriteAsync(new(Next(current, content, actor.UserId), current.Revision), cancellationToken), cancellationToken);
    }

    private async ValueTask<DashboardResourceContent> NormalizeAsync(DashboardActor actor, string kind, DashboardResourceInput input,
        DashboardResourceDocument? current, CancellationToken cancellationToken)
    {
        if (input.Tiles is null || input.Tiles.Any(tile => tile is null)) Invalid("Tiles must be a collection of tile definitions.");
        var name = Name(input.Name); var description = Description(input.Description);
        DashboardQueryRequest? definition = null; var tiles = new List<DashboardTile>();
        if (kind == DashboardResourceKinds.Question)
        {
            if (input.Tiles.Count != 0) Invalid("Questions cannot contain dashboard tiles.");
            if (input.PreserveDefinition)
            {
                if (current?.Content.Definition is null || input.Definition is not null) Invalid("Preserve definition requires an existing question and no replacement definition.");
                definition = current!.Content.Definition;
            }
            else
            {
                definition = input.Definition ?? throw new DashboardResourceException("validation", "A question definition is required.");
                if (definition.Query is null || definition.Visualization is null || definition.ParameterValues is null ||
                    definition.Query.Parameters is null || definition.Query.Parameters.Any(parameter => parameter is null))
                    Invalid("A question requires a query, visualization and typed parameter collections.");
                await definitions.ValidateAsync(actor, definition, cancellationToken);
                definition = definition with { Query = definition.Query with { Parameters = definition.Query.Parameters.Select(parameter =>
                    definition.ParameterValues.TryGetValue(parameter.Name, out var accepted) ? parameter with { Default = accepted } : parameter).ToArray() } };
            }
        }
        else
        {
            if (input.Definition is not null || input.PreserveDefinition) Invalid("Dashboards contain question references, not a question definition.");
            if (input.Tiles.Count > 50 || input.Tiles.Select(tile => tile.Id).Distinct(StringComparer.Ordinal).Count() != input.Tiles.Count)
                Invalid("A dashboard contains at most 50 tiles with distinct identities.");
            foreach (var tile in input.Tiles)
            {
                if (string.IsNullOrWhiteSpace(tile.Id) || tile.Id.Length > 128 || tile.Size is not ("small" or "medium" or "wide" or "full"))
                    Invalid("A tile requires a bounded identity and supported size.");
                if (tile.PreserveReference)
                {
                    if (tile.QuestionId is not null || tile.Presentation is not null || tile.Parameters is not null) Invalid("Preserved references cannot replace hidden question content.");
                    var stored = current?.Content.Tiles.SingleOrDefault(item => item.Id == tile.Id)
                        ?? throw new DashboardResourceException("validation", "Only this dashboard's existing tile can preserve its reference.");
                    tiles.Add(stored with { Size = tile.Size });
                }
                else
                {
                    var question = await GetAuthorizedQuestionAsync(actor, tile.QuestionId ?? "", cancellationToken);
                    var original = question.Content.Definition!;
                    var effective = EffectiveDefinition(original, tile.Presentation, tile.Parameters);
                    await definitions.ValidateAsync(actor, effective, cancellationToken);
                    tiles.Add(new(tile.Id, question.Id, tile.Size, tile.Presentation, tile.Parameters));
                }
            }
        }
        var content = new DashboardResourceContent(name, description, definition, tiles);
        ValidateContentSize(kind, content);
        return content;
    }

    private static void ValidateContentSize(string kind, DashboardResourceContent content)
    {
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(content)) > (kind == DashboardResourceKinds.Question ? 65_536 : 131_072))
            Invalid("The resource definition exceeds its supported size.");
    }

    private async ValueTask<DashboardResourceDocument> ReadAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken)
    {
        ValidateActor(actor); ValidateKind(kind);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128) throw Missing();
        var document = await store.FindAsync(actor.TenantId, kind, id, cancellationToken);
        if (document is null || !CanRead(actor, document)) throw Missing();
        return document;
    }

    private async ValueTask<DashboardResourceView> ProjectAsync(DashboardActor actor, DashboardResourceDocument document,
        CancellationToken cancellationToken, bool includeUses = true)
    {
        if (!CanRead(actor, document)) throw Missing();
        var definition = document.Content.Definition;
        var withheld = definition is not null && !await definitions.CanDiscloseAsync(actor, definition, cancellationToken);
        var tiles = new List<DashboardTileView>();
        foreach (var tile in document.Content.Tiles)
        {
            var question = await store.FindAsync(actor.TenantId, DashboardResourceKinds.Question, tile.QuestionId, cancellationToken);
            var available = question is not null && CanRead(actor, question) && question.Content.Definition is { } content &&
                await definitions.CanDiscloseAsync(actor, content, cancellationToken) &&
                await definitions.CanDiscloseAsync(actor, EffectiveDefinition(content, tile.Presentation, tile.Parameters), cancellationToken);
            tiles.Add(available ? new(tile.Id, tile.QuestionId, tile.Size, "available", tile.Presentation, tile.Parameters)
                : new(tile.Id, null, tile.Size, "unavailable"));
        }
        var owner = IsOwner(actor, document); var canEdit = owner || Grant(actor, document) == "editor";
        var caps = new DashboardResourceCapabilities(true, canEdit && Has(actor, document.Kind, "edit"), owner && Has(actor, document.Kind, "share"),
            owner && Has(actor, document.Kind, "delete"), Has(actor, document.Kind, "create") && !withheld);
        IReadOnlyList<DashboardQuestionUse>? uses = null;
        if (includeUses && document.Kind == DashboardResourceKinds.Question)
            uses = Has(actor, DashboardResourceKinds.Dashboard, "read")
                ? (await store.ListAsync(actor.TenantId, DashboardResourceKinds.Dashboard, cancellationToken))
                    .Where(dashboard => dashboard.Kind == DashboardResourceKinds.Dashboard && CanRead(actor, dashboard))
                    .Select(dashboard => new DashboardQuestionUse(dashboard.Id, dashboard.Content.Name,
                        dashboard.Content.Tiles.Count(tile => tile.QuestionId == document.Id)))
                    .Where(use => use.TileCount != 0)
                    .OrderBy(use => use.Name, StringComparer.OrdinalIgnoreCase).ThenBy(use => use.Id, StringComparer.Ordinal).ToArray()
                : [];
        return new(document.Id, document.Kind, document.OwnerId, document.Revision, document.Content.Name, document.Content.Description,
            caps, withheld ? null : definition, withheld ? "withheld" : "available", tiles, document.CreatedAt, document.UpdatedAt) { Uses = uses };
    }

    private DashboardResourceDocument Next(DashboardResourceDocument current, DashboardResourceContent content, string actorId) =>
        current with { Content = content, Revision = NewRevision(), UpdatedAt = clock.GetUtcNow(), UpdatedBy = actorId,
            History = new[] { new DashboardResourceRevision(current.Revision, current.Content, LastModifier(current), current.UpdatedAt) }.Concat(current.History).Take(20).ToArray() };

    private static string LastModifier(DashboardResourceDocument document) => !string.IsNullOrWhiteSpace(document.UpdatedBy)
        ? document.UpdatedBy : document.History.Count == 0 && document.CreatedAt == document.UpdatedAt ? document.OwnerId : "unknown";

    private async ValueTask<DashboardResourceDocument> WriteAsync(DashboardResourceWrite write, CancellationToken cancellationToken)
    {
        var result = await store.WriteAsync(write, cancellationToken);
        return result.Status switch {
            "saved" or "replayed" => result.Document!,
            "conflict" => throw new DashboardResourceException("conflict", "This resource changed. Reload and review your changes, or save a copy."),
            "notFound" => throw Missing(),
            _ => throw new DashboardResourceException("unavailable", "Resource persistence is unavailable.") };
    }

    private static bool IsOwner(DashboardActor actor, DashboardResourceDocument document) => document.OwnerId == actor.UserId;
    private static string? Grant(DashboardActor actor, DashboardResourceDocument document) => document.Audience
        .Where(grant => grant.Kind == "user" && grant.Id == actor.UserId || grant.Kind == "role" && actor.RoleIds.Contains(grant.Id))
        .Select(grant => grant.Access).OrderByDescending(access => access == "editor").FirstOrDefault();
    private static bool CanRead(DashboardActor actor, DashboardResourceDocument document) => actor.TenantId == document.TenantId &&
        Has(actor, document.Kind, "read") && (IsOwner(actor, document) || Grant(actor, document) is not null);
    private static bool Has(DashboardActor actor, string kind, string verb) => actor.Permissions.Contains("*") ||
        actor.Permissions.Contains($"studio/{(kind == DashboardResourceKinds.Question ? "questions" : "dashboards")}:{verb}");
    private static void Require(DashboardActor actor, string kind, string verb) { if (!Has(actor, kind, verb)) throw new DashboardResourceException("denied", "This action requires additional permission."); }
    private static void RequireEdit(DashboardActor actor, DashboardResourceDocument document)
    { Require(actor, document.Kind, "edit"); if (!IsOwner(actor, document) && Grant(actor, document) != "editor") throw new DashboardResourceException("denied", "This resource is read-only."); }
    private static void RequireOwner(DashboardActor actor, DashboardResourceDocument document, string verb)
    { Require(actor, document.Kind, verb); if (!IsOwner(actor, document)) throw new DashboardResourceException("denied", "Only the owner can change sharing or delete this resource."); }
    private static void CheckRevision(DashboardResourceDocument document, string revision)
    { if (string.IsNullOrWhiteSpace(revision)) throw new DashboardResourceException("preconditionRequired", "An exact resource revision is required."); if (revision != document.Revision) throw new DashboardResourceException("conflict", "The resource changed. Reload before saving."); }
    private static void ValidateActor(DashboardActor actor)
    { if (string.IsNullOrWhiteSpace(actor.TenantId) || string.IsNullOrWhiteSpace(actor.UserId) || actor.TenantId.Length > 256 || actor.UserId.Length > 256) throw new DashboardResourceException("unauthenticated", "A canonical authenticated user and tenant are required."); }
    private static void ValidateKind(string kind) { if (kind is not (DashboardResourceKinds.Question or DashboardResourceKinds.Dashboard)) Invalid("Unknown resource kind."); }
    private static string Name(string value) { value = value?.Trim() ?? ""; if (value.Length is < 1 or > 200) Invalid("A name of 1–200 characters is required."); return value; }
    private static string? Description(string? value) { if (value?.Length > 2_000) Invalid("Description exceeds 2,000 characters."); return value; }
    private static string OperationKey(string? value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) Invalid("A creation operation key of 1–128 characters is required."); return value!; }
    private static string NewRevision() => $"rev-{Guid.NewGuid():N}";
    private static DashboardResourceException Missing() => new("notFound", "This resource is unavailable.");
    [DoesNotReturn]
    private static void Invalid(string message) => throw new DashboardResourceException("validation", message);
    private static DashboardQueryRequest EffectiveDefinition(DashboardQueryRequest definition, DashboardVisualization? presentation,
        IReadOnlyDictionary<string, DashboardValue>? parameters)
    {
        var values = definition.ParameterValues.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var value in parameters ?? new Dictionary<string, DashboardValue>()) values[value.Key] = value.Value;
        return definition with { Visualization = presentation ?? definition.Visualization, ParameterValues = values };
    }

    private static string Fingerprint(object value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(value));
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var member in value.EnumerateObject().OrderBy(member => member.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(member.Name); WriteCanonical(writer, member.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
    private sealed record Cursor(string Scope, string Id);
}
