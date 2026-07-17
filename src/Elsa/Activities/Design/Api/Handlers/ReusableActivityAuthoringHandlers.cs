using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;

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
    IReplaceActivityDraftCommand replaceDraft,
    IDiscardActivityDraftCommand discardDraft,
    IStoreActivityDraftValidationCommand storeValidation,
    IActivityProviderRegistry providers,
    IActivityDraftValidator validator,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider,
    IActivityAuthoringContext context)
{
    public async Task<ReusableActivityDefinitionDetailsView> CreateDefinitionAsync(
        CreateReusableActivityDefinition command,
        CancellationToken cancellationToken)
    {
        EnsureProviderWrite(command.Provider.ProviderKey);
        EnsureDisplayName(command.DisplayName);

        var now = timeProvider.GetUtcNow();
        var definitionId = NewId("activity-def");
        var draftId = NewId("activity-draft");
        var definition = NewDefinition(definitionId, command.ActivityTypeKey, command.Category, command.DisplayName, command.Description, now);
        var authoring = NewAuthoring(definitionId, new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), null, now);
        var draft = NewDraft(draftId, definitionId, null, ToDomainContract(command.Contract), command.Provider, now);
        var layout = NewDraftLayout(draftId, command.Layout, now);

        try
        {
            await createDefinition.ExecuteAsync(new(definition, authoring, draft, layout), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict("activity.definition.key-conflict", "Activity definition key conflict", "An activity definition with this activity type key already exists.", exception);
        }

        return new(ToIdentity(definition, authoring), [ToSummary(draft)], []);
    }

    public async Task<ReusableActivityDefinitionDetailsView> ForkDefinitionAsync(
        ForkReusableActivityDefinition command,
        CancellationToken cancellationToken)
    {
        EnsureProviderWrite(command.TargetProviderKey);
        EnsureDisplayName(command.DisplayName);
        var sourceAuthoring = await GetAuthoringAsync(command.DefinitionId, cancellationToken);
        EnsureVisible(sourceAuthoring.TenantId);
        if (sourceAuthoring.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource)
            throw BadRequest("Only a source-owned definition version can be forked through this operation.");
        var source = await GetPublicationAsync(command.SourceVersionId, cancellationToken);
        if (!string.Equals(source.DefinitionId, command.DefinitionId, StringComparison.Ordinal))
            throw NotFound("activity.version.not-found", "Activity version not found", "The exact source version was not found for this definition.");
        EnsureVisible(source.TenantId);

        IActivityProvider targetProvider;
        try
        {
            targetProvider = providers.Resolve(command.TargetProviderKey, command.TargetProviderSchemaVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw MigrationUnsupported(source.Provider, command.TargetProviderKey, command.TargetProviderSchemaVersion, [], exception);
        }

        var migration = await targetProvider.MigrateAsync(
            new(source.Provider, command.TargetProviderSchemaVersion),
            cancellationToken);
        if (migration.Manifest is null || migration.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw MigrationUnsupported(source.Provider, command.TargetProviderKey, command.TargetProviderSchemaVersion, migration.Diagnostics);

        var sourceLayout = await layoutStore.FindVersionLayoutAsync(command.SourceVersionId, cancellationToken)
            ?? throw OperationFailed("The source version layout is unavailable.");
        var now = timeProvider.GetUtcNow();
        var definitionId = NewId("activity-def");
        var draftId = NewId("activity-draft");
        var definition = NewDefinition(definitionId, command.ActivityTypeKey, command.Category, command.DisplayName, command.Description, now);
        var authoring = NewAuthoring(
            definitionId,
            new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            new(command.DefinitionId, source.DefinitionVersionId, source.Version),
            now);
        var draft = NewDraft(draftId, definitionId, source.DefinitionVersionId, source.Contract, migration.Manifest, now);
        var layout = NewDraftLayout(draftId, sourceLayout.Records.ToArray(), now);

        try
        {
            await createDefinition.ExecuteAsync(new(definition, authoring, draft, layout), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict("activity.definition.key-conflict", "Activity definition key conflict", "The fork target could not be created because its identity or key already exists.", exception);
        }

        return new(ToIdentity(definition, authoring), [ToSummary(draft)], []);
    }

    public async Task<ReusableActivityDefinitionDetailsView> UpdateDefinitionAsync(
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

        await updateDefinitionPresentation.ExecuteAsync(new(
            command.DefinitionId,
            context.TenantId,
            command.Category,
            command.DisplayName,
            command.Description,
            timeProvider.GetUtcNow()), cancellationToken);

        return await GetDefinitionAsync(command.DefinitionId, cancellationToken);
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

        EnsureProviderWrite(provider.ProviderKey);
        var now = timeProvider.GetUtcNow();
        var draft = NewDraft(NewId("activity-draft"), command.DefinitionId, command.SourceVersionId, contract, provider, now);
        var layout = NewDraftLayout(draft.Id, records, now);
        try
        {
            await createDraft.ExecuteAsync(new(draft, layout, authoring.HeadVersionId), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict("activity.definition.stale-head", "Activity definition head is stale", "The definition head changed while the draft was being created.", exception);
        }

        return ToDraftView(draft, layout.Records.ToArray(), null);
    }

    public async Task<ReusableActivityDraftView> ReplaceDraftAsync(
        ReplaceReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        EnsureProviderWrite(command.Provider.ProviderKey);
        var current = await GetDraftAsync(command.DraftId, cancellationToken);
        var authoring = await GetAuthoringAsync(current.DefinitionId, cancellationToken);
        EnsureVisible(current.TenantId);
        EnsureDesignAuthority(authoring);
        EnsureActiveRevision(current, command.ExpectedRevision);

        ActivityDefinitionDraft updated;
        try
        {
            updated = await replaceDraft.ExecuteAsync(
                new(command.DraftId, command.ExpectedRevision, new(ToDomainContract(command.Contract), command.Provider, current.State.Options), command.Layout),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw StaleRevision(current, command.ExpectedRevision, exception);
        }

        return ToDraftView(updated, command.Layout, null);
    }

    public async Task<ReusableActivityDraftView> MigrateDraftAsync(
        MigrateReusableActivityDraft command,
        CancellationToken cancellationToken)
    {
        EnsureProviderWrite(command.TargetProviderKey);
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
            now);
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

        return ToDraftView(migrated, migratedLayout.Records.ToArray(), null);
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
            throw StaleRevision(current, command.ExpectedRevision, exception);
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

        var validation = new ActivityDraftValidationState
        {
            Id = NewId("activity-validation"),
            TenantId = draft.TenantId,
            DraftId = draft.Id,
            Revision = draft.Revision,
            ValidatedAt = result.ValidatedAt,
            Diagnostics = ActivityDiagnosticOrderer.Order(result.Diagnostics).ToList(),
            CreatedAt = result.ValidatedAt,
            LastModifiedAt = result.ValidatedAt
        };
        try
        {
            await storeValidation.ExecuteAsync(validation, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw StaleRevision(draft, command.ExpectedRevision, exception);
        }

        return ToValidationView(validation);
    }

    public async Task<IReadOnlyList<ActivityDefinitionIdentityView>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        var catalog = await definitions.ListAsync(new ActivityDefinitionFilter(), cancellationToken);
        var visible = catalog.Where(x => IsVisible(x.TenantId)).OrderBy(x => x.ActivityTypeKey, StringComparer.Ordinal).ToArray();
        var authoring = (await authoringStore.ListAsync(visible.Select(x => x.Id), cancellationToken))
            .ToDictionary(x => x.DefinitionId, StringComparer.Ordinal);
        return visible.Where(x => authoring.ContainsKey(x.Id)).Select(x => ToIdentity(x, authoring[x.Id])).ToArray();
    }

    public async Task<ReusableActivityDefinitionDetailsView> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definition = await GetDefinitionEntityAsync(definitionId, cancellationToken);
        var authoring = await GetAuthoringAsync(definitionId, cancellationToken);
        EnsureVisible(definition.TenantId);
        var draftsTask = draftStore.ListByDefinitionAsync(definitionId, cancellationToken);
        var versionsTask = publicationStore.ListByDefinitionAsync(definitionId, cancellationToken);
        await Task.WhenAll(draftsTask, versionsTask);
        return new(
            ToIdentity(definition, authoring),
            draftsTask.Result.Select(ToSummary).ToArray(),
            versionsTask.Result.Select(ToSummary).ToArray());
    }

    public async Task<IReadOnlyList<ReusableActivityDraftSummaryView>> ListDraftsAsync(string definitionId, CancellationToken cancellationToken)
    {
        var authoring = await GetAuthoringAsync(definitionId, cancellationToken);
        EnsureVisible(authoring.TenantId);
        return (await draftStore.ListByDefinitionAsync(definitionId, cancellationToken)).Select(ToSummary).ToArray();
    }

    public async Task<ReusableActivityDraftView> GetDraftViewAsync(string draftId, CancellationToken cancellationToken)
    {
        var draft = await GetDraftAsync(draftId, cancellationToken);
        EnsureVisible(draft.TenantId);
        var layoutTask = layoutStore.FindDraftLayoutAsync(draftId, cancellationToken);
        var validationTask = validationStore.FindAsync(draftId, draft.Revision, cancellationToken);
        await Task.WhenAll(layoutTask, validationTask);
        var layout = layoutTask.Result ?? throw OperationFailed("The draft layout is unavailable.");
        return ToDraftView(draft, layout.Records.ToArray(), validationTask.Result);
    }

    public async Task<IReadOnlyList<ReusableActivityVersionSummaryView>> ListVersionsAsync(string definitionId, CancellationToken cancellationToken)
    {
        var authoring = await GetAuthoringAsync(definitionId, cancellationToken);
        EnsureVisible(authoring.TenantId);
        return (await publicationStore.ListByDefinitionAsync(definitionId, cancellationToken)).Select(ToSummary).ToArray();
    }

    public async Task<ReusableActivityVersionView> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var version = await GetPublicationAsync(versionId, cancellationToken);
        EnsureVisible(version.TenantId);
        var definition = await GetDefinitionEntityAsync(version.DefinitionId, cancellationToken);
        var authoring = await GetAuthoringAsync(version.DefinitionId, cancellationToken);
        return new(
            ToIdentity(definition, authoring),
            version.DefinitionVersionId,
            version.Version,
            version.SourceDraftId,
            version.SourceVersionId,
            version.Contract.ToView(),
            ToProviderView(version.Provider),
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
        DateTimeOffset now) => new()
    {
        Id = draftId,
        TenantId = context.TenantId,
        DefinitionId = definitionId,
        Revision = 1,
        SourceVersionId = sourceVersionId,
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

    private void EnsureProviderWrite(string providerKey)
    {
        if (!context.CanAuthorProvider(providerKey))
            throw Forbidden("The caller is not authorized to author this activity provider.");
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

    private static void EnsureActiveRevision(ActivityDefinitionDraft draft, long expectedRevision)
    {
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict("activity.draft.stale-revision", "Activity draft is not active", "Only an active draft can be changed.");
        if (draft.Revision != expectedRevision)
            throw StaleRevision(draft, expectedRevision);
    }

    private ActivityProviderManifestView ToProviderView(ActivityProviderManifest provider) => new(
        provider.ProviderKey,
        provider.SchemaVersion,
        context.CanReadProviderPayload(provider.ProviderKey) ? provider.Payload.Clone() : null);

    private ActivityDefinitionIdentityView ToIdentity(ActivityDefinition definition, ActivityDefinitionAuthoringState authoring) => new(
        definition.Id,
        definition.ActivityTypeKey,
        definition.TenantId,
        definition.Category,
        string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ActivityTypeKey : definition.DisplayName,
        definition.Description,
        authoring.ContentAuthority,
        authoring.ForkedFrom,
        authoring.HeadVersionId);

    private static ReusableActivityDraftSummaryView ToSummary(ActivityDefinitionDraft draft) => new(
        draft.Id,
        draft.DefinitionId,
        draft.Revision,
        draft.SourceVersionId,
        draft.Status,
        draft.State.Provider.ProviderKey,
        draft.State.Provider.SchemaVersion,
        draft.LastModifiedAt);

    private static ReusableActivityVersionSummaryView ToSummary(ActivityDefinitionVersionPublication version) => new(
        version.DefinitionVersionId,
        version.DefinitionId,
        version.Version,
        version.Lifecycle,
        version.PublishedAt);

    private ReusableActivityDraftView ToDraftView(
        ActivityDefinitionDraft draft,
        IReadOnlyList<ActivityLayoutRecord> layout,
        ActivityDraftValidationState? validation) => new(
        draft.Id,
        draft.DefinitionId,
        draft.TenantId,
        draft.Revision,
        draft.SourceVersionId,
        draft.Status,
        draft.State.Contract.ToView(),
        ToProviderView(draft.State.Provider),
        layout,
        validation is null ? null : ToValidationView(validation),
        draft.CreatedAt,
        draft.LastModifiedAt);

    private static ActivityDraftValidationView ToValidationView(ActivityDraftValidationState validation) => new(
        validation.DraftId,
        validation.Revision,
        validation.IsValid,
        validation.ValidatedAt,
        ActivityDiagnosticOrderer.Order(validation.Diagnostics));

    private string NewId(string prefix) => $"{prefix}-{identityGenerator.Generate()}";

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
                "activity.request.invalid",
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
        inner);

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

    private static ActivityAuthoringException BadRequest(string detail) => new(400, "activity.request.invalid", "Invalid activity authoring request", detail);
    private static ActivityAuthoringException Forbidden(string detail, string code = "activity.authorization.denied") => new(403, code, "Activity authoring is forbidden", detail);
    private static ActivityAuthoringException NotFound(string code, string title, string detail, Exception? inner = null) => new(404, code, title, detail, innerException: inner);
    private static ActivityAuthoringException Conflict(string code, string title, string detail, Exception? inner = null) => new(409, code, title, detail, innerException: inner);
    private static ActivityAuthoringException OperationFailed(string detail) => new(500, "activity.operation.failed", "Activity authoring operation failed", detail);
}

public sealed class CreateReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<CreateReusableActivityDefinition, ReusableActivityDefinitionDetailsView>
{
    public Task<ReusableActivityDefinitionDetailsView> Handle(CreateReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.CreateDefinitionAsync(command, cancellationToken);
}

public sealed class ForkReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<ForkReusableActivityDefinition, ReusableActivityDefinitionDetailsView>
{
    public Task<ReusableActivityDefinitionDetailsView> Handle(ForkReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.ForkDefinitionAsync(command, cancellationToken);
}

public sealed class UpdateReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : ICommandHandler<UpdateReusableActivityDefinition, ReusableActivityDefinitionDetailsView>
{
    public Task<ReusableActivityDefinitionDetailsView> Handle(UpdateReusableActivityDefinition command, CancellationToken cancellationToken) =>
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

public sealed class ListReusableActivityDefinitionsHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<ListReusableActivityDefinitions, IReadOnlyList<ActivityDefinitionIdentityView>>
{
    public Task<IReadOnlyList<ActivityDefinitionIdentityView>> Handle(ListReusableActivityDefinitions request, CancellationToken cancellationToken) =>
        service.ListDefinitionsAsync(cancellationToken);
}

public sealed class GetReusableActivityDefinitionHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<GetReusableActivityDefinition, ReusableActivityDefinitionDetailsView>
{
    public Task<ReusableActivityDefinitionDetailsView> Handle(GetReusableActivityDefinition request, CancellationToken cancellationToken) =>
        service.GetDefinitionAsync(request.DefinitionId, cancellationToken);
}

public sealed class ListReusableActivityDraftsHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<ListReusableActivityDrafts, IReadOnlyList<ReusableActivityDraftSummaryView>>
{
    public Task<IReadOnlyList<ReusableActivityDraftSummaryView>> Handle(ListReusableActivityDrafts request, CancellationToken cancellationToken) =>
        service.ListDraftsAsync(request.DefinitionId, cancellationToken);
}

public sealed class GetReusableActivityDraftHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<GetReusableActivityDraft, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(GetReusableActivityDraft request, CancellationToken cancellationToken) =>
        service.GetDraftViewAsync(request.DraftId, cancellationToken);
}

public sealed class ListReusableActivityVersionsHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<ListReusableActivityVersions, IReadOnlyList<ReusableActivityVersionSummaryView>>
{
    public Task<IReadOnlyList<ReusableActivityVersionSummaryView>> Handle(ListReusableActivityVersions request, CancellationToken cancellationToken) =>
        service.ListVersionsAsync(request.DefinitionId, cancellationToken);
}

public sealed class GetReusableActivityVersionHandler(ReusableActivityAuthoringService service)
    : IRequestHandler<GetReusableActivityVersion, ReusableActivityVersionView>
{
    public Task<ReusableActivityVersionView> Handle(GetReusableActivityVersion request, CancellationToken cancellationToken) =>
        service.GetVersionAsync(request.VersionId, cancellationToken);
}
