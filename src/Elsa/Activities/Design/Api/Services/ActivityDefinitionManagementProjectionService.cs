using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Api.Services;

public sealed class ActivityDefinitionManagementProjectionService(
    IActivityDefinitionManagementStore store,
    IActivityAuthoringContext context,
    TimeProvider timeProvider,
    IActivityManagementCursorCodec cursorCodec)
{
    public async Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> ListDefinitionsAsync(
        ListReusableActivityDefinitions request,
        CancellationToken cancellationToken)
    {
        var authority = ParseOptional<ActivityContentAuthorityKind>(request.Authority, "authority");
        var binding = Bind(
            "definitions",
            null,
            request.Limit,
            request.Cursor,
            request.Search,
            request.Authority,
            request.ProviderKey,
            null,
            request.Sort);
        var page = await store.ReadDefinitionsAsync(new(
            context.TenantId,
            binding.Offset,
            request.Limit,
            binding.AsOf,
            Normalize(request.Search),
            authority,
            Normalize(request.ProviderKey)), cancellationToken);
        return Page(page, binding.Scope, page.Items.Select(ToDefinitionView).ToArray());
    }

    public async Task<ReusableActivityDefinitionManagementView> GetDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        var record = await store.FindDefinitionAsync(definitionId, context.TenantId, timeProvider.GetUtcNow(), cancellationToken);
        return record is null
            ? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.")
            : ToDefinitionView(record);
    }

    public async Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> ListDraftsAsync(
        ListReusableActivityDrafts request,
        CancellationToken cancellationToken)
    {
        var status = ParseOptional<ActivityDefinitionDraftStatus>(request.Status, "status");
        var binding = Bind(
            "drafts",
            request.DefinitionId,
            request.Limit,
            request.Cursor,
            request.Search,
            null,
            request.ProviderKey,
            request.Status,
            request.Sort);
        _ = await store.FindDefinitionAsync(request.DefinitionId, context.TenantId, binding.AsOf, cancellationToken)
            ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");
        var page = await store.ReadDraftsAsync(request.DefinitionId, new(
            context.TenantId,
            binding.Offset,
            request.Limit,
            binding.AsOf,
            Normalize(request.Search),
            ProviderKey: Normalize(request.ProviderKey),
            DraftStatus: status), cancellationToken);
        return Page(page, binding.Scope, page.Items.Select(ToDraftView).ToArray());
    }

    public async Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> ListVersionsAsync(
        ListReusableActivityVersions request,
        CancellationToken cancellationToken)
    {
        var lifecycle = ParseOptional<ActivityDefinitionVersionLifecycle>(request.Lifecycle, "lifecycle");
        var binding = Bind(
            "versions",
            request.DefinitionId,
            request.Limit,
            request.Cursor,
            request.Search,
            null,
            request.ProviderKey,
            request.Lifecycle,
            request.Sort);
        var definition = await store.FindDefinitionAsync(request.DefinitionId, context.TenantId, binding.AsOf, cancellationToken)
            ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");
        var page = await store.ReadVersionsAsync(request.DefinitionId, new(
            context.TenantId,
            binding.Offset,
            request.Limit,
            binding.AsOf,
            Normalize(request.Search),
            ProviderKey: Normalize(request.ProviderKey),
            VersionLifecycle: lifecycle), cancellationToken);
        var items = page.Items.Select(version => ToVersionView(version, definition.Authoring)).ToArray();
        return Page(page, binding.Scope, items);
    }

    private ReusableActivityDefinitionManagementView ToDefinitionView(ActivityDefinitionManagementRecord record) => new(
        ToIdentity(record.Definition, record.Authoring),
        new(record.DraftCount, record.VersionCount, ToReference(record.Head), ToReference(record.Recommendation)),
        DefinitionActions(record.Authoring),
        record.Definition.LastModifiedAt);

    private ReusableActivityDraftManagementView ToDraftView(ActivityDefinitionDraft draft) => new(
        new(
            draft.Id,
            draft.DefinitionId,
            draft.Revision,
            draft.SourceVersionId,
            draft.Status,
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            draft.LastModifiedAt,
            draft.PresentationLabel),
        DraftActions(draft));

    private ReusableActivityVersionManagementView ToVersionView(
        ActivityDefinitionVersionPublication version,
        ActivityDefinitionAuthoringState authoring) => new(
        new(version.DefinitionVersionId, version.DefinitionId, version.Version, version.Lifecycle, version.PublishedAt),
        version.Provider.ProviderKey,
        version.Provider.SchemaVersion,
        StringComparer.Ordinal.Equals(authoring.RecommendedVersionId, version.DefinitionVersionId),
        VersionActions(version, authoring));

    private IReadOnlyList<ActivityActionAvailabilityView> DefinitionActions(ActivityDefinitionAuthoringState authoring)
    {
        var canManage = context.CanManageActivityDefinitions;
        var designOwned = authoring.ContentAuthority.Kind == ActivityContentAuthorityKind.Design;
        return
        [
            Action("edit-definition", canManage && designOwned, canManage ? "activity.definition.source-owned" : "activity.action.forbidden"),
            Action("create-draft", canManage && designOwned, canManage ? "activity.definition.source-owned" : "activity.action.forbidden"),
            Action("set-recommendation", canManage, "activity.action.forbidden"),
            Action("fork-definition", canManage && !designOwned, canManage ? "activity.definition.design-owned" : "activity.action.forbidden")
        ];
    }

    private IReadOnlyList<ActivityActionAvailabilityView> DraftActions(ActivityDefinitionDraft draft)
    {
        var canManageActive = context.CanManageActivityDefinitions && draft.Status == ActivityDefinitionDraftStatus.Active;
        var canAuthorProvider = context.CanAuthorProvider(draft.State.Provider.ProviderKey);
        var stateUnavailable = !context.CanManageActivityDefinitions
            ? "activity.action.forbidden"
            : draft.Status != ActivityDefinitionDraftStatus.Active
                ? "activity.draft.not-active"
                : null;
        var authoringUnavailable = stateUnavailable ?? "activity.provider.authoring-forbidden";
        return
        [
            Action("edit-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("edit-draft-label", canManageActive, stateUnavailable ?? "activity.action.forbidden"),
            Action("discard-draft", canManageActive, stateUnavailable ?? "activity.action.forbidden"),
            Action("validate-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("publish-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("migrate-draft-provider", canManageActive, stateUnavailable ?? "activity.action.forbidden"),
            Action("propose-contract", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("apply-contract-proposal", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("create-conflict-copy", canManageActive, stateUnavailable ?? "activity.action.forbidden")
        ];
    }

    private IReadOnlyList<ActivityActionAvailabilityView> VersionActions(
        ActivityDefinitionVersionPublication version,
        ActivityDefinitionAuthoringState authoring)
    {
        var canManage = context.CanManageActivityDefinitions;
        var designOwned = authoring.ContentAuthority.Kind == ActivityContentAuthorityKind.Design;
        var canAuthorProvider = context.CanAuthorProvider(version.Provider.ProviderKey);
        return
        [
            Action("clone-draft", canManage && designOwned && canAuthorProvider,
                !canManage ? "activity.action.forbidden" : !designOwned ? "activity.definition.source-owned" : "activity.provider.authoring-forbidden"),
            Action("fork-definition", canManage && !designOwned,
                canManage ? "activity.definition.design-owned" : "activity.action.forbidden"),
            Action("set-recommendation", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Active,
                canManage ? "activity.version.lifecycle-ineligible" : "activity.action.forbidden"),
            Action("retire-version", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Active,
                canManage ? "activity.version.lifecycle-ineligible" : "activity.action.forbidden"),
            Action("restore-version", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Retired,
                canManage ? "activity.version.lifecycle-ineligible" : "activity.action.forbidden"),
            Action("revoke-version", canManage && version.Lifecycle != ActivityDefinitionVersionLifecycle.Revoked,
                canManage ? "activity.version.lifecycle-ineligible" : "activity.action.forbidden")
        ];
    }

    private static ActivityActionAvailabilityView Action(string action, bool allowed, string unavailableCode) =>
        new(action, allowed, allowed ? null : unavailableCode);

    private static ActivityDefinitionIdentityView ToIdentity(
        ActivityDefinition definition,
        ActivityDefinitionAuthoringState authoring) => new(
        definition.Id,
        definition.ActivityTypeKey,
        definition.TenantId,
        definition.Category,
        string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ActivityTypeKey : definition.DisplayName,
        definition.Description,
        authoring.ContentAuthority,
        null,
        authoring.HeadVersionId,
        authoring.RecommendedVersionId);

    private static ActivityDefinitionVersionReferenceView? ToReference(ActivityDefinitionVersionPublication? version) =>
        version is null
            ? null
            : new(version.DefinitionVersionId, version.Version, version.Lifecycle, version.Provider.ProviderKey, version.Provider.SchemaVersion);

    private ActivityManagementPageView<TView> Page<TEntity, TView>(
        ActivityManagementPage<TEntity> page,
        string scope,
        IReadOnlyList<TView> items) => new(
        items,
        items.Count,
        page.TotalCount,
        page.NextOffset is not null,
        page.NextOffset is { } offset ? cursorCodec.Encode(new(scope, offset, page.AsOf)) : null,
        new(Scope(scope, page.AsOf), page.AsOf));

    private CursorBinding Bind(
        string resource,
        string? definitionId,
        int limit,
        string? cursor,
        string? search,
        string? authority,
        string? providerKey,
        string? lifecycle,
        string sort)
    {
        if (limit is < 1 or > 100)
            throw Invalid("'limit' must be between 1 and 100.");
        if (!StringComparer.Ordinal.Equals(sort, "identity-asc"))
            throw Invalid("'sort' must be 'identity-asc'.");
        var scope = Scope(
            resource,
            definitionId,
            context.TenantId,
            context.AuthorizationProfile,
            limit,
            Normalize(search),
            Normalize(authority),
            Normalize(providerKey),
            Normalize(lifecycle),
            sort);
        if (string.IsNullOrWhiteSpace(cursor))
            return new(scope, 0, timeProvider.GetUtcNow());
        try
        {
            var decoded = cursorCodec.Decode(cursor);
            if (!StringComparer.Ordinal.Equals(decoded.Scope, scope) || decoded.Offset < 0)
                throw new InvalidOperationException();
            return new(scope, decoded.Offset, decoded.AsOf);
        }
        catch (Exception exception) when (exception is ActivityManagementCursorInvalidException or InvalidOperationException)
        {
            throw new ActivityAuthoringException(
                400,
                "activity.management.cursor-invalid",
                "Activity management cursor is invalid",
                "The continuation does not belong to this authorization and filter snapshot.",
                recovery: new(Instruction: "restart-without-cursor"),
                innerException: exception);
        }
    }

    private static T? ParseOptional<T>(string? value, string name) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<T>(value, false, out var parsed) && StringComparer.Ordinal.Equals(parsed.ToString(), value))
            return parsed;
        throw Invalid($"'{name}' is not a supported value.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Scope(params object?[] values)
    {
        var json = JsonSerializer.Serialize(values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static ActivityAuthoringException Invalid(string message) => new(
        400,
        "activity.request.invalid",
        "Invalid activity management request",
        message);

    private static ActivityAuthoringException NotFound(string code, string title, string message) => new(404, code, title, message);

    private sealed record CursorBinding(string Scope, int Offset, DateTimeOffset AsOf);
}
