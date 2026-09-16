using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;

public sealed record EfActivityManagementDefinitionChange(ActivityDefinition Definition, ActivityDefinitionAuthoringState Authoring);
public sealed record EfActivityManagementProjectionMutation(DateTimeOffset ChangedAt, IReadOnlyList<EfActivityManagementDefinitionChange> Definitions, IReadOnlyList<ActivityDefinitionDraft> Drafts, IReadOnlyList<ActivityDefinitionVersionPublication> Versions);

/// <summary>Writes one coherent temporal management projection checkpoint in the EF transaction.</summary>
public sealed class EfActivityManagementProjectionWriter(ActivitiesDesignDbContext db, IPersistenceAccessContextAccessor? accessContextAccessor = null)
{
    private readonly record struct ProjectionIdentity(string? TenantId, string Id);

    private readonly IPersistenceAccessContextAccessor? access = accessContextAccessor;
    public Task<long> ApplyAsync(EfActivityManagementProjectionMutation mutation, CancellationToken cancellationToken = default) => WriteAsync(mutation, cancellationToken);

    public async Task<long> WriteAsync(EfActivityManagementProjectionMutation mutation, CancellationToken cancellationToken = default)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await WriteInCurrentTransactionAsync(mutation, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DesignPersistenceException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            if (transaction is not null) await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "projection-checkpoint", null, exception);
        }
        catch
        {
            if (transaction is not null) await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Applies a checkpoint using the caller's already-open transaction. The caller owns commit and
    /// rollback, which is what lets another module's atomic write include the checkpoint.
    /// </summary>
    public Task<long> WriteInCurrentTransactionAsync(EfActivityManagementProjectionMutation mutation, CancellationToken cancellationToken = default) =>
        WriteCoreAsync(mutation, cancellationToken);

    private async Task<long> WriteCoreAsync(EfActivityManagementProjectionMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        foreach (var change in mutation.Definitions) EnsureTenant(change.Definition.TenantId);
        foreach (var draft in mutation.Drafts) EnsureTenant(draft.TenantId);
        foreach (var version in mutation.Versions) EnsureTenant(version.TenantId);
        EnsureUnique(mutation.Definitions.Select(x => new ProjectionIdentity(x.Definition.TenantId, x.Definition.Id)), "definition");
        EnsureUnique(mutation.Drafts.Select(x => new ProjectionIdentity(x.TenantId, x.Id)), "draft");
        EnsureUnique(mutation.Versions.Select(x => new ProjectionIdentity(x.TenantId, x.DefinitionVersionId)), "version");
        if (mutation.Definitions.Count == 0 && mutation.Drafts.Count == 0 && mutation.Versions.Count == 0)
            return (await db.ActivityManagementProjectionWatermarks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ActivityManagementProjectionWatermark.CurrentId, cancellationToken))?.Sequence ?? 0;

        {
            var watermark = await db.ActivityManagementProjectionWatermarks.SingleOrDefaultAsync(x => x.Id == ActivityManagementProjectionWatermark.CurrentId, cancellationToken)
                ?? new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 0, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UnixEpoch };
            if (watermark.Id == ActivityManagementProjectionWatermark.CurrentId && db.Entry(watermark).State == EntityState.Detached) db.Add(watermark);
            var sequence = checked(watermark.Sequence + 1);
            var changedAt = mutation.ChangedAt < watermark.AdvancedAt ? watermark.AdvancedAt : mutation.ChangedAt;
            var changed = mutation.Definitions.ToDictionary(x => new ProjectionIdentity(x.Definition.TenantId, x.Definition.Id));

            foreach (var change in mutation.Definitions)
            {
                if (change.Definition.Id != change.Authoring.DefinitionId || change.Definition.TenantId != change.Authoring.TenantId)
                    throw new ArgumentException("Definition projection ownership does not match authoring state.", nameof(mutation));
                await EnsureNoHiddenCurrentRevisionAsync(
                    db.ActivityDefinitionManagementProjections,
                    change.Definition.Id,
                    change.Definition.Id,
                    change.Definition.TenantId,
                    "definition",
                    cancellationToken);
                var current = await CurrentDefinition(change.Definition.Id, change.Definition.TenantId, cancellationToken);
                if (current is not null)
                {
                    if (!StringComparer.Ordinal.Equals(current.DefinitionId, change.Definition.Id))
                        throw new InvalidOperationException("Definition projection identity does not match the existing definition.");
                    if (current.TenantId != change.Definition.TenantId)
                        throw new InvalidOperationException("Definition projection ownership does not match the existing definition tenant.");
                }
                Close(current, sequence);
                db.ActivityDefinitionManagementProjections.Add(await ToDefinition(change, sequence, changedAt, current, cancellationToken));
            }
            foreach (var draft in mutation.Drafts)
            {
                await EnsureNoHiddenCurrentRevisionAsync(
                    db.ActivityDraftManagementProjections,
                    draft.Id,
                    draft.DefinitionId,
                    draft.TenantId,
                    "draft",
                    cancellationToken);
                var current = await ScopedResource(Access(db.ActivityDraftManagementProjections), draft.Id, draft.TenantId)
                    .SingleOrDefaultAsync(x => x.ResourceId == draft.Id && x.TenantId == draft.TenantId && x.ValidToSequenceExclusive == long.MaxValue, cancellationToken);
                await EnsureDefinitionOwner(draft.DefinitionId, draft.TenantId, changed, cancellationToken);
                EnsureChildOwner(current, draft.DefinitionId, draft.TenantId, "draft");
                Close(current, sequence);
                db.ActivityDraftManagementProjections.Add(ToDraft(draft, sequence));
                if (!changed.ContainsKey(new ProjectionIdentity(draft.TenantId, draft.DefinitionId)))
                    await CopyDefinitionForChildAsync(draft.DefinitionId, draft.TenantId, sequence, changedAt, cancellationToken);
            }
            foreach (var version in mutation.Versions)
            {
                await EnsureNoHiddenCurrentRevisionAsync(
                    db.ActivityVersionManagementProjections,
                    version.DefinitionVersionId,
                    version.DefinitionId,
                    version.TenantId,
                    "version",
                    cancellationToken);
                var current = await ScopedResource(Access(db.ActivityVersionManagementProjections), version.DefinitionVersionId, version.TenantId)
                    .SingleOrDefaultAsync(x => x.ResourceId == version.DefinitionVersionId && x.TenantId == version.TenantId && x.ValidToSequenceExclusive == long.MaxValue, cancellationToken);
                await EnsureDefinitionOwner(version.DefinitionId, version.TenantId, changed, cancellationToken);
                EnsureChildOwner(current, version.DefinitionId, version.TenantId, "version");
                Close(current, sequence);
                db.ActivityVersionManagementProjections.Add(ToVersion(version, sequence));
                if (!changed.ContainsKey(new ProjectionIdentity(version.TenantId, version.DefinitionId)))
                    await CopyDefinitionForChildAsync(version.DefinitionId, version.TenantId, sequence, changedAt, cancellationToken);
            }
            watermark.Sequence = sequence;
            watermark.AdvancedAt = changedAt;
            if (watermark.RetainedFromSequence == 0) watermark.RetainedFromSequence = sequence;
            db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = SequenceKey(sequence), Sequence = sequence, AsOf = changedAt });
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { throw; }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception)) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "projection-checkpoint-unique", null, exception); }
            catch (DbUpdateException exception) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "projection-checkpoint", null, exception); }
            return sequence;
        }
    }
    private static void EnsureChildOwner(ActivityManagementProjectionRevision? current, string definitionId, string? tenantId, string childKind)
    {
        if (current is null)
            return;
        if (!StringComparer.Ordinal.Equals(current.DefinitionId, definitionId))
            throw new InvalidOperationException($"Existing {childKind} projection definition identity does not match the incoming child.");
        if (current.TenantId != tenantId)
            throw new InvalidOperationException($"Existing {childKind} projection tenant does not match the incoming child.");
    }

    private static async Task EnsureNoHiddenCurrentRevisionAsync<T>(
        IQueryable<T> rows,
        string resourceId,
        string definitionId,
        string? tenantId,
        string projectionKind,
        CancellationToken token)
        where T : ActivityManagementProjectionRevision
    {
        // Access intentionally hides other tenants from normal reads. Probe only the target
        // physical scope: the same resource identity is legal in another tenant scope. Keep the
        // stored tenant as an exact residual, however, so a row whose physical scope was corrupted
        // to this target cannot be mistaken for an owned current revision.
        var owners = await ScopedResource(rows.AsNoTracking(), resourceId, tenantId)
            .Where(x => x.ResourceId == resourceId &&
                        x.ValidToSequenceExclusive == long.MaxValue)
            .Select(x => new { x.DefinitionId, x.TenantId })
            .ToListAsync(token);
        if (owners.Any(existing => !StringComparer.Ordinal.Equals(existing.DefinitionId, definitionId) ||
                                   !StringComparer.Ordinal.Equals(existing.TenantId, tenantId)))
            throw new InvalidOperationException($"Existing {projectionKind} projection ownership does not match the incoming resource.");
    }

    private async Task<ActivityDefinitionManagementProjectionRevision?> CurrentDefinition(string id, string? tenantId, CancellationToken token) =>
        await ScopedResource(Access(db.ActivityDefinitionManagementProjections), id, tenantId)
            .SingleOrDefaultAsync(x => x.ResourceId == id && x.TenantId == tenantId && x.ValidToSequenceExclusive == long.MaxValue, token);

    private async Task CopyDefinitionForChildAsync(string definitionId, string? tenantId, long sequence, DateTimeOffset changedAt, CancellationToken token)
    {
        var pending = db.ChangeTracker.Entries<ActivityDefinitionManagementProjectionRevision>()
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.DefinitionId == definitionId && x.TenantId == tenantId && x.ValidFromSequence == sequence && x.ValidToSequenceExclusive == long.MaxValue);
        if (pending is not null)
        {
            pending.DraftCount = await CountDraftsAsync(definitionId, pending.TenantId, token);
            pending.VersionCount = await CountVersionsAsync(definitionId, pending.TenantId, token);
            return;
        }
        var current = await CurrentDefinition(definitionId, tenantId, token) ?? throw new InvalidOperationException($"Activity definition projection '{definitionId}' does not exist.");
        Close(current, sequence);
        db.ActivityDefinitionManagementProjections.Add(new ActivityDefinitionManagementProjectionRevision
        {
            Id = RevisionId("definition", definitionId, sequence), ResourceId = current.ResourceId, DefinitionId = current.DefinitionId, TenantId = current.TenantId,
            ValidFromSequence = sequence, ValidToSequenceExclusive = long.MaxValue, ValidFromKey = SequenceKey(sequence), ValidToKey = SequenceKey(long.MaxValue),
            VisibilityKey = current.VisibilityKey, SortKey = current.SortKey, SearchText = current.SearchText, ActivityTypeKey = current.ActivityTypeKey,
            Category = current.Category, DisplayName = current.DisplayName, Description = current.Description, ContentAuthority = current.ContentAuthority, ContentAuthorityKind = current.ContentAuthorityKind,
            HeadVersionId = current.HeadVersionId, RecommendedVersionId = current.RecommendedVersionId, Head = current.Head, Recommendation = current.Recommendation,
            HeadProviderKey = current.HeadProviderKey, RecommendationProviderKey = current.RecommendationProviderKey,
            DraftCount = await CountDraftsAsync(definitionId, current.TenantId, token), VersionCount = await CountVersionsAsync(definitionId, current.TenantId, token), UpdatedAt = current.UpdatedAt, CreatedAt = current.CreatedAt, LastModifiedAt = changedAt
        });
    }

    private async Task<ActivityDefinitionManagementProjectionRevision> ToDefinition(EfActivityManagementDefinitionChange change, long sequence, DateTimeOffset changedAt, ActivityDefinitionManagementProjectionRevision? current, CancellationToken token)
    {
        var head = await FindHeadPublicationAsync(change.Authoring.HeadVersionId, change.Definition.Id, change.Definition.TenantId, token);
        var recommendation = await FindPublicationAsync(change.Authoring.RecommendedVersionId, change.Definition.TenantId);
        if (head is not null && (head.DefinitionId != change.Definition.Id || head.TenantId != change.Definition.TenantId) ||
            recommendation is not null && (recommendation.DefinitionId != change.Definition.Id || recommendation.TenantId != change.Definition.TenantId))
            throw new InvalidOperationException("Definition projection publication ownership does not match its definition.");
        return new()
    {
        Id = RevisionId("definition", change.Definition.Id, sequence), ResourceId = change.Definition.Id, DefinitionId = change.Definition.Id, TenantId = change.Definition.TenantId,
        ValidFromSequence = sequence, ValidToSequenceExclusive = long.MaxValue, ValidFromKey = SequenceKey(sequence), ValidToKey = SequenceKey(long.MaxValue), VisibilityKey = ActivitiesDesignDbContext.NormalizeTenantKey(change.Definition.TenantId), SortKey = change.Definition.Id,
        SearchText = SearchText(change.Definition.Id, change.Definition.ActivityTypeKey, change.Definition.Category, change.Definition.DisplayName, change.Definition.Description), ActivityTypeKey = change.Definition.ActivityTypeKey,
        Category = change.Definition.Category, DisplayName = change.Definition.DisplayName, Description = change.Definition.Description, ContentAuthority = change.Authoring.ContentAuthority, ContentAuthorityKind = change.Authoring.ContentAuthority.Kind,
        HeadVersionId = change.Authoring.HeadVersionId, RecommendedVersionId = change.Authoring.RecommendedVersionId,
        Head = head is null ? null : ToReference(head), Recommendation = recommendation is null ? null : ToReference(recommendation),
        HeadProviderKey = head?.Provider.ProviderKey, RecommendationProviderKey = recommendation?.Provider.ProviderKey,
        DraftCount = await CountDraftsAsync(change.Definition.Id, change.Definition.TenantId, token), VersionCount = await CountVersionsAsync(change.Definition.Id, change.Definition.TenantId, token),
        UpdatedAt = change.Definition.LastModifiedAt, CreatedAt = change.Definition.CreatedAt, LastModifiedAt = changedAt
    };
    }

    private async Task<long> CountDraftsAsync(string definitionId, string? tenantId, CancellationToken token)
    {
        var query = ScopedIdentity(
                Access(db.ActivityDefinitionDrafts.AsNoTracking()),
                nameof(ActivityDefinitionDraft.DefinitionId),
                definitionId,
                tenantId)
            .Where(x => x.DefinitionId == definitionId && x.TenantId == tenantId)
            .Select(x => x.Id)
            .Distinct();
        var persistedCount = await query.LongCountAsync(token);
        var pending = db.ChangeTracker.Entries<ActivityDefinitionDraft>()
            .Where(x => x.State == EntityState.Added && x.Entity.DefinitionId == definitionId && x.Entity.TenantId == tenantId)
            .Select(x => x.Entity.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (pending.Length == 0) return persistedCount;
        var alreadyPersisted = await query.Where(x => pending.Contains(x)).LongCountAsync(token);
        return persistedCount + pending.LongCount() - alreadyPersisted;
    }

    private async Task<long> CountVersionsAsync(string definitionId, string? tenantId, CancellationToken token)
    {
        var query = ScopedIdentity(
                Access(db.ActivityDefinitionVersionPublications.AsNoTracking()),
                nameof(ActivityDefinitionVersionPublication.DefinitionId),
                definitionId,
                tenantId)
            .Where(x => x.DefinitionId == definitionId && x.TenantId == tenantId)
            .Select(x => x.DefinitionVersionId)
            .Distinct();
        var persistedCount = await query.LongCountAsync(token);
        var pending = db.ChangeTracker.Entries<ActivityDefinitionVersionPublication>()
            .Where(x => x.State == EntityState.Added && x.Entity.DefinitionId == definitionId && x.Entity.TenantId == tenantId)
            .Select(x => x.Entity.DefinitionVersionId).Distinct(StringComparer.Ordinal).ToArray();
        if (pending.Length == 0) return persistedCount;
        var alreadyPersisted = await query.Where(x => pending.Contains(x)).LongCountAsync(token);
        return persistedCount + pending.LongCount() - alreadyPersisted;
    }

    private async Task<ActivityDefinitionVersionPublication?> FindPublicationAsync(string? versionId, string? tenantId)
    {
        if (versionId is null) return null;
        return await FindPublicationOrDefaultAsync(versionId, tenantId)
               ?? throw new InvalidOperationException($"Activity publication '{versionId}' was not found for the projection definition.");
    }

    /// <summary>
    /// A head names an immutable version, and a version can be the head before it has a publication: an
    /// imported Elsa 3 version is. Such a head projects no head reference. A head
    /// that names no version of this definition in this tenant is still corrupt and fails closed.
    /// </summary>
    private async Task<ActivityDefinitionVersionPublication?> FindHeadPublicationAsync(string? versionId, string definitionId, string? tenantId, CancellationToken token)
    {
        if (versionId is null) return null;
        var publication = await FindPublicationOrDefaultAsync(versionId, tenantId);
        if (publication is not null || await UnpublishedHeadVersionExistsAsync(versionId, definitionId, tenantId, token))
            return publication;
        throw new InvalidOperationException($"Activity publication '{versionId}' was not found for the projection definition.");
    }

    private async Task<ActivityDefinitionVersionPublication?> FindPublicationOrDefaultAsync(string versionId, string? tenantId)
    {
        var tracked = db.ChangeTracker.Entries<ActivityDefinitionVersionPublication>()
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.DefinitionVersionId == versionId && x.TenantId == tenantId);
        var publication = tracked ?? await ScopedIdentity(
                db.ActivityDefinitionVersionPublications.AsNoTracking(),
                nameof(ActivityDefinitionVersionPublication.DefinitionVersionId),
                versionId,
                tenantId)
            .SingleOrDefaultAsync(x => x.DefinitionVersionId == versionId && x.TenantId == tenantId);
        if (publication is not null && publication.TenantId != tenantId)
            throw new InvalidOperationException("Definition projection publication ownership does not match its definition.");
        return publication;
    }

    private async Task<bool> UnpublishedHeadVersionExistsAsync(string versionId, string definitionId, string? tenantId, CancellationToken token)
    {
        if (db.ChangeTracker.Entries<ActivityDefinitionVersion>().Any(x =>
                x.State != EntityState.Deleted &&
                StringComparer.Ordinal.Equals(x.Entity.Id, versionId) &&
                StringComparer.Ordinal.Equals(x.Entity.DefinitionId, definitionId) &&
                StringComparer.Ordinal.Equals(x.Entity.TenantId, tenantId)))
            return true;
        var candidates = await db.ActivityDefinitionVersions.AsNoTracking()
            .Where(x =>
                EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.NormalizeTenantKey(tenantId) &&
                EF.Property<string>(x, "IdIdentityHash") == ActivitiesDesignDbContext.ComputeIdentityHash(versionId))
            .Select(x => new { x.Id, x.DefinitionId, x.TenantId })
            .Take(2)
            .ToListAsync(token);
        return candidates.Any(x =>
            StringComparer.Ordinal.Equals(x.Id, versionId) &&
            StringComparer.Ordinal.Equals(x.DefinitionId, definitionId) &&
            StringComparer.Ordinal.Equals(x.TenantId, tenantId));
    }

    private static ActivityManagementVersionProjectionReference ToReference(ActivityDefinitionVersionPublication publication) =>
        new(publication.DefinitionVersionId, publication.Version, publication.Lifecycle, publication.Provider.ProviderKey, publication.Provider.SchemaVersion);

    private static ActivityDefinitionDraftManagementProjectionRevision ToDraft(ActivityDefinitionDraft draft, long sequence) => new()
    {
        Id = RevisionId("draft", draft.Id, sequence), ResourceId = draft.Id, DefinitionId = draft.DefinitionId, TenantId = draft.TenantId, ValidFromSequence = sequence, ValidToSequenceExclusive = long.MaxValue,
        ValidFromKey = SequenceKey(sequence), ValidToKey = SequenceKey(long.MaxValue), VisibilityKey = ActivitiesDesignDbContext.NormalizeTenantKey(draft.TenantId), SortKey = draft.Id, SearchText = SearchText(draft.Id, draft.PresentationLabel), DraftId = draft.Id,
        Revision = draft.Revision, SourceVersionId = draft.SourceVersionId, Status = draft.Status, ProviderKey = draft.State.Provider.ProviderKey, ProviderSchemaVersion = draft.State.Provider.SchemaVersion,
        PresentationLabel = draft.PresentationLabel, UpdatedAt = draft.LastModifiedAt, CreatedAt = draft.CreatedAt, LastModifiedAt = draft.LastModifiedAt
    };

    private static ActivityDefinitionVersionManagementProjectionRevision ToVersion(ActivityDefinitionVersionPublication version, long sequence) => new()
    {
        Id = RevisionId("version", version.DefinitionVersionId, sequence), ResourceId = version.DefinitionVersionId, DefinitionId = version.DefinitionId, TenantId = version.TenantId, ValidFromSequence = sequence, ValidToSequenceExclusive = long.MaxValue,
        ValidFromKey = SequenceKey(sequence), ValidToKey = SequenceKey(long.MaxValue), VisibilityKey = ActivitiesDesignDbContext.NormalizeTenantKey(version.TenantId), SortKey = version.DefinitionVersionId, SearchText = SearchText(version.DefinitionVersionId, version.Version),
        DefinitionVersionId = version.DefinitionVersionId, Version = version.Version, Lifecycle = version.Lifecycle, ProviderKey = version.Provider.ProviderKey, ProviderSchemaVersion = version.Provider.SchemaVersion,
        PublishedAt = version.PublishedAt, CreatedAt = version.CreatedAt, LastModifiedAt = version.LastModifiedAt
    };

    private static void Close(ActivityManagementProjectionRevision? current, long sequence) { if (current is not null) { current.ValidToSequenceExclusive = sequence; current.ValidToKey = SequenceKey(sequence); } }
    private async Task EnsureDefinitionOwner(string definitionId, string? tenantId, IReadOnlyDictionary<ProjectionIdentity, EfActivityManagementDefinitionChange> changed, CancellationToken token)
    {
        string? ownerTenantId;
        if (changed.TryGetValue(new ProjectionIdentity(tenantId, definitionId), out var owner))
            ownerTenantId = owner.Definition.TenantId;
        else
        {
            var current = await CurrentDefinition(definitionId, tenantId, token)
                ?? throw new InvalidOperationException($"Activity definition projection '{definitionId}' does not exist.");
            if (!StringComparer.Ordinal.Equals(current.DefinitionId, definitionId))
                throw new InvalidOperationException("Projection child definition identity does not match its existing definition projection.");
            ownerTenantId = current.TenantId;
        }
        if (ownerTenantId != tenantId)
            throw new InvalidOperationException("Projection child tenant does not match its definition.");
    }
    private static void EnsureUnique(IEnumerable<ProjectionIdentity> ids, string kind)
    {
        var duplicates = ids
            .GroupBy(x => x)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key.Id)
            .ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException($"Duplicate {kind} projection identity: {string.Join(", ", duplicates)}.");
    }

    private static IQueryable<T> ScopedResource<T>(IQueryable<T> query, string resourceId, string? tenantId)
        where T : ActivityManagementProjectionRevision =>
        ScopedIdentity(query, nameof(ActivityManagementProjectionRevision.ResourceId), resourceId, tenantId);

    private static IQueryable<T> IdentityHash<T>(IQueryable<T> query, string identityProperty, string identity)
        where T : Elsa.Primitives.Entities.TenantEntity =>
        query.Where(x => EF.Property<string>(x, $"{identityProperty}IdentityHash") == ActivitiesDesignDbContext.ComputeIdentityHash(identity));

    private static IQueryable<T> ScopedIdentity<T>(IQueryable<T> query, string identityProperty, string identity, string? tenantId)
        where T : Elsa.Primitives.Entities.TenantEntity =>
        query.Where(x =>
            EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.NormalizeTenantKey(tenantId) &&
            EF.Property<string>(x, $"{identityProperty}IdentityHash") == ActivitiesDesignDbContext.ComputeIdentityHash(identity));

    private static string SequenceKey(long sequence) => sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
    private static string RevisionId(string kind, string id, long sequence) => $"{kind}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant()}:{SequenceKey(sequence)}";
    private static string SearchText(params string?[] values) => string.Join('\u001f', values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToUpperInvariant()));
    private void EnsureTenant(string? tenantId)
    {
        access?.Current.EnsureTenantScope(tenantId);
    }

    private IQueryable<T> Access<T>(IQueryable<T> query) where T : Elsa.Primitives.Entities.TenantEntity
    {
        if (access is null || access.Current.AcrossScopes)
            return query;
        return access.Current.Scope is { } scope ? query.Where(x => x.TenantId == scope.Value || x.TenantId == null) : query.Where(x => x.TenantId == null);
    }

}

