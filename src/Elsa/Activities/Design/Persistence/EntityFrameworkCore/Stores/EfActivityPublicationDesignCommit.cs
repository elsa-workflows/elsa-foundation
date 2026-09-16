using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The Activities Design half of a reusable-activity publication: every design kind, the dependency projection
/// and the management projection, committed in one Activities Design transaction.
/// </summary>
/// <remarks>
/// This transaction is the linearization point of the ordered publication (ADR 0066): the publication is done
/// exactly when it commits. The draft, the authoring state and both derived projections move under their own
/// optimistic concurrency tokens, so a concurrent publication loses here as it would in one transaction.
/// Every refusal is an <see cref="InvalidOperationException"/>; nothing is left tracked afterwards.
/// </remarks>
public sealed class EfActivityPublicationDesignCommit
{
    private readonly ActivitiesDesignDbContext db;
    private readonly EfActivityDesignStores stores;
    private readonly IPersistenceAccessContextAccessor? access;
    private readonly EfActivityManagementProjectionWriter managementProjection;

    public EfActivityPublicationDesignCommit(
        IActivityDefinitionVersionPublicationStore publications,
        IPersistenceAccessContextAccessor? accessContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(publications);
        // Writing design rows into this context while reads resolve another backend would misfile the
        // publication silently, so a composition whose Activities Design backend is not EF is refused here.
        stores = publications as EfActivityDesignStores ?? throw new InvalidOperationException(
            $"EF activity publication requires the EF Activities Design stores, but '{publications.GetType().FullName}' is registered.");
        db = stores.Context;
        access = accessContextAccessor;
        managementProjection = new EfActivityManagementProjectionWriter(db, accessContextAccessor);
    }

