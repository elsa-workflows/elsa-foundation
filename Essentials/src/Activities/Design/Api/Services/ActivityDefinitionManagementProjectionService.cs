using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Services;

public sealed class ActivityDefinitionManagementProjectionService(
    IActivityDefinitionManagementProjectionStore store,
    IActivityProviderRegistry providers,
    IActivityAuthoringContextAsync context,
    IActivityManagementCursorCodec cursorCodec) : IActivityDefinitionManagementProjectionService
{
    Task<ReusableActivityDefinitionManagementView> IActivityDefinitionManagementProjectionService.GetDefinitionAsync(
        GetReusableActivityDefinition request,
        CancellationToken cancellationToken) =>
        GetDefinitionAsync(request.DefinitionId, cancellationToken);

    public async Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> ListDefinitionsAsync(
        ListReusableActivityDefinitions request,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizationSnapshotAsync(cancellationToken);
        var authority = ParseOptional<ActivityContentAuthorityKind>(request.Authority, "authority");
        var binding = await BindAsync(
            "definitions",
            null,
            request.Limit,
            request.Cursor,
            request.Search,
            request.Authority,
            request.ProviderKey,
            null,
            request.Sort,
            cancellationToken);
        try
        {
            var page = await store.ReadDefinitionsAsync(new(
                context.TenantId,
                binding.SnapshotSequence,
                binding.Offset,
                request.Limit,
                Normalize(request.Search),
                authority,
                Normalize(request.ProviderKey)), cancellationToken);
            return Page(page, binding.Scope, page.Items.Select(item => ToDefinitionView(item, authorization)).ToArray());
        }
        catch (ActivityManagementSnapshotExpiredException exception)
        {
            throw Expired(exception);
        }
    }

    public async Task<ReusableActivityDefinitionManagementView> GetDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizationSnapshotAsync(cancellationToken);
        var record = await store.FindDefinitionAsync(definitionId, context.TenantId, cancellationToken: cancellationToken);
        return record is null
            ? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.")
            : ToDefinitionView(record, authorization);
    }

    public async Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> ListDraftsAsync(
        ListReusableActivityDrafts request,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizationSnapshotAsync(cancellationToken);
        var status = ParseOptional<ActivityDefinitionDraftStatus>(request.Status, "status");
        var binding = await BindAsync(
            "drafts",
            request.DefinitionId,
            request.Limit,
            request.Cursor,
            request.Search,
            null,
            request.ProviderKey,
            request.Status,
            request.Sort,
            cancellationToken);
        try
        {
            _ = await store.FindDefinitionAsync(request.DefinitionId, context.TenantId, binding.SnapshotSequence, cancellationToken)
                ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");
            var page = await store.ReadDraftsAsync(request.DefinitionId, new(
                context.TenantId,
                binding.SnapshotSequence,
                binding.Offset,
                request.Limit,
                Normalize(request.Search),
                ProviderKey: Normalize(request.ProviderKey),
                DraftStatus: status), cancellationToken);
            return Page(page, binding.Scope, page.Items.Select(x => ToDraftView(x, authorization)).ToArray());
        }
        catch (ActivityManagementSnapshotExpiredException exception)
        {
            throw Expired(exception);
        }
    }

    public async Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> ListVersionsAsync(
        ListReusableActivityVersions request,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizationSnapshotAsync(cancellationToken);
        var lifecycle = ParseOptional<ActivityDefinitionVersionLifecycle>(request.Lifecycle, "lifecycle");
        var binding = await BindAsync(
            "versions",
            request.DefinitionId,
            request.Limit,
            request.Cursor,
            request.Search,
            null,
            request.ProviderKey,
            request.Lifecycle,
            request.Sort,
            cancellationToken);
        try
        {
            var definition = await store.FindDefinitionAsync(
                    request.DefinitionId,
                    context.TenantId,
                    binding.SnapshotSequence,
                    cancellationToken)
                ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");
            var page = await store.ReadVersionsAsync(request.DefinitionId, new(
                context.TenantId,
                binding.SnapshotSequence,
                binding.Offset,
                request.Limit,
                Normalize(request.Search),
                ProviderKey: Normalize(request.ProviderKey),
                VersionLifecycle: lifecycle), cancellationToken);
            var items = page.Items.Select(version => ToVersionView(version, definition, authorization)).ToArray();
            return Page(page, binding.Scope, items);
        }
        catch (ActivityManagementSnapshotExpiredException exception)
        {
            throw Expired(exception);
        }
    }

    private ReusableActivityDefinitionManagementView ToDefinitionView(
        ActivityDefinitionManagementProjectionRevision record,
        AuthorizationSnapshot authorization) => new(
        ToIdentity(record),
        new(record.DraftCount, record.VersionCount, ToReference(record.Head), ToReference(record.Recommendation)),
        DefinitionActions(record, authorization),
        record.UpdatedAt);

    private ReusableActivityDraftManagementView ToDraftView(
        ActivityDefinitionDraftManagementProjectionRevision draft,
        AuthorizationSnapshot authorization) => new(
        new(
            draft.DraftId,
            draft.DefinitionId,
            draft.Revision,
            draft.SourceVersionId,
            draft.Status,
            draft.ProviderKey,
            draft.ProviderSchemaVersion,
            draft.UpdatedAt,
            draft.PresentationLabel),
        DraftActions(draft, authorization));

    private ReusableActivityVersionManagementView ToVersionView(
        ActivityDefinitionVersionManagementProjectionRevision version,
        ActivityDefinitionManagementProjectionRevision definition,
        AuthorizationSnapshot authorization) => new(
        new(version.DefinitionVersionId, version.DefinitionId, version.Version, version.Lifecycle, version.PublishedAt),
        version.ProviderKey,
        version.ProviderSchemaVersion,
        StringComparer.Ordinal.Equals(definition.RecommendedVersionId, version.DefinitionVersionId),
        VersionActions(version, definition.ContentAuthority, authorization));

    private IReadOnlyList<ActivityActionAvailabilityView> DefinitionActions(
        ActivityDefinitionManagementProjectionRevision definition,
        AuthorizationSnapshot authorization)
    {
        var designOwned = definition.ContentAuthority.Kind == ActivityContentAuthorityKind.Design;
        var recommendation = definition.Recommendation;
        var hasActiveRecommendation = recommendation?.Lifecycle == ActivityDefinitionVersionLifecycle.Active;
        var forkTargets = hasActiveRecommendation
            ? GetForkTargetAvailability(recommendation!.ProviderKey, recommendation.ProviderSchemaVersion, authorization)
            : default;
        return
        [
            Action("edit-definition", authorization.CanManage && designOwned, authorization.CanManage ? "activity.definition.source-owned" : ActivityErrorCodes.ActionForbidden),
            Action("create-draft", authorization.CanManage && designOwned, authorization.CanManage ? "activity.definition.source-owned" : ActivityErrorCodes.ActionForbidden),
            Action("set-recommendation", authorization.CanManage, ActivityErrorCodes.ActionForbidden),
            Action("fork-definition", authorization.CanManage && !designOwned && hasActiveRecommendation && forkTargets.HasAuthorizedTarget,
                ForkUnavailableCode(authorization, designOwned, forkTargets, hasActiveRecommendation))
        ];
    }

    private IReadOnlyList<ActivityActionAvailabilityView> DraftActions(
        ActivityDefinitionDraftManagementProjectionRevision draft,
        AuthorizationSnapshot authorization)
    {
        var canManageActive = authorization.CanManage && draft.Status == ActivityDefinitionDraftStatus.Active;
        var canAuthorProvider = authorization.CanAuthor;
        var stateUnavailable = !authorization.CanManage
            ? ActivityErrorCodes.ActionForbidden
            : draft.Status != ActivityDefinitionDraftStatus.Active
                ? "activity.draft.not-active"
                : null;
        var authoringUnavailable = stateUnavailable ?? "activity.provider.authoring-forbidden";
        return
        [
            Action("edit-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("edit-draft-label", canManageActive, stateUnavailable ?? ActivityErrorCodes.ActionForbidden),
            Action("discard-draft", canManageActive, stateUnavailable ?? ActivityErrorCodes.ActionForbidden),
            Action("validate-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("publish-draft", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("migrate-draft-provider", canManageActive, stateUnavailable ?? ActivityErrorCodes.ActionForbidden),
            Action("propose-contract", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("apply-contract-proposal", canManageActive && canAuthorProvider, authoringUnavailable),
            Action("create-conflict-copy", canManageActive, stateUnavailable ?? ActivityErrorCodes.ActionForbidden)
        ];
    }

    private IReadOnlyList<ActivityActionAvailabilityView> VersionActions(
        ActivityDefinitionVersionManagementProjectionRevision version,
        ActivityContentAuthority authority,
        AuthorizationSnapshot authorization)
    {
        var canManage = authorization.CanManage;
        var designOwned = authority.Kind == ActivityContentAuthorityKind.Design;
        var canAuthorProvider = authorization.CanAuthor;
        var forkTargets = GetForkTargetAvailability(version.ProviderKey, version.ProviderSchemaVersion, authorization);
        return
        [
            Action("clone-draft", canManage && designOwned && canAuthorProvider,
                !canManage ? ActivityErrorCodes.ActionForbidden : !designOwned ? "activity.definition.source-owned" : "activity.provider.authoring-forbidden"),
            Action("fork-definition", canManage && !designOwned && forkTargets.HasAuthorizedTarget,
                ForkUnavailableCode(authorization, designOwned, forkTargets)),
            Action("set-recommendation", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Active,
                canManage ? "activity.version.lifecycle-ineligible" : ActivityErrorCodes.ActionForbidden),
            Action("retire-version", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Active,
                canManage ? "activity.version.lifecycle-ineligible" : ActivityErrorCodes.ActionForbidden),
            Action("restore-version", canManage && version.Lifecycle == ActivityDefinitionVersionLifecycle.Retired,
                canManage ? "activity.version.lifecycle-ineligible" : ActivityErrorCodes.ActionForbidden),
            Action("revoke-version", canManage && version.Lifecycle != ActivityDefinitionVersionLifecycle.Revoked,
                canManage ? "activity.version.lifecycle-ineligible" : ActivityErrorCodes.ActionForbidden)
        ];
    }

    private static ActivityActionAvailabilityView Action(string action, bool allowed, string unavailableCode) =>
        new(action, allowed, allowed ? null : unavailableCode);

    private static ForkTargetAvailability GetForkTargetAvailability(
        string sourceProviderKey,
        string sourceSchemaVersion,
        AuthorizationSnapshot authorization)
    {
        var hasSupportedTarget = false;
        var hasAuthorizedTarget = false;
        foreach (var provider in authorization.RegisteredProviders)
        foreach (var schema in provider.AuthoringCapabilities.ManifestSchemas)
        {
            if (!schema.IsAuthorable || !ActivityProviderMigrationCapabilities.SupportsMigrationFrom(
                    provider.ProviderKey,
                    schema,
                    sourceProviderKey,
                    sourceSchemaVersion))
                continue;

            hasSupportedTarget = true;
            hasAuthorizedTarget |= authorization.AuthorableProviderKeys.Contains(provider.ProviderKey);
        }

        return new(hasSupportedTarget, hasAuthorizedTarget);
    }

    private static string ForkUnavailableCode(
        AuthorizationSnapshot authorization,
        bool designOwned,
        ForkTargetAvailability targets,
        bool hasActiveRecommendation = true) =>
        !authorization.CanManage
            ? ActivityErrorCodes.ActionForbidden
            : designOwned
                ? "activity.definition.design-owned"
                : !hasActiveRecommendation
                    ? "activity.version.recommendation-ineligible"
                    : targets.HasSupportedTarget
                        ? "activity.provider.authoring-forbidden"
                        : "activity.provider.migration-unsupported";

    private readonly record struct ForkTargetAvailability(bool HasSupportedTarget, bool HasAuthorizedTarget);

    private static ActivityDefinitionIdentityView ToIdentity(ActivityDefinitionManagementProjectionRevision definition) => new(
        definition.DefinitionId,
        definition.ActivityTypeKey,
        definition.TenantId,
        definition.Category,
        string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ActivityTypeKey : definition.DisplayName,
        definition.Description,
        definition.ContentAuthority,
        null,
        definition.HeadVersionId,
        definition.RecommendedVersionId);

    private static ActivityDefinitionVersionReferenceView? ToReference(ActivityManagementVersionProjectionReference? version) =>
        version is null
            ? null
            : new(version.DefinitionVersionId, version.Version, version.Lifecycle, version.ProviderKey, version.ProviderSchemaVersion);

    private ActivityManagementPageView<TView> Page<TEntity, TView>(
        ActivityManagementProjectionPage<TEntity> page,
        string scope,
        IReadOnlyList<TView> items) => new(
        items,
        items.Count,
        page.TotalCount,
        page.NextOffset is not null,
        page.NextOffset is { } offset ? cursorCodec.Encode(new(scope, offset, page.Snapshot.Sequence)) : null,
        new(Scope(scope, page.Snapshot.Sequence), page.Snapshot.AsOf));

    private async Task<CursorBinding> BindAsync(
        string resource,
        string? definitionId,
        int limit,
        string? cursor,
        string? search,
        string? authority,
        string? providerKey,
        string? lifecycle,
        string sort,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
            throw Invalid("'limit' must be between 1 and 100.");
        if (!StringComparer.Ordinal.Equals(sort, "identity-asc"))
            throw Invalid("'sort' must be 'identity-asc'.");
        var scope = Scope(
            resource,
            definitionId,
            context.TenantId,
            await context.GetAuthorizationProfileAsync(cancellationToken),
            limit,
            Normalize(search),
            Normalize(authority),
            Normalize(providerKey),
            Normalize(lifecycle),
            sort);
        if (string.IsNullOrWhiteSpace(cursor))
        {
            var snapshot = await store.GetCurrentSnapshotAsync(cancellationToken);
            return new(scope, 0, snapshot.Sequence);
        }
        try
        {
            var decoded = cursorCodec.Decode(cursor);
            if (!StringComparer.Ordinal.Equals(decoded.Scope, scope) || decoded.Offset < 0)
                throw new InvalidOperationException();
            return new(scope, decoded.Offset, decoded.SnapshotSequence);
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

    private async Task<AuthorizationSnapshot> AuthorizationSnapshotAsync(CancellationToken cancellationToken)
    {
        var profile = await context.GetAuthorizationProfileAsync(cancellationToken);
        var canManage = await context.CanManageActivityDefinitionsAsync(cancellationToken);
        var canAuthor = await context.CanAuthorProviderAsync("profile", cancellationToken);
        var registeredProviders = providers.Providers.ToArray();
        var authorableProviderKeys = new HashSet<string>(StringComparer.Ordinal);
        if (canManage)
            foreach (var provider in registeredProviders)
                if (await context.CanAuthorProviderAsync(provider.ProviderKey, cancellationToken))
                    authorableProviderKeys.Add(provider.ProviderKey);
        return new(profile, canManage, canAuthor, authorableProviderKeys, registeredProviders);
    }

    private sealed record AuthorizationSnapshot(
        string Profile,
        bool CanManage,
        bool CanAuthor,
        IReadOnlySet<string> AuthorableProviderKeys,
        IReadOnlyList<IActivityProvider> RegisteredProviders);

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
        ActivityErrorCodes.RequestInvalid,
        "Invalid activity management request",
        message);

    private static ActivityAuthoringException NotFound(string code, string title, string message) => new(404, code, title, message);

    private static ActivityAuthoringException Expired(ActivityManagementSnapshotExpiredException exception) => new(
        410,
        ActivityErrorCodes.CursorExpired,
        "Activity management snapshot expired",
        "The retained activity management snapshot is no longer available.",
        recovery: new(Instruction: "restart-without-cursor"),
        innerException: exception);

    private sealed record CursorBinding(string Scope, int Offset, long SnapshotSequence);
}

/// <summary>The definition management projections the Design endpoints dispatch to.</summary>
/// <remarks>Every method takes its wire contract, so the operation seam mirrors the routes one to one.</remarks>
public interface IActivityDefinitionManagementProjectionService
{
    Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> ListDefinitionsAsync(ListReusableActivityDefinitions request, CancellationToken cancellationToken);
    Task<ReusableActivityDefinitionManagementView> GetDefinitionAsync(GetReusableActivityDefinition request, CancellationToken cancellationToken);
    Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> ListDraftsAsync(ListReusableActivityDrafts request, CancellationToken cancellationToken);
    Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> ListVersionsAsync(ListReusableActivityVersions request, CancellationToken cancellationToken);
}
