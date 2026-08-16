using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Api.Services;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ReusableActivityAuthoringService(
    IActivityDefinitionStore definitions,
    IActivityDefinitionAuthoringStore authoringStore,
    IActivityDefinitionDraftStore draftStore,
    IActivityDefinitionVersionPublicationStore publicationStore,
    IActivityDefinitionLayoutStore layoutStore,
    IActivityDraftValidationStore validationStore,
    ICreateActivityDefinitionCommand createDefinition,
    IUpdateActivityDefinitionPresentationCommand updateDefinitionPresentation,
    ICreateActivityDraftCommand createDraft,
    IUpdateActivityDraftPresentationCommand updateDraftPresentation,
    ICreateActivityDraftConflictCopyCommand createConflictCopy,
    IReplaceActivityDraftCommand replaceDraft,
    IDiscardActivityDraftCommand discardDraft,
    IStoreActivityDraftValidationCommand storeValidation,
    IActivityProviderRegistry providers,
    IActivityDraftValidator validator,
    ActivityContractAuthoringValidator contractAuthoringValidator,
    IActivityTypeKeyPolicy typeKeyPolicy,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider,
    IActivityAuthoringContextAsync context)
{
    public async Task<ReusableActivityDefinitionMutationView> CreateDefinitionAsync(
        CreateReusableActivityDefinition command,
        CancellationToken cancellationToken)
    {
        EnsureDisplayName(command.DisplayName);

        var now = timeProvider.GetUtcNow();
        var definitionId = NewId("activity-def");
        var draftId = NewId("activity-draft");
        var contract = ToDomainContract(command.Contract);
        await EnsureAuthorableProviderAsync(command.Provider, cancellationToken);
        EnsureAuthorableContract(contract, new("ActivityDraft", draftId, definitionId, Revision: 1));
        var activityTypeKey = ResolveActivityTypeKey(command.ActivityTypeKey, command.DisplayName, definitionId);
        var definition = NewDefinition(definitionId, activityTypeKey, command.Category, command.DisplayName, command.Description, now);
        var authoring = NewAuthoring(definitionId, new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), null, now);
        var draft = NewDraft(draftId, definitionId, null, contract, command.Provider, now);
        var layout = NewDraftLayout(draftId, command.Layout, now);

        try
        {
            await createDefinition.ExecuteAsync(new(definition, authoring, draft, layout), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict("activity.definition.key-conflict", "Activity definition key conflict", "An activity definition with this activity type key already exists.", exception);
        }

        return new(ToIdentity(definition, authoring), ToSummary(draft));
    }

    public async Task<ActivityDefinitionIdentityView> UpdateDefinitionAsync(
        UpdateReusableActivityDefinition command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Category))
            throw BadRequest("'category' is required.");
        EnsureDisplayName(command.DisplayName);

        var definition = await GetDefinitionEntityAsync(command.DefinitionId, cancellationToken);
        var authoring = await GetAuthoringAsync(command.DefinitionId, cancellationToken);
        EnsureVisible(definition.TenantId);
        EnsureVisible(authoring.TenantId);
        EnsureDesignAuthority(authoring);

        var updated = await updateDefinitionPresentation.ExecuteAsync(new(
            command.DefinitionId,
            context.TenantId,
            command.Category,
            command.DisplayName,
            command.Description,
            timeProvider.GetUtcNow()), cancellationToken);

        return ToIdentity(updated, authoring);
    }

    public async Task<ReusableActivityDraftView> CreateDraftAsync(
        CreateReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        var authoring = await GetAuthoringAsync(command.DefinitionId, cancellationToken);
        EnsureVisible(authoring.TenantId);
        EnsureDesignAuthority(authoring);

        ActivityContract contract;
        ActivityProviderManifest provider;
        IReadOnlyList<ActivityLayoutRecord> records;
        if (command.SourceVersionId is not null)
        {
            if (command.Provider is not null || command.Contract is not null || command.Layout is not null)
                throw BadRequest("A clone request supplies only 'sourceVersionId'.");

            var source = await GetPublicationAsync(command.SourceVersionId, cancellationToken);
            if (!string.Equals(source.DefinitionId, command.DefinitionId, StringComparison.Ordinal))
                throw NotFound("activity.version.not-found", "Activity version not found", "The exact source version was not found for this definition.");
            var sourceLayout = await layoutStore.FindVersionLayoutAsync(source.DefinitionVersionId, cancellationToken)
                ?? throw OperationFailed("The source version layout is unavailable.");
            contract = source.Contract;
            provider = source.Provider;
            records = sourceLayout.Records.ToArray();
        }
        else
        {
            if (command.Provider is null || command.Contract is null || command.Layout is null)
                throw BadRequest("A fresh draft requires provider, contract, and layout.");
            contract = ToDomainContract(command.Contract);
            provider = command.Provider;
            records = command.Layout;
        }

        var now = timeProvider.GetUtcNow();
        var draft = NewDraft(NewId("activity-draft"), command.DefinitionId, command.SourceVersionId, contract, provider, now, command.PresentationLabel);
        await EnsureAuthorableProviderAsync(provider, cancellationToken);
        EnsureAuthorableContract(contract, new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision));
        var layout = NewDraftLayout(draft.Id, records, now);
        try
        {
            await createDraft.ExecuteAsync(new(draft, layout, authoring.HeadVersionId), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict("activity.definition.stale-head", "Activity definition head is stale", "The definition head changed while the draft was being created.", exception);
        }

        return await ToDraftViewAsync(draft, layout.Records.ToArray(), null, cancellationToken);
    }

    public async Task<ReusableActivityDraftView> UpdateDraftPresentationAsync(
        UpdateReusableActivityDraftPresentation command,
        CancellationToken cancellationToken)
    {
        var current = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(current.DefinitionId, cancellationToken);
        EnsureVisible(current.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(current, command.ExpectedRevision);
        var label = NormalizePresentationLabel(command.PresentationLabel);
        var layout = await layoutStore.FindDraftLayoutAsync(current.Id, cancellationToken)
            ?? throw OperationFailed("The draft layout is unavailable.");
        try
        {
            var updated = await updateDraftPresentation.ExecuteAsync(
                new(current.Id, current.Revision, label, timeProvider.GetUtcNow()),
                cancellationToken);
            return await ToDraftViewAsync(updated, layout.Records.ToArray(), null, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw await LatestStaleRevisionAsync(current, command.ExpectedRevision, exception, cancellationToken);
        }
    }

    public async Task<ReusableActivityDraftView> CreateConflictCopyAsync(
        CreateReusableActivityDraftConflictCopy command,
        CancellationToken cancellationToken)
    {
        var source = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(source.DefinitionId, cancellationToken);
        EnsureVisible(source.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(source, command.ExpectedSourceRevision);
        var contract = ToDomainContract(command.Contract);
        await EnsureAuthorableProviderAsync(command.Provider, cancellationToken);
        EnsureAuthorableContract(contract, new("ActivityDraft", source.Id, source.DefinitionId, Revision: source.Revision));
        var now = timeProvider.GetUtcNow();
        var copy = NewDraft(
            NewId("activity-draft"),
            source.DefinitionId,
            source.SourceVersionId,
            contract,
            command.Provider,
            now,
            command.PresentationLabel);
        copy.State = copy.State with { Options = new Dictionary<string, string>(source.State.Options, StringComparer.Ordinal) };
        var layout = NewDraftLayout(copy.Id, command.Layout, now);
        try
        {
            await createConflictCopy.ExecuteAsync(new(source.Id, command.ExpectedSourceRevision, copy, layout), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw await LatestStaleRevisionAsync(source, command.ExpectedSourceRevision, exception, cancellationToken);
        }
        return await ToDraftViewAsync(copy, layout.Records.ToArray(), null, cancellationToken);
    }

    public async Task<ReusableActivityDraftView> ReplaceDraftAsync(
        ReplaceReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        var current = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(current.DefinitionId, cancellationToken);
        EnsureVisible(current.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(current, command.ExpectedRevision);
        var contract = ToDomainContract(command.Contract);
        await EnsureAuthorableProviderAsync(command.Provider, cancellationToken);
        EnsureAuthorableContract(contract, new("ActivityDraft", current.Id, current.DefinitionId, Revision: current.Revision));

        ActivityDefinitionDraft updated;
        try
        {
            updated = await replaceDraft.ExecuteAsync(
                new(
                    command.DraftId,
                    command.ExpectedRevision,
                    new(contract, command.Provider, current.State.Options),
                    command.Layout,
                    NormalizePresentationLabel(command.PresentationLabel)),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw await LatestStaleRevisionAsync(current, command.ExpectedRevision, exception, cancellationToken);
        }

        return await ToDraftViewAsync(updated, command.Layout, null, cancellationToken);
    }

    public async Task<ReusableActivityDraftView> MigrateDraftAsync(
        MigrateReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        await EnsureProviderWriteAsync(command.TargetProviderKey, cancellationToken);
        var current = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(current.DefinitionId, cancellationToken);
        EnsureVisible(current.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(current, command.ExpectedRevision);

        IActivityProvider targetProvider;
        try
        {
            targetProvider = providers.Resolve(command.TargetProviderKey, command.TargetSchemaVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw MigrationUnsupported(current.State.Provider, command.TargetProviderKey, command.TargetSchemaVersion, [], exception);
        }

        ActivityManifestMigration migration;
        try
        {
            migration = await targetProvider.MigrateAsync(
                new(current.State.Provider, command.TargetSchemaVersion),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MigrationUnsupported(current.State.Provider, command.TargetProviderKey, command.TargetSchemaVersion, [], exception);
        }

        if (migration.Manifest is null ||
            !StringComparer.Ordinal.Equals(migration.Manifest.ProviderKey, command.TargetProviderKey) ||
            !StringComparer.Ordinal.Equals(migration.Manifest.SchemaVersion, command.TargetSchemaVersion) ||
            migration.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
        {
            throw MigrationUnsupported(
                current.State.Provider,
                command.TargetProviderKey,
                command.TargetSchemaVersion,
                migration.Diagnostics);
        }

        await EnsureAuthorableProviderAsync(migration.Manifest, cancellationToken);
        EnsureAuthorableContract(current.State.Contract, new("ActivityDraft", current.Id, current.DefinitionId, Revision: current.Revision));

        var currentLayout = await layoutStore.FindDraftLayoutAsync(current.Id, cancellationToken)
                            ?? throw OperationFailed("The source draft layout is unavailable.");
        if (currentLayout.Revision != current.Revision)
            throw StaleRevision(current, command.ExpectedRevision);

        var now = timeProvider.GetUtcNow();
        var migrated = NewDraft(
            NewId("activity-draft"),
            current.DefinitionId,
            current.SourceVersionId,
            current.State.Contract,
            migration.Manifest,
            now,
            current.PresentationLabel);
        migrated.State = migrated.State with
        {
            Options = new Dictionary<string, string>(current.State.Options, StringComparer.Ordinal)
        };
        var migratedLayout = NewDraftLayout(migrated.Id, currentLayout.Records.ToArray(), now);

        try
        {
            await createDraft.ExecuteAsync(new(migrated, migratedLayout, authoring.HeadVersionId), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict(
                "activity.definition.stale-head",
                "Activity definition head is stale",
                "The definition head changed while the migrated draft was being created.",
                exception);
        }

        return await ToDraftViewAsync(migrated, migratedLayout.Records.ToArray(), null, cancellationToken);
    }

    public async Task DiscardDraftAsync(DiscardReusableActivityDraft command, CancellationToken cancellationToken)
    {
        var current = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(current.DefinitionId, cancellationToken);
        EnsureVisible(current.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(current, command.ExpectedRevision);
        try
        {
            await discardDraft.ExecuteAsync(new(command.DraftId, command.ExpectedRevision), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw await LatestStaleRevisionAsync(current, command.ExpectedRevision, exception, cancellationToken);
        }
    }

    public async Task<ActivityDraftValidationView> ValidateDraftAsync(
        ValidateReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        var draft = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(draft.DefinitionId, cancellationToken);
        EnsureVisible(draft.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(draft, command.ExpectedRevision);

        var result = await validator.ValidateAsync(
            new(draft.DefinitionId, draft.Id, draft.Revision, draft.State),
            cancellationToken);
        if (!string.Equals(result.DraftId, draft.Id, StringComparison.Ordinal) || result.Revision != draft.Revision)
            throw OperationFailed("The validator returned a result for a different draft revision.");

        var existingValidation = await validationStore.FindAsync(draft.Id, draft.Revision, cancellationToken);
        var validation = new ActivityDraftValidationState
        {
            Id = existingValidation?.Id ?? NewId("activity-validation"),
            TenantId = draft.TenantId,
            DraftId = draft.Id,
            Revision = draft.Revision,
            ValidatedAt = result.ValidatedAt,
            Diagnostics = ActivityDiagnosticOrderer.Order(result.Diagnostics).ToList(),
            CreatedAt = existingValidation?.CreatedAt ?? result.ValidatedAt,
            LastModifiedAt = result.ValidatedAt
        };
        try
        {
            await storeValidation.ExecuteAsync(validation, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw await LatestStaleRevisionAsync(draft, command.ExpectedRevision, exception, cancellationToken);
        }

        return ToValidationView(validation);
    }

    public async Task<ReusableActivityDraftView> GetDraftViewAsync(string draftId, CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindAsync(draftId, cancellationToken);
        if (draft is null || !IsVisible(draft.TenantId))
            throw NotFound("activity.draft.not-found", "Activity draft not found", "The requested activity draft was not found.");
        var layoutTask = layoutStore.FindDraftLayoutAsync(draftId, cancellationToken);
        var validationTask = validationStore.FindAsync(draftId, draft.Revision, cancellationToken);
        await Task.WhenAll(layoutTask, validationTask);
        var layout = await layoutTask ?? throw OperationFailed("The draft layout is unavailable.");
        return await ToDraftViewAsync(draft, layout.Records.ToArray(), await validationTask, cancellationToken);
    }

    public async Task<ReusableActivityVersionView> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var version = await publicationStore.FindAsync(versionId, cancellationToken);
        if (version is null || !IsVisible(version.TenantId))
            throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
        ActivityDefinition definition;
        try
        {
            definition = await definitions.GetAsync(version.DefinitionId, cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
        }
        var authoring = await authoringStore.FindAsync(version.DefinitionId, cancellationToken);
        if (authoring is null ||
            !StringComparer.Ordinal.Equals(definition.TenantId, version.TenantId) ||
            !StringComparer.Ordinal.Equals(authoring.TenantId, version.TenantId))
            throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
        return new(
            ToIdentity(definition, authoring),
            version.DefinitionVersionId,
            version.Version,
            version.SourceDraftId,
            version.SourceVersionId,
            version.Contract.ToView(),
            await ToProviderViewAsync(version.Provider, cancellationToken),
            new(
                version.TemplateId,
                version.TemplateHash,
                version.SourceReferenceId,
                version.ProviderFingerprint,
                version.DirectDependencyCount,
                version.ClosedTemplateCount,
                version.RuntimeRequirements.ToArray()),
            version.Lifecycle,
            version.PublishedAt);
    }

    private ActivityDefinition NewDefinition(
        string id,
        string activityTypeKey,
        string category,
        string? displayName,
        string? description,
        DateTimeOffset now) => new()
    {
        Id = id,
        TenantId = context.TenantId,
        ActivityTypeKey = activityTypeKey,
        Category = category,
        DisplayName = displayName,
        Description = description,
        CreatedAt = now,
        LastModifiedAt = now
    };

    private ActivityDefinitionAuthoringState NewAuthoring(
        string definitionId,
        ActivityContentAuthority authority,
        ActivityDefinitionForkOrigin? forkedFrom,
        DateTimeOffset now) => new()
    {
        Id = NewId("activity-authoring"),
        TenantId = context.TenantId,
        DefinitionId = definitionId,
        ContentAuthority = authority,
        ForkedFrom = forkedFrom,
        CreatedAt = now,
        LastModifiedAt = now
    };

    private ActivityDefinitionDraft NewDraft(
        string draftId,
        string definitionId,
        string? sourceVersionId,
        ActivityContract contract,
        ActivityProviderManifest provider,
        DateTimeOffset now,
        string? presentationLabel = null) => new()
    {
        Id = draftId,
        TenantId = context.TenantId,
        DefinitionId = definitionId,
        Revision = 1,
        SourceVersionId = sourceVersionId,
        PresentationLabel = NormalizePresentationLabel(presentationLabel),
        Status = ActivityDefinitionDraftStatus.Active,
        State = new(contract, provider, new Dictionary<string, string>()),
        CreatedAt = now,
        LastModifiedAt = now
    };

    private ActivityDefinitionDraftLayout NewDraftLayout(
        string draftId,
        IReadOnlyList<ActivityLayoutRecord> records,
        DateTimeOffset now) => new()
    {
        Id = NewId("activity-draft-layout"),
        TenantId = context.TenantId,
        DraftId = draftId,
        Revision = 1,
        Records = records.ToList(),
        CreatedAt = now,
        LastModifiedAt = now
    };

    private async Task<ActivityDefinition> GetDefinitionEntityAsync(string definitionId, CancellationToken cancellationToken)
    {
        try
        {
            return await definitions.GetAsync(definitionId, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.", exception);
        }
    }

    private async Task<ActivityDefinitionAuthoringState> GetAuthoringAsync(string definitionId, CancellationToken cancellationToken) =>
        await authoringStore.FindAsync(definitionId, cancellationToken)
        ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");

    private async Task<ActivityDefinitionDraft> GetDraftAsync(string draftId, CancellationToken cancellationToken) =>
        await draftStore.FindAsync(draftId, cancellationToken)
        ?? throw NotFound("activity.draft.not-found", "Activity draft not found", "The requested activity draft was not found.");

    private async Task<ActivityDefinitionVersionPublication> GetPublicationAsync(string versionId, CancellationToken cancellationToken) =>
        await publicationStore.FindAsync(versionId, cancellationToken)
        ?? throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");

    private async ValueTask EnsureProviderWriteAsync(string providerKey, CancellationToken cancellationToken)
    {
        if (!await context.CanAuthorProviderAsync(providerKey, cancellationToken))
            throw Forbidden("The caller is not authorized to author this activity provider.");
    }

    private async ValueTask EnsureAuthorableProviderAsync(ActivityProviderManifest manifest, CancellationToken cancellationToken)
    {
        await EnsureProviderWriteAsync(manifest.ProviderKey, cancellationToken);
        IActivityProvider provider;
        try
        {
            provider = providers.Resolve(manifest.ProviderKey, manifest.SchemaVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw new ActivityAuthoringException(
                422,
                "activity.provider.schema-unavailable",
                "Activity provider schema is unavailable",
                "The selected provider schema is not available for authoring.",
                innerException: exception);
        }

        if (provider.AuthoringCapabilities.ManifestSchemas.All(x =>
                !StringComparer.Ordinal.Equals(x.SchemaVersion, manifest.SchemaVersion) || !x.IsAuthorable))
            throw new ActivityAuthoringException(
                422,
                "activity.provider.schema-not-authorable",
                "Activity provider schema is not authorable",
                "The selected provider schema may be readable historically but cannot be used for mutable authoring.");
    }

    private void EnsureAuthorableContract(ActivityContract contract, ActivityDiagnosticSubject subject)
    {
        var diagnostics = contractAuthoringValidator.Validate(contract, subject);
        if (diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw new ActivityAuthoringException(
                422,
                "activity.contract.capability-rejected",
                "Activity contract is not authorable",
                "The mutable activity contract contains types, collection kinds, or storage drivers outside the activated capability catalog.",
                diagnostics);
    }

    private void EnsureVisible(string? tenantId)
    {
        if (!IsVisible(tenantId))
            throw Forbidden("The requested activity identity is outside the caller's tenant scope.", "activity.tenant.reference-denied");
    }

    private bool IsVisible(string? tenantId) => tenantId is null || string.Equals(tenantId, context.TenantId, StringComparison.Ordinal);

    private static void EnsureDesignAuthority(ActivityDefinitionAuthoringState authoring)
    {
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict(
                "activity.definition.content-authority",
                "Activity definition is source-owned",
                "This operation cannot modify a source-owned activity definition. Fork an exact version into a new identity instead.");
    }

    private static void EnsureDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw BadRequest("'displayName' is required.");
    }

    private static string? NormalizePresentationLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        var normalized = label.Trim();
        if (normalized.Length > 200)
            throw BadRequest("'presentationLabel' must not exceed 200 characters.");
        return normalized;
    }

    private static void EnsureActiveRevision(ActivityDefinitionDraft draft, long expectedRevision)
    {
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict("activity.draft.stale-revision", "Activity draft is not active", "Only an active draft can be changed.");
        if (draft.Revision != expectedRevision)
            throw StaleRevision(draft, expectedRevision);
    }

    private async ValueTask<ActivityProviderManifestView> ToProviderViewAsync(
        ActivityProviderManifest provider,
        CancellationToken cancellationToken) => new(
        provider.ProviderKey,
        provider.SchemaVersion,
        ActivityProviderManifestFingerprint.Compute(provider),
        await context.CanReadProviderPayloadAsync(provider.ProviderKey, cancellationToken) ? provider.Payload.Clone() : null);

    private ActivityDefinitionIdentityView ToIdentity(ActivityDefinition definition, ActivityDefinitionAuthoringState authoring) => new(
        definition.Id,
        definition.ActivityTypeKey,
        definition.TenantId,
        definition.Category,
        string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ActivityTypeKey : definition.DisplayName,
        definition.Description,
        authoring.ContentAuthority,
        authoring.ForkedFrom,
        authoring.HeadVersionId,
        authoring.RecommendedVersionId);

    private static ReusableActivityDraftSummaryView ToSummary(ActivityDefinitionDraft draft) => new(
        draft.Id,
        draft.DefinitionId,
        draft.Revision,
        draft.SourceVersionId,
        draft.Status,
        draft.State.Provider.ProviderKey,
        draft.State.Provider.SchemaVersion,
        draft.LastModifiedAt,
        draft.PresentationLabel);

    private async ValueTask<ReusableActivityDraftView> ToDraftViewAsync(
        ActivityDefinitionDraft draft,
        IReadOnlyList<ActivityLayoutRecord> layout,
        ActivityDraftValidationState? validation,
        CancellationToken cancellationToken) => new(
        draft.Id,
        draft.DefinitionId,
        draft.TenantId,
        draft.Revision,
        draft.SourceVersionId,
        draft.Status,
        draft.State.Contract.ToView(),
        await ToProviderViewAsync(draft.State.Provider, cancellationToken),
        layout,
        validation is null ? null : ToValidationView(validation),
        draft.CreatedAt,
        draft.LastModifiedAt,
        draft.PresentationLabel);

    private static ActivityDraftValidationView ToValidationView(ActivityDraftValidationState validation) => new(
        validation.DraftId,
        validation.Revision,
        validation.IsValid,
        validation.ValidatedAt,
        ActivityDiagnosticOrderer.Order(validation.Diagnostics));

    private string NewId(string prefix) => $"{prefix}-{identityGenerator.Generate()}";

    private string ResolveActivityTypeKey(string? requestedActivityTypeKey, string displayName, string definitionId)
    {
        if (requestedActivityTypeKey is null)
            return typeKeyPolicy.Generate(displayName, definitionId);
        if (!typeKeyPolicy.Rules.AllowsPreCreationOverride)
            throw BadRequest("An activity type key override is not allowed by the active key policy.");

        try
        {
            return typeKeyPolicy.NormalizeAndValidateOverride(requestedActivityTypeKey);
        }
        catch (ArgumentException exception)
        {
            throw new ActivityAuthoringException(
                400,
                "activity.definition.key-invalid",
                "Invalid activity definition key",
                "The supplied activity type key does not satisfy the advertised activity type key rules.",
                innerException: exception);
        }
    }

    private static ActivityContract ToDomainContract(ActivityContractView contract)
    {
        try
        {
            return contract.ToDomain();
        }
        catch (ArgumentException exception)
        {
            throw new ActivityAuthoringException(
                400,
                ActivityErrorCodes.RequestInvalid,
                "Invalid activity authoring request",
                "The public activity contract contains an unsupported type reference.",
                innerException: exception);
        }
    }

    private static ActivityAuthoringException StaleRevision(ActivityDefinitionDraft draft, long expected, Exception? inner = null) => new(
        409,
        "activity.draft.stale-revision",
        "Activity draft revision is stale",
        "The draft changed after the submitted revision was read.",
        [new(
            "activity.draft.stale-revision",
            ActivityDiagnosticSeverity.Error,
            $"Expected revision {expected} but the current revision is {draft.Revision}.",
            new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision),
            Remediation: "Reload the draft and reapply the intended change.",
            Metadata: new Dictionary<string, string>
            {
                ["expectedRevision"] = expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["actualRevision"] = draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })],
        inner,
        new(
            draft.Revision,
            "activity-draft-conflict-copies",
            $"design/activities/drafts/{draft.Id}/conflict-copies",
            "review-current-revision-and-create-conflict-copy"));

    private async Task<ActivityAuthoringException> LatestStaleRevisionAsync(
        ActivityDefinitionDraft observed,
        long expectedRevision,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var latest = await draftStore.FindAsync(observed.Id, cancellationToken);
        return StaleRevision(
            latest is not null && IsVisible(latest.TenantId) ? latest : observed,
            expectedRevision,
            exception);
    }

    private static ActivityAuthoringException MigrationUnsupported(
        ActivityProviderManifest source,
        string targetProviderKey,
        string targetSchema,
        IReadOnlyList<ActivityDiagnostic> diagnostics,
        Exception? inner = null) => new(
        422,
        "activity.provider.migration-unsupported",
        "Activity provider migration is unsupported",
        $"No deterministic provider migration is available from '{source.ProviderKey}/{source.SchemaVersion}' to '{targetProviderKey}/{targetSchema}'.",
        ActivityDiagnosticOrderer.Order(diagnostics),
        inner);

    private static ActivityAuthoringException BadRequest(string detail) => new(400, ActivityErrorCodes.RequestInvalid, "Invalid activity authoring request", detail);
    private static ActivityAuthoringException Forbidden(string detail, string code = ActivityErrorCodes.AuthorizationDenied) => new(403, code, "Activity authoring is forbidden", detail);
    private static ActivityAuthoringException NotFound(string code, string title, string detail, Exception? inner = null) => new(404, code, title, detail, innerException: inner);
    private static ActivityAuthoringException Conflict(string code, string title, string detail, Exception? inner = null) => new(409, code, title, detail, innerException: inner);
    private static ActivityAuthoringException OperationFailed(string detail) => new(500, ActivityErrorCodes.OperationFailed, "Activity authoring operation failed", detail);
}

public sealed class CreateReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<CreateReusableActivityDefinition, ReusableActivityDefinitionMutationView>
{
    public Task<ReusableActivityDefinitionMutationView> Handle(CreateReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.CreateDefinitionAsync(command, cancellationToken);
}

public sealed class UpdateReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<UpdateReusableActivityDefinition, ActivityDefinitionIdentityView>
{
    public Task<ActivityDefinitionIdentityView> Handle(UpdateReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.UpdateDefinitionAsync(command, cancellationToken);
}

public sealed class CreateReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<CreateReusableActivityDraft, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(CreateReusableActivityDraft command, CancellationToken cancellationToken) =>
        service.CreateDraftAsync(command, cancellationToken);
}

public sealed class ReplaceReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<ReplaceReusableActivityDraft, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(ReplaceReusableActivityDraft command, CancellationToken cancellationToken) =>
        service.ReplaceDraftAsync(command, cancellationToken);
}

public sealed class UpdateReusableActivityDraftPresentationHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<UpdateReusableActivityDraftPresentation, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(UpdateReusableActivityDraftPresentation command, CancellationToken cancellationToken) =>
        service.UpdateDraftPresentationAsync(command, cancellationToken);
}

public sealed class CreateReusableActivityDraftConflictCopyHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<CreateReusableActivityDraftConflictCopy, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(CreateReusableActivityDraftConflictCopy command, CancellationToken cancellationToken) =>
        service.CreateConflictCopyAsync(command, cancellationToken);
}

public sealed class MigrateReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<MigrateReusableActivityDraft, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(MigrateReusableActivityDraft command, CancellationToken cancellationToken) =>
        service.MigrateDraftAsync(command, cancellationToken);
}

public sealed class DiscardReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<DiscardReusableActivityDraft>
{
    public async Task<Unit> Handle(DiscardReusableActivityDraft command, CancellationToken cancellationToken)
    {
        await service.DiscardDraftAsync(command, cancellationToken);
        return Unit.Instance;
    }
}

public sealed class ValidateReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<ValidateReusableActivityDraft, ActivityDraftValidationView>
{
    public Task<ActivityDraftValidationView> Handle(ValidateReusableActivityDraft command, CancellationToken cancellationToken) =>
        service.ValidateDraftAsync(command, cancellationToken);
}

public sealed class ListReusableActivityDefinitionsHandler(ActivityDefinitionManagementProjectionService service)
    : IRequestHandler<ListReusableActivityDefinitions, ActivityManagementPageView<ReusableActivityDefinitionManagementView>>
{
    public Task<ActivityManagementPageView<ReusableActivityDefinitionManagementView>> Handle(ListReusableActivityDefinitions request, CancellationToken cancellationToken) =>
        service.ListDefinitionsAsync(request, cancellationToken);
}

public sealed class GetReusableActivityDefinitionHandler(ActivityDefinitionManagementProjectionService service)
    : IRequestHandler<GetReusableActivityDefinition, ReusableActivityDefinitionManagementView>
{
    public Task<ReusableActivityDefinitionManagementView> Handle(GetReusableActivityDefinition request, CancellationToken cancellationToken) =>
        service.GetDefinitionAsync(request.DefinitionId, cancellationToken);
}

public sealed class ListReusableActivityDraftsHandler(ActivityDefinitionManagementProjectionService service)
    : IRequestHandler<ListReusableActivityDrafts, ActivityManagementPageView<ReusableActivityDraftManagementView>>
{
    public Task<ActivityManagementPageView<ReusableActivityDraftManagementView>> Handle(ListReusableActivityDrafts request, CancellationToken cancellationToken) =>
        service.ListDraftsAsync(request, cancellationToken);
}

public sealed class GetReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<GetReusableActivityDraft, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(GetReusableActivityDraft request, CancellationToken cancellationToken) =>
        service.GetDraftViewAsync(request.DraftId, cancellationToken);
}

public sealed class ListReusableActivityVersionsHandler(ActivityDefinitionManagementProjectionService service)
    : IRequestHandler<ListReusableActivityVersions, ActivityManagementPageView<ReusableActivityVersionManagementView>>
{
    public Task<ActivityManagementPageView<ReusableActivityVersionManagementView>> Handle(ListReusableActivityVersions request, CancellationToken cancellationToken) =>
        service.ListVersionsAsync(request, cancellationToken);
}

public sealed class GetReusableActivityVersionHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<GetReusableActivityVersion, ReusableActivityVersionView>
{
    public Task<ReusableActivityVersionView> Handle(GetReusableActivityVersion request, CancellationToken cancellationToken) =>
        service.GetVersionAsync(request.VersionId, cancellationToken);
}