    /// <summary>
    /// Reads the current design state and refuses a publication that could not commit: a stale draft or
    /// head, a non-Design authority, or any design row the publication creates already existing. Nothing is
    /// written, so callers can run it before writing material another context owns.
    /// </summary>
    public async Task EnsureDraftPublicationAdmissibleAsync(ActivityPublicationDesignMutation mutation, CancellationToken cancellationToken = default)
    {
        Validate(mutation);
        try
        {
            _ = await LoadDraftPublicationAsync(mutation, tracking: false, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Whether this exact publication's design phase already committed: the stored publication carries its
    /// identity and material, and the draft it came from is published as that version. A different
    /// publication reusing the id is not this one, and the caller's create-only checks then refuse it.
    /// </summary>
    public async Task<bool> IsDraftPublicationCommittedAsync(ActivityPublicationDesignMutation mutation, CancellationToken cancellationToken = default)
    {
        Validate(mutation);
        var publication = mutation.Publication;
        try
        {
            var stored = await InScope(EfActivityDesignStores.ByReference(
                    db.ActivityDefinitionVersionPublications.AsNoTracking(),
                    nameof(ActivityDefinitionVersionPublication.DefinitionVersionId),
                    publication.DefinitionVersionId), publication.TenantId)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (stored.Count != 1 || !SamePublication(stored[0], publication))
                return false;

            var draft = await InScope(EfActivityDesignStores.ById(db.ActivityDefinitionDrafts.AsNoTracking(), mutation.DraftId), publication.TenantId)
                .SingleOrDefaultAsync(cancellationToken);
            return draft is not null &&
                   draft.Status == ActivityDefinitionDraftStatus.Published &&
                   StringComparer.Ordinal.Equals(draft.PublishedVersionId, publication.DefinitionVersionId);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Commits the design half of a draft publication.</summary>
    public async Task CommitDraftPublicationAsync(ActivityPublicationDesignMutation mutation, CancellationToken cancellationToken = default)
    {
        Validate(mutation);
        var publication = mutation.Publication;
        await CommitAsync(async () =>
        {
            var state = await LoadDraftPublicationAsync(mutation, tracking: true, cancellationToken);
            EfActivityDesignStores.PrepareVersion(mutation.CatalogVersion);
            db.ActivityDefinitionVersions.Add(mutation.CatalogVersion);
            db.ActivityDefinitionVersionPublications.Add(publication);
            db.ActivityDefinitionVersionLayouts.Add(mutation.Layout);
            db.ActivityDependencyEdges.AddRange(mutation.DirectDependencies);

            var establishesRecommendation = state.Authoring.HeadVersionId is null && state.Authoring.RecommendedVersionId is null;
            state.Draft.Status = ActivityDefinitionDraftStatus.Published;
            state.Draft.PublishedVersionId = publication.DefinitionVersionId;
            state.Draft.LastModifiedAt = publication.PublishedAt;
            state.Authoring.HeadVersionId = publication.DefinitionVersionId;
            if (establishesRecommendation)
                state.Authoring.RecommendedVersionId = publication.DefinitionVersionId;
            state.Authoring.LastModifiedAt = publication.PublishedAt;

            await StageDependencyProjectionAsync(mutation, cancellationToken);
            // The checkpoint writer saves every staged row, so the design rows and both projections land in
            // this one save inside the transaction.
            await managementProjection.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(
                publication.PublishedAt,
                [new EfActivityManagementDefinitionChange(state.Definition, state.Authoring)],
                [state.Draft],
                [publication]), cancellationToken);
        }, $"activity version publication '{publication.DefinitionVersionId}'", cancellationToken);
    }

    /// <summary>Refuses a source-owned publication that could not commit, without writing anything.</summary>
    public async Task EnsureSourcePublicationAdmissibleAsync<TExecutableTemplate, TSourceReference>(
        SourceActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        CancellationToken cancellationToken = default)
        where TExecutableTemplate : class
        where TSourceReference : class
    {
        ArgumentNullException.ThrowIfNull(commit);
        try
        {
            _ = await LoadSourcePublicationAsync(commit, tracking: false, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Commits the design half of a source-owned publication.</summary>
    public async Task CommitSourcePublicationAsync<TExecutableTemplate, TSourceReference>(
        SourceActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        CancellationToken cancellationToken = default)
        where TExecutableTemplate : class
        where TSourceReference : class
    {
        ArgumentNullException.ThrowIfNull(commit);
        await CommitAsync(async () =>
        {
            var state = await LoadSourcePublicationAsync(commit, tracking: true, cancellationToken);
            var definition = state.Definition ?? commit.Definition;
            if (state.Definition is null)
                db.ActivityDefinitions.Add(commit.Definition);

            var authoring = state.Authoring ?? commit.AuthoringState;
            if (state.Authoring is null)
                db.ActivityDefinitionAuthoringStates.Add(commit.AuthoringState);
            else
            {
                if (await ShouldAdvanceHeadAsync(state.Authoring.HeadVersionId, commit.CatalogVersion, cancellationToken))
                    state.Authoring.HeadVersionId = commit.CatalogVersion.Id;
                state.Authoring.LastModifiedAt = commit.Publication.PublishedAt;
            }

            if (!state.CatalogVersionExists)
            {
                EfActivityDesignStores.PrepareVersion(commit.CatalogVersion);
                db.ActivityDefinitionVersions.Add(commit.CatalogVersion);
            }

            db.ActivityDefinitionVersionPublications.Add(commit.Publication);
            db.ActivityDefinitionVersionLayouts.Add(commit.Layout);
            await managementProjection.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(
                commit.Publication.PublishedAt,
                [new EfActivityManagementDefinitionChange(definition, authoring)],
                [],
                [commit.Publication]), cancellationToken);
        }, $"source-owned activity version publication '{commit.Publication.DefinitionVersionId}'", cancellationToken);
    }

    private async Task CommitAsync(Func<Task> stage, string subject, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await stage();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAsync(transaction);
            throw new InvalidOperationException($"The {subject} lost an optimistic concurrency race and was rolled back.", exception);
        }
        catch (DesignPersistenceException exception) when (
            exception.InnerException is DbUpdateException providerFailure &&
            EfRelationalExceptionClassifier.IsUniqueConstraintViolation(providerFailure))
        {
            await RollbackAsync(transaction);
            throw new InvalidOperationException($"The {subject} lost a uniqueness race and was rolled back.", exception);
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
            // A failed save leaves its rows Added or Modified; clearing keeps a later, unrelated save in this
            // scope from committing the publication the caller saw fail.
            db.ChangeTracker.Clear();
        }
    }

    private static async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
    }

    private async Task<DraftPublicationState> LoadDraftPublicationAsync(
        ActivityPublicationDesignMutation mutation,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var publication = mutation.Publication;
        EnsureTenant(publication.TenantId);
        var draft = await EfActivityDesignStores.ById(Visible(db.ActivityDefinitionDrafts, tracking), mutation.DraftId)
                        .SingleOrDefaultAsync(cancellationToken)
                    ?? throw Conflict($"Activity draft '{mutation.DraftId}' was not found.");
        var authorings = await EfActivityDesignStores.ByReference(
                Visible(db.ActivityDefinitionAuthoringStates, tracking),
                nameof(ActivityDefinitionAuthoringState.DefinitionId),
                mutation.DefinitionId)
            .Take(2)
            .ToListAsync(cancellationToken);
        var authoring = authorings.Count switch
        {
            1 => authorings[0],
            0 => throw Conflict($"Authoring state for activity definition '{mutation.DefinitionId}' was not found."),
            _ => throw Conflict($"Multiple authoring states exist for activity definition '{mutation.DefinitionId}'.")
        };
        var definition = await EfActivityDesignStores.ById(Visible(db.ActivityDefinitions, tracking: false), mutation.DefinitionId)
                             .SingleOrDefaultAsync(cancellationToken)
                         ?? throw Conflict($"Activity definition '{mutation.DefinitionId}' was not found.");

        if (draft.Revision != mutation.ExpectedDraftRevision)
            throw Conflict("The activity draft revision changed before publication committed.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict("The activity draft is no longer active.");
        if (!StringComparer.Ordinal.Equals(draft.DefinitionId, mutation.DefinitionId) ||
            !StringComparer.Ordinal.Equals(authoring.DefinitionId, mutation.DefinitionId))
            throw Conflict("The publication owner identities do not align.");
        // The design rows are owned by the definition's tenant. A publication claiming another would move
        // them into a different physical scope from the draft it publishes.
        if (!StringComparer.Ordinal.Equals(draft.TenantId, publication.TenantId) ||
            !StringComparer.Ordinal.Equals(authoring.TenantId, publication.TenantId) ||
            !StringComparer.Ordinal.Equals(definition.TenantId, publication.TenantId))
            throw Conflict("The publication tenant does not match the draft, authoring state and definition it publishes.");
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, mutation.ExpectedDefinitionHeadVersionId))
            throw Conflict("The activity definition head changed before publication committed.");
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict("The activity definition is not Design-owned.");

        await EnsureNewVersionAsync(mutation.CatalogVersion, cancellationToken);
        await EnsurePublicationAbsentAsync(publication, cancellationToken);
        await EnsureAbsentAsync(db.ActivityDefinitionVersionLayouts, mutation.Layout.Id, mutation.Layout.TenantId, $"Activity version layout '{mutation.Layout.Id}'", cancellationToken);
        if (await InScope(EfActivityDesignStores.ByReference(
                    db.ActivityDefinitionVersionLayouts.AsNoTracking(),
                    nameof(ActivityDefinitionVersionLayout.DefinitionVersionId),
                    mutation.Layout.DefinitionVersionId), mutation.Layout.TenantId)
                .AnyAsync(cancellationToken))
            throw Conflict($"A layout already exists for activity version '{mutation.Layout.DefinitionVersionId}'.");
        foreach (var edge in mutation.DirectDependencies)
            await EnsureAbsentAsync(db.ActivityDependencyEdges, edge.Id, edge.TenantId, $"Activity dependency edge '{edge.Id}'", cancellationToken);
        return new(draft, authoring, definition);
    }

    private async Task<SourcePublicationState> LoadSourcePublicationAsync<TExecutableTemplate, TSourceReference>(
        SourceActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        bool tracking,
        CancellationToken cancellationToken)
        where TExecutableTemplate : class
        where TSourceReference : class
    {
        EnsureTenant(commit.Definition.TenantId);
        EnsureTenant(commit.Publication.TenantId);
        var definition = await InScope(EfActivityDesignStores.ById(db.ActivityDefinitions.AsNoTracking(), commit.Definition.Id), commit.Definition.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
        if (definition is not null && !StringComparer.Ordinal.Equals(definition.ActivityTypeKey, commit.Definition.ActivityTypeKey))
            throw Conflict($"Activity definition '{commit.Definition.Id}' is already bound to another source identity.");

        var authorings = await InScope(EfActivityDesignStores.ByReference(
                    tracking ? db.ActivityDefinitionAuthoringStates : db.ActivityDefinitionAuthoringStates.AsNoTracking(),
                    nameof(ActivityDefinitionAuthoringState.DefinitionId),
                    commit.Definition.Id), commit.AuthoringState.TenantId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (authorings.Count > 1)
            throw Conflict($"Multiple authoring states exist for activity definition '{commit.Definition.Id}'.");
        var authoring = authorings.SingleOrDefault();
        if (authoring is not null &&
            (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource ||
             !StringComparer.Ordinal.Equals(authoring.ContentAuthority.AuthorityKey, commit.AuthoringState.ContentAuthority.AuthorityKey) ||
             !StringComparer.Ordinal.Equals(authoring.ContentAuthority.SourceId, commit.AuthoringState.ContentAuthority.SourceId)))
            throw Conflict($"Activity definition '{commit.Definition.Id}' has a different content authority.");

        var version = await InScope(EfActivityDesignStores.ById(db.ActivityDefinitionVersions.AsNoTracking(), commit.CatalogVersion.Id), commit.CatalogVersion.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
        if (version is not null &&
            (!StringComparer.Ordinal.Equals(version.DefinitionId, commit.CatalogVersion.DefinitionId) ||
             !StringComparer.Ordinal.Equals(version.Version, commit.CatalogVersion.Version) ||
             !StringComparer.Ordinal.Equals(version.Hash, commit.CatalogVersion.Hash)))
            throw Conflict($"Catalog version '{commit.CatalogVersion.Id}' is already bound to different content.");

        await EnsurePublicationAbsentAsync(commit.Publication, cancellationToken);
        await EnsureAbsentAsync(db.ActivityDefinitionVersionLayouts, commit.Layout.Id, commit.Layout.TenantId, $"Activity version layout '{commit.Layout.Id}'", cancellationToken);
        return new(definition, authoring, version is not null);
    }

    private async Task EnsureNewVersionAsync(ActivityDefinitionVersion candidate, CancellationToken cancellationToken)
    {
        await EnsureAbsentAsync(db.ActivityDefinitionVersions, candidate.Id, candidate.TenantId, $"Activity version '{candidate.Id}'", cancellationToken);
        if (await InScope(EfActivityDesignStores.ByReference(
                    db.ActivityDefinitionVersions.AsNoTracking(),
                    nameof(ActivityDefinitionVersion.DefinitionId),
                    candidate.DefinitionId), candidate.TenantId)
                .AnyAsync(x => x.SemVerSortKey == candidate.SemVerSortKey, cancellationToken))
            throw Conflict($"Activity version '{candidate.Version}' already exists for definition '{candidate.DefinitionId}'.");
    }

    private async Task EnsurePublicationAbsentAsync(ActivityDefinitionVersionPublication publication, CancellationToken cancellationToken)
    {
        await EnsureAbsentAsync(db.ActivityDefinitionVersionPublications, publication.Id, publication.TenantId, $"Activity version publication '{publication.Id}'", cancellationToken);
        if (await InScope(EfActivityDesignStores.ByReference(
                    db.ActivityDefinitionVersionPublications.AsNoTracking(),
                    nameof(ActivityDefinitionVersionPublication.DefinitionVersionId),
                    publication.DefinitionVersionId), publication.TenantId)
                .AnyAsync(cancellationToken))
            throw Conflict($"Activity version '{publication.DefinitionVersionId}' is already published.");
    }

    private static async Task EnsureAbsentAsync<T>(DbSet<T> set, string id, string? tenantId, string subject, CancellationToken cancellationToken)
        where T : Elsa.Primitives.Entities.TenantEntity
    {
        if (await InScope(EfActivityDesignStores.ById(set.AsNoTracking(), id), tenantId).AnyAsync(cancellationToken))
            throw Conflict($"{subject} already exists.");
    }

    /// <summary>
    /// Replaces the source draft's derived dependency facts with the published version's, mirroring what the
    /// projection staged in the same commit. The EF projection is one global row holding every
    /// tenant's facts, so an owner is matched on its tenant as well as its kind and identity.
    /// </summary>
    private async Task StageDependencyProjectionAsync(ActivityPublicationDesignMutation mutation, CancellationToken cancellationToken)
    {
        var publication = mutation.Publication;
        var owner = new ActivityDefinitionReference(
            "ActivityVersion",
            publication.DefinitionId,
            publication.DefinitionVersionId,
            publication.Version,
            TemplateHash: publication.TemplateHash,
            TenantId: publication.TenantId,
            Lifecycle: publication.Lifecycle);
        var items = new List<ActivityDependencyItem>(mutation.DirectDependencies.Count);
        foreach (var edge in mutation.DirectDependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var target = await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync(edge.DependencyVersionId, cancellationToken)
                         ?? throw Conflict($"Dependency publication '{edge.DependencyVersionId}' was not found.");
            var dependency = new ActivityDefinitionReference(
                "ActivityVersion",
                target.DefinitionId,
                target.DefinitionVersionId,
                target.Version,
                TemplateHash: target.TemplateHash,
                TenantId: target.TenantId,
                Lifecycle: target.Lifecycle);
            items.Add(new(edge.Id, owner, dependency, new(edge.OccurrenceId, edge.NodeOrigin.ToArray()), true, 1, [owner, dependency]));
        }

        var sourceDraft = new ActivityDefinitionReference(
            "ActivityDraft",
            mutation.DefinitionId,
            DraftId: mutation.DraftId,
            Revision: mutation.ExpectedDraftRevision,
            TenantId: publication.TenantId);
        var current = await EfActivityDesignStores.ById(db.ActivityDependencyProjections, ActivityDependencyProjectionState.CurrentId)
            .SingleOrDefaultAsync(cancellationToken);
        var next = current?.Items.Where(item => !SameOwner(item.Owner, sourceDraft)).ToList() ?? [];
        next.AddRange(items);
        EfActivityDesignStores.ValidateProjectionItems(next);
        var ordered = next.OrderBy(EfActivityDesignStores.ItemSortKey, StringComparer.Ordinal).ToList();
        if (current is null)
        {
            db.ActivityDependencyProjections.Add(new ActivityDependencyProjectionState
            {
                Id = ActivityDependencyProjectionState.CurrentId,
                RebuildId = "incremental",
                Sequence = 1,
                AsOf = publication.PublishedAt,
                Items = ordered
            });
            return;
        }

        current.Sequence = checked(current.Sequence + 1);
        current.AsOf = publication.PublishedAt;
        current.Items = ordered;
    }

    private async Task<bool> ShouldAdvanceHeadAsync(string? currentHeadVersionId, ActivityDefinitionVersion candidate, CancellationToken cancellationToken)
    {
        if (currentHeadVersionId is null)
            return true;
        var current = await InScope(EfActivityDesignStores.ById(db.ActivityDefinitionVersions.AsNoTracking(), currentHeadVersionId), candidate.TenantId)
            .Select(x => x.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
            return true;
        return SemVer.TryParse(candidate.Version, out var candidateVersion) &&
               SemVer.TryParse(current, out var currentVersion) &&
               candidateVersion > currentVersion;
    }

    private static bool SameOwner(ActivityDefinitionReference left, ActivityDefinitionReference right) =>
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        StringComparer.Ordinal.Equals(left.DraftId ?? left.VersionId, right.DraftId ?? right.VersionId) &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId);

    private static bool SamePublication(ActivityDefinitionVersionPublication stored, ActivityDefinitionVersionPublication candidate) =>
        StringComparer.Ordinal.Equals(stored.Id, candidate.Id) &&
        StringComparer.Ordinal.Equals(stored.DefinitionId, candidate.DefinitionId) &&
        StringComparer.Ordinal.Equals(stored.DefinitionVersionId, candidate.DefinitionVersionId) &&
        StringComparer.Ordinal.Equals(stored.Version, candidate.Version) &&
        StringComparer.Ordinal.Equals(stored.TemplateId, candidate.TemplateId) &&
        StringComparer.Ordinal.Equals(stored.TemplateHash, candidate.TemplateHash) &&
        StringComparer.Ordinal.Equals(stored.SourceReferenceId, candidate.SourceReferenceId) &&
        StringComparer.Ordinal.Equals(stored.SourceDraftId, candidate.SourceDraftId) &&
        StringComparer.Ordinal.Equals(stored.TenantId, candidate.TenantId);

    private static void Validate(ActivityPublicationDesignMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var publication = mutation.Publication;
        if (!StringComparer.Ordinal.Equals(mutation.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(mutation.Layout.DefinitionVersionId, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(mutation.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(mutation.CatalogVersion.DefinitionId, publication.DefinitionId))
            throw new ArgumentException("Publication definition/version identities do not align.", nameof(mutation));
        if (!StringComparer.Ordinal.Equals(mutation.CatalogVersion.TenantId, publication.TenantId) ||
            !StringComparer.Ordinal.Equals(mutation.Layout.TenantId, publication.TenantId) ||
            mutation.DirectDependencies.Any(edge => !StringComparer.Ordinal.Equals(edge.TenantId, publication.TenantId)))
            throw new ArgumentException("Publication Design document tenants do not align.", nameof(mutation));
    }

    private IQueryable<T> Visible<T>(DbSet<T> set, bool tracking) where T : Elsa.Primitives.Entities.TenantEntity =>
        EfActivityDesignStores.AccessFilter(tracking ? set : set.AsNoTracking(), access);

    /// <summary>The rows in exactly <paramref name="tenantId"/>'s physical scope, the scope a create lands in.</summary>
    private static IQueryable<T> InScope<T>(IQueryable<T> query, string? tenantId) where T : Elsa.Primitives.Entities.TenantEntity
    {
        var scopeKey = ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);
        return query.Where(x => EF.Property<string>(x, "TenantScopeKey") == scopeKey && x.TenantId == tenantId);
    }

    private void EnsureTenant(string? tenantId) => access?.Current.EnsureTenantScope(tenantId);

    private static InvalidOperationException Conflict(string message) => new(message);

    private sealed record DraftPublicationState(ActivityDefinitionDraft Draft, ActivityDefinitionAuthoringState Authoring, ActivityDefinition Definition);

    private sealed record SourcePublicationState(ActivityDefinition? Definition, ActivityDefinitionAuthoringState? Authoring, bool CatalogVersionExists);
}
