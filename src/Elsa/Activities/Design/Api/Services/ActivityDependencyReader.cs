using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Services;

public sealed class ActivityDependencyReaderOptions
{
    public int DefaultPageSize { get; set; } = 100;
    public int MaximumPageSize { get; set; } = 500;
}

public sealed class ActivityDependencyCursorOptions
{
    public string SigningKey { get; set; } = null!;
}

public interface IActivityDependencyCursorCodec
{
    string Encode(ActivityDependencyCursorState state);
    ActivityDependencyCursorState Decode(string cursor);
}

public sealed record ActivityDependencyCursorState(
    string TenantScope,
    string AuthorizationProfileFingerprint,
    string RootVersionId,
    string Direction,
    bool Transitive,
    IReadOnlyList<string> Include,
    string Watermark,
    int Position);

public sealed class ActivityDependencyCursorInvalidException : Exception
{
    public ActivityDependencyCursorInvalidException() : base("The activity dependency cursor is invalid.") { }
}

public sealed class HmacActivityDependencyCursorCodec : IActivityDependencyCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public HmacActivityDependencyCursorCodec(IOptions<ActivityDependencyCursorOptions> options)
    {
        var signingKey = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Activity dependency cursor signing key must contain at least 32 UTF-8 bytes.");
        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string Encode(ActivityDependencyCursorState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    public ActivityDependencyCursorState Decode(string cursor)
    {
        try
        {
            var parts = cursor.Split('.');
            if (parts is not [var payloadPart, var signaturePart]) throw new ActivityDependencyCursorInvalidException();
            var payload = FromBase64Url(payloadPart);
            var signature = FromBase64Url(signaturePart);
            var expected = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected)) throw new ActivityDependencyCursorInvalidException();
            var state = JsonSerializer.Deserialize<ActivityDependencyCursorState>(payload, JsonOptions);
            if (state is null || state.Position < 0 || string.IsNullOrWhiteSpace(state.Watermark))
                throw new ActivityDependencyCursorInvalidException();
            return state;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ActivityDependencyCursorInvalidException)
        {
            throw new ActivityDependencyCursorInvalidException();
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}

public sealed class ActivityDependencyReader(
    IActivityDefinitionVersionPublicationStore publicationStore,
    IActivityDirectDependencyStore directStore,
    IActivityDependencyProjectionStore projectionStore,
    IActivityDependencyCursorCodec cursorCodec,
    IActivityDependencyAuthorizationContext authorization,
    IOptions<ActivityDependencyReaderOptions> options)
{
    public async Task<ActivityDependencyPageView> ReadAsync(
        string rootVersionId,
        string direction,
        bool transitive,
        string include,
        string? cursor,
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(direction, transitive, include);
        var limit = ValidateLimit(requestedLimit);
        var rootPublication = await publicationStore.FindAsync(rootVersionId, cancellationToken)
            ?? throw NotFound();
        var root = Reference(rootPublication);
        EnsureRootVisible(root);
        EnsureAuthorizationProfile();

        ActivityDependencyCursorState? cursorState = null;
        if (cursor is not null)
        {
            try
            {
                cursorState = cursorCodec.Decode(cursor);
            }
            catch (ActivityDependencyCursorInvalidException exception)
            {
                throw BindingMismatch(exception);
            }
            EnsureBinding(cursorState, rootVersionId, query);
        }

        var page = query is { Direction: ActivityDependencyDirection.Outbound, Transitive: false }
            ? await ReadAuthoritativeAsync(rootPublication, query, cursorState, limit, cancellationToken)
            : await ReadDerivedAsync(rootVersionId, query, cursorState, limit, cancellationToken);
        return page.ToView();
    }

    private async Task<ActivityDependencyPage> ReadAuthoritativeAsync(
        ActivityDefinitionVersionPublication rootPublication,
        ActivityDependencyQuery query,
        ActivityDependencyCursorState? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var edges = await directStore.ListOutboundAsync(rootPublication.DefinitionVersionId, cancellationToken);
        var items = new List<ActivityDependencyItem>(edges.Count);
        foreach (var edge in edges)
        {
            var dependency = await publicationStore.FindAsync(edge.DependencyVersionId, cancellationToken)
                ?? throw OperationFailed("An authoritative dependency edge references an unavailable version.");
            var ownerReference = Reference(rootPublication);
            var dependencyReference = Reference(dependency);
            var item = new ActivityDependencyItem(
                edge.Id,
                ownerReference,
                dependencyReference,
                new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
                true,
                1,
                [ownerReference, dependencyReference]);
            if (Visible(item.Owner) && Visible(item.Dependency) && authorization.CanRead(item.Owner) && authorization.CanRead(item.Dependency) && Included(item.Owner.Kind, query.Include))
                items.Add(item);
        }

        var ordered = Order(items);
        var watermark = Watermark(Reference(rootPublication), ordered);
        if (cursor is not null && !StringComparer.Ordinal.Equals(cursor.Watermark, watermark))
            throw CursorExpired();
        var offset = cursor?.Position ?? 0;
        if (offset > ordered.Count) throw BindingMismatch();
        var selected = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + selected.Length;
        var nextCursor = nextOffset < ordered.Count ? EncodeCursor(rootPublication.DefinitionVersionId, query, watermark, nextOffset) : null;
        return new(
            Reference(rootPublication),
            query,
            new(ActivityDependencyConsistencyKind.AuthoritativeDirect, true, null, null),
            selected,
            nextCursor);
    }

    private async Task<ActivityDependencyPage> ReadDerivedAsync(
        string rootVersionId,
        ActivityDependencyQuery query,
        ActivityDependencyCursorState? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var position = cursor?.Position ?? 0;
        var watermark = cursor?.Watermark;
        var items = new List<ActivityDependencyItem>(limit);
        ActivityDependencyProjectionSlice? last = null;
        var hasMore = false;
        try
        {
            while (items.Count < limit)
            {
                var slice = await projectionStore.ReadAsync(new(
                    rootVersionId,
                    query,
                    authorization.TenantId,
                    watermark,
                    position,
                    limit), cancellationToken);
                last = slice;
                watermark ??= slice.Watermark;
                if (!StringComparer.Ordinal.Equals(watermark, slice.Watermark)) throw CursorExpired();
                var consumed = 0;
                foreach (var item in slice.Items)
                {
                    consumed++;
                    position++;
                    if (Visible(item.Owner) && Visible(item.Dependency) && authorization.CanRead(item.Owner) && authorization.CanRead(item.Dependency))
                        items.Add(item);
                    if (items.Count == limit)
                    {
                        hasMore = consumed < slice.Items.Count || slice.NextOffset is not null;
                        break;
                    }
                }
                if (items.Count == limit) break;
                if (slice.NextOffset is null)
                    break;
                position = slice.NextOffset.Value;
            }
        }
        catch (ActivityDependencyWatermarkExpiredException exception)
        {
            throw CursorExpired(exception);
        }

        if (last is null) throw OperationFailed("The dependency projection returned no snapshot.");
        var nextCursor = hasMore || last.NextOffset is not null ? EncodeCursor(rootVersionId, query, watermark!, position) : null;
        return new(last.Root, query, last.Consistency, items, nextCursor);
    }

    private ActivityDependencyQuery ParseQuery(string direction, bool transitive, string include)
    {
        if (!Enum.TryParse<ActivityDependencyDirection>(direction, true, out var parsedDirection))
            throw BadRequest("'direction' must be 'outbound' or 'inbound'.");
        var included = include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant() switch
            {
                "versions" => "Versions",
                "drafts" => "Drafts",
                _ => throw BadRequest("'include' supports only 'versions' and 'drafts'.")
            })
            .ToHashSet(StringComparer.Ordinal);
        if (included.Count == 0) throw BadRequest("'include' must select at least one owner kind.");
        return new(parsedDirection, transitive, included);
    }

    private int ValidateLimit(int? requested)
    {
        var configured = options.Value;
        if (configured.DefaultPageSize <= 0 || configured.MaximumPageSize <= 0 || configured.DefaultPageSize > configured.MaximumPageSize)
            throw OperationFailed("Activity dependency page-size options are invalid.");
        var limit = requested ?? configured.DefaultPageSize;
        if (limit <= 0 || limit > configured.MaximumPageSize)
            throw BadRequest($"'limit' must be between 1 and {configured.MaximumPageSize}.");
        return limit;
    }

    private void EnsureBinding(ActivityDependencyCursorState cursor, string rootVersionId, ActivityDependencyQuery query)
    {
        if (!StringComparer.Ordinal.Equals(cursor.TenantScope, TenantScope()) ||
            !StringComparer.Ordinal.Equals(cursor.AuthorizationProfileFingerprint, AuthorizationProfileFingerprint()) ||
            !StringComparer.Ordinal.Equals(cursor.RootVersionId, rootVersionId) ||
            !StringComparer.OrdinalIgnoreCase.Equals(cursor.Direction, query.Direction.ToString()) ||
            cursor.Transitive != query.Transitive ||
            !cursor.Include.SequenceEqual(query.Include.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw BindingMismatch();
    }

    private string EncodeCursor(string rootVersionId, ActivityDependencyQuery query, string watermark, int position) =>
        cursorCodec.Encode(new(
            TenantScope(),
            AuthorizationProfileFingerprint(),
            rootVersionId,
            query.Direction.ToString(),
            query.Transitive,
            query.Include.Order(StringComparer.Ordinal).ToArray(),
            watermark,
            position));

    private void EnsureRootVisible(ActivityDefinitionReference root)
    {
        if (!Visible(root) || !authorization.CanRead(root))
            throw new ActivityAuthoringException(403, "activity.tenant.reference-denied", "Activity dependency query is forbidden", "The requested activity version is outside the caller's authorized scope.");
    }

    private void EnsureAuthorizationProfile()
    {
        if (string.IsNullOrWhiteSpace(authorization.AuthorizationProfile))
            throw OperationFailed("The dependency authorization profile is not configured.");
    }

    private bool Visible(ActivityDefinitionReference reference) =>
        reference.TenantId is null || StringComparer.Ordinal.Equals(reference.TenantId, authorization.TenantId);

    private string TenantScope() => authorization.TenantId is null ? "global:" : $"tenant:{authorization.TenantId}";

    private string AuthorizationProfileFingerprint() =>
        $"sha256-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authorization.AuthorizationProfile))).ToLowerInvariant()}";

    private static bool Included(string ownerKind, IReadOnlySet<string> include) => ownerKind switch
    {
        "ActivityVersion" or "WorkflowVersion" => include.Contains("Versions"),
        "ActivityDraft" or "WorkflowDraft" => include.Contains("Drafts"),
        _ => false
    };

    private static IReadOnlyList<ActivityDependencyItem> Order(IEnumerable<ActivityDependencyItem> items) => items
        .OrderBy(x => x.Depth)
        .ThenBy(x => x.Owner.Kind, StringComparer.Ordinal)
        .ThenBy(x => OwnerIdentity(x.Owner), StringComparer.Ordinal)
        .ThenBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal)
        .ThenBy(x => x.Dependency.VersionId, StringComparer.Ordinal)
        .ThenBy(x => x.RelationshipId, StringComparer.Ordinal)
        .ToArray();

    private static string OwnerIdentity(ActivityDefinitionReference reference) =>
        reference.VersionId ?? reference.DraftId ?? reference.DefinitionId;

    private static string Watermark(ActivityDefinitionReference root, IReadOnlyList<ActivityDependencyItem> items)
    {
        var canonical = new StringBuilder();
        Append(root.Kind); Append(root.VersionId); Append(root.Lifecycle?.ToString());
        foreach (var item in items)
        {
            Append(item.RelationshipId);
            Append(item.Owner.Kind);
            Append(OwnerIdentity(item.Owner));
            Append(item.Owner.Lifecycle?.ToString());
            Append(item.Occurrence.OccurrenceId);
            Append(item.Dependency.VersionId);
            Append(item.Dependency.TemplateHash);
            Append(item.Dependency.Lifecycle?.ToString());
        }
        return $"sha256-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant()}";

        void Append(string? value) => canonical.Append(value?.Length ?? -1).Append(':').Append(value).Append(';');
    }

    private static ActivityDefinitionReference Reference(ActivityDefinitionVersionPublication publication) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: publication.TenantId,
        Lifecycle: publication.Lifecycle);

    private static ActivityAuthoringException NotFound() =>
        new(404, "activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
    private static ActivityAuthoringException BadRequest(string detail) =>
        new(400, "activity.request.invalid", "Invalid activity dependency query", detail);
    private static ActivityAuthoringException BindingMismatch(Exception? inner = null) =>
        new(409, "activity.cursor.binding-mismatch", "Activity dependency cursor does not match", "The dependency cursor does not belong to this query or authorization scope.", innerException: inner);
    private static ActivityAuthoringException CursorExpired(Exception? inner = null) =>
        new(410, "activity.cursor.expired", "Activity dependency cursor expired", "The dependency projection snapshot used by this cursor is no longer available.", innerException: inner);
    private static ActivityAuthoringException OperationFailed(string detail) =>
        new(500, "activity.operation.failed", "Activity dependency query failed", detail);
}