public sealed class EfActivityManagementProjectionRetention(ActivitiesDesignDbContext db, IPersistenceAccessContextAccessor? accessContextAccessor = null)
{
    private const int DeleteBatchSize = 256;
    private readonly IPersistenceAccessContextAccessor? access = accessContextAccessor;
    public Task ExpireBeforeAsync(long oldestRetainedSequence, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        if (oldestRetainedSequence < 1) throw new ArgumentOutOfRangeException(nameof(oldestRetainedSequence));
        return ExpireCoreAsync(oldestRetainedSequence, changedAt, cancellationToken);
    }

    private async Task ExpireCoreAsync(long oldest, DateTimeOffset changedAt, CancellationToken token)
    {
        if (access is not null && (access.Current.AccessPolicy != Elsa.Workflows.Runtime.Core.Models.PersistenceAccessPolicy.Privileged || !access.Current.AcrossScopes))
            throw new InvalidOperationException("Projection retention requires privileged cross-scope persistence access.");
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(token);
            var watermark = await db.ActivityManagementProjectionWatermarks.SingleOrDefaultAsync(x => x.Id == ActivityManagementProjectionWatermark.CurrentId, token) ?? throw new InvalidOperationException("The activity-management projection is not initialized.");
            if (oldest <= watermark.RetainedFromSequence) return;
            if (oldest > watermark.Sequence) throw new ArgumentOutOfRangeException(nameof(oldest));
            await DeleteInBatchesAsync(db.ActivityDefinitionManagementProjections, x => x.ValidToSequenceExclusive <= oldest, token);
            await DeleteInBatchesAsync(db.ActivityDraftManagementProjections, x => x.ValidToSequenceExclusive <= oldest, token);
            await DeleteInBatchesAsync(db.ActivityVersionManagementProjections, x => x.ValidToSequenceExclusive <= oldest, token);
            await DeleteInBatchesAsync(db.ActivityManagementProjectionSnapshots, x => x.Sequence < oldest, token);
            watermark.RetainedFromSequence = oldest;
            watermark.LastModifiedAt = changedAt;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
        catch (OperationCanceledException) when (tx is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(tx);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException) when (tx is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(tx);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DesignPersistenceException) when (tx is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(tx);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            if (tx is not null) await EfPersistenceCleanup.RollbackQuietlyAsync(tx);
            db.ChangeTracker.Clear();
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "projection-retention", null, exception);
        }
        catch
        {
            if (tx is not null) await EfPersistenceCleanup.RollbackQuietlyAsync(tx);
            db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private async Task DeleteInBatchesAsync<TEntity>(DbSet<TEntity> set, System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken token)
        where TEntity : class
    {
        while (true)
        {
            var batch = await set.Where(predicate).Take(DeleteBatchSize).ToListAsync(token);
            if (batch.Count == 0) return;
            set.RemoveRange(batch);
            await db.SaveChangesAsync(token);
        }
    }

}
