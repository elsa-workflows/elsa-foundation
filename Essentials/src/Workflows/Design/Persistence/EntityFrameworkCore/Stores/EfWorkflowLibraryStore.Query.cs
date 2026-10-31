using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    private static WorkflowLibraryQuery NormalizeQuery(WorkflowLibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query); ValidatePage(query.PageSize);
        if (query.State is not ("active" or "deleted" or "all")) throw new ArgumentException("Select active, deleted or all workflow definitions.");
        if (query.Sort is not ("name" or "name-desc" or "modified")) throw new ArgumentException("Unsupported workflow sort order.");
        if (query.PublicationState is not (null or "published")) throw new ArgumentException("The library publication filter currently supports published workflows only.");
        if (query.FolderId is not null && query.Unfiled) throw new ArgumentException("Choose a folder or Unfiled, not both.");
        if (query.FolderId is not null) ValidateStoredId(query.FolderId, "folder-");
        if (query.SearchTerm?.Length > 2048) throw new ArgumentException("Workflow search cannot exceed 2048 characters.");
        var filters = query.TagFilters ?? [];
        if (filters.Count > 16) throw new ArgumentException("A workflow query supports at most 16 tag clauses.");
        var normalized = new List<WorkflowTagFilter>();
        foreach (var filter in filters)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ValidateStoredId(filter.TagId, "tag-");
            if (filter.Operator is not ("exists" or "missing" or "any" or "all" or "none")) throw new ArgumentException("Unsupported tag filter operator.");
            var values = filter.ValueIds ?? [];
            if (values.Count > 32) throw new ArgumentException("A tag clause supports at most 32 selected values.");
            foreach (var id in values) ValidateStoredId(id, "value-");
            if (filter.Operator is "exists" or "missing" ? values.Count != 0 : values.Count == 0)
                throw new ArgumentException("The tag filter operator and selected values do not match.");
            normalized.Add(filter with { ValueIds = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() });
        }
        return query with { SearchTerm = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim(),
            IncludeDescendants = query.FolderId is not null && query.IncludeDescendants,
            TagFilters = normalized.OrderBy(filter => filter.TagId, StringComparer.Ordinal).ThenBy(filter => filter.Operator, StringComparer.Ordinal).ToArray() };
    }

    private async Task ValidateQueryReferencesAsync(WorkflowLibraryQuery query, CancellationToken token)
    {
        if (query.FolderId is not null) await FolderAsync(query.FolderId, token);
        var filters = query.TagFilters ?? [];
        var ids = filters.Select(filter => filter.TagId).Distinct(StringComparer.Ordinal).ToArray();
        var tags = ids.Length == 0 ? [] : await TagRows.AsNoTracking().Where(row => ids.Contains(row.Id)).ToArrayAsync(token);
        if (tags.Length != ids.Length) Missing<WorkflowTag>("filter tag");
        foreach (var filter in filters)
        {
            var tag = tags.Single(row => row.Id == filter.TagId);
            if (tag.Mode == "marker" && filter.Operator is not ("exists" or "missing"))
                throw new ArgumentException("Marker tags support exists and missing filters.");
            if (filter.Operator == "all" && tag.Cardinality != "multiple")
                throw new ArgumentException("All-values filters require a multiple-valued controlled tag.");
            var values = Values(tag);
            if ((filter.ValueIds ?? []).Any(id => !values.Any(value => value.Id == id)))
                throw new ArgumentException("The filter references an unavailable controlled tag value.");
        }
    }

    public async Task<WorkflowLibraryPage> ListDefinitionsAsync(WorkflowLibraryQuery query, string? subjectId,
        CancellationToken cancellationToken = default)
    {
        query = NormalizeQuery(query);
        if (subjectId is not null) ValidateActor(subjectId);
        if (query.FavoritesOnly && subjectId is null) throw new ArgumentException("Favorites require an authenticated personal subject.");
        await ValidateQueryReferencesAsync(query, cancellationToken);
        var binding = Binding("definitions", query with { ContinuationToken = null }, subjectId);
        var cursor = Decode(query.ContinuationToken, binding);
        var source = DefinitionRows.AsNoTracking();
        if (query.State == "active") source = source.Where(row => row.DeletedAt == null);
        else if (query.State == "deleted") source = source.Where(row => row.DeletedAt != null);
        if (query.SearchTerm is not null)
        {
            var search = EfDesignSupport.SearchKey(query.SearchTerm);
            source = source.Where(row => row.IdSearchKey.Contains(search) || row.NameSearchKey!.Contains(search) || row.DescriptionSearchKey != null && row.DescriptionSearchKey.Contains(search));
        }
        var organizations = OrganizationRows.AsNoTracking();
        if (query.Unfiled)
            source = source.Where(row => !organizations.Any(organization => organization.DefinitionHash == row.IdLookupHash && organization.FolderId != null));
        if (query.FolderId is not null)
        {
            var folderId = query.FolderId;
            if (query.IncludeDescendants)
            {
                var folder = await FolderAsync(folderId, cancellationToken); var path = folder.Path;
                var folders = FolderRows.AsNoTracking().Where(candidate => candidate.Path.StartsWith(path));
                source = source.Where(row => organizations.Any(organization => organization.DefinitionHash == row.IdLookupHash &&
                    folders.Any(candidate => candidate.Id == organization.FolderId)));
            }
            else source = source.Where(row => organizations.Any(organization => organization.DefinitionHash == row.IdLookupHash && organization.FolderId == folderId));
        }
        if (query.PublicationState == "published")
            source = source.Where(row => organizations.Any(organization => organization.DefinitionHash == row.IdLookupHash && organization.Published));
        var assignments = AssignmentRows.AsNoTracking();
        foreach (var filter in query.TagFilters ?? [])
        {
            var tagId = filter.TagId; var values = (filter.ValueIds ?? []).ToArray();
            if (filter.Operator == "exists") source = source.Where(row => assignments.Any(assignment => assignment.DefinitionHash == row.IdLookupHash && assignment.TagId == tagId));
            else if (filter.Operator == "missing") source = source.Where(row => !assignments.Any(assignment => assignment.DefinitionHash == row.IdLookupHash && assignment.TagId == tagId));
            else if (filter.Operator == "any") source = source.Where(row => assignments.Any(assignment => assignment.DefinitionHash == row.IdLookupHash && assignment.TagId == tagId && values.Contains(assignment.ValueKey)));
            else if (filter.Operator == "none") source = source.Where(row => !assignments.Any(assignment => assignment.DefinitionHash == row.IdLookupHash && assignment.TagId == tagId && values.Contains(assignment.ValueKey)));
            else foreach (var value in values)
            {
                var valueId = value;
                source = source.Where(row => assignments.Any(assignment => assignment.DefinitionHash == row.IdLookupHash && assignment.TagId == tagId && assignment.ValueKey == valueId));
            }
        }
        var subjectHash = subjectId is null ? "" : EfDesignSupport.LookupHash(subjectId);
        var favorites = Scoped(db.Favorites).AsNoTracking().Where(favorite => favorite.SubjectHash == subjectHash && favorite.SubjectId == subjectId);
        if (query.FavoritesOnly) source = source.Where(row => favorites.Any(favorite => favorite.DefinitionHash == row.IdLookupHash));
        var total = await source.CountAsync(cancellationToken);
        var pageSource = source;
        if (cursor is not null)
        {
            if (query.Sort == "modified") pageSource = pageSource.Where(row => EF.Property<long>(row, "LibraryModifiedTicks") < cursor.Ticks ||
                EF.Property<long>(row, "LibraryModifiedTicks") == cursor.Ticks && row.IdLookupHash.CompareTo(cursor.Id) > 0);
            else if (query.Sort == "name-desc") pageSource = pageSource.Where(row => row.NameSearchKey!.CompareTo(cursor.Key) < 0 || row.NameSearchKey == cursor.Key && row.IdLookupHash.CompareTo(cursor.Id) > 0);
            else pageSource = pageSource.Where(row => row.NameSearchKey!.CompareTo(cursor.Key) > 0 || row.NameSearchKey == cursor.Key && row.IdLookupHash.CompareTo(cursor.Id) > 0);
        }
        var ordered = query.Sort switch {
            "modified" => pageSource.OrderByDescending(row => EF.Property<long>(row, "LibraryModifiedTicks")).ThenBy(row => row.IdLookupHash),
            "name-desc" => pageSource.OrderByDescending(row => row.NameSearchKey).ThenBy(row => row.IdLookupHash),
            _ => pageSource.OrderBy(row => row.NameSearchKey).ThenBy(row => row.IdLookupHash) };
        var selected = await ordered.Take(query.PageSize + 1).Select(row => new { Definition = row, Ticks = EF.Property<long>(row, "LibraryModifiedTicks") }).ToArrayAsync(cancellationToken);
        var definitions = selected.Take(query.PageSize).Select(row => row.Definition).ToArray();
        if (definitions.Length == 0) return new([], null, total);
        var hashes = definitions.Select(row => row.IdLookupHash).ToArray();
        var organizationRows = await organizations.Where(row => hashes.Contains(row.DefinitionHash)).ToArrayAsync(cancellationToken);
        var tagRows = await assignments.Where(row => hashes.Contains(row.DefinitionHash)).OrderBy(row => row.DefinitionHash)
            .ThenBy(row => row.TagId).ThenBy(row => row.ValueKey).Take(query.PageSize * MaximumAssignments + 1).ToArrayAsync(cancellationToken);
        if (tagRows.Length > query.PageSize * MaximumAssignments) throw new InvalidOperationException("Stored workflow tag assignments exceed their bound.");
        var favoriteHashes = subjectId is null ? [] : await favorites.Where(row => hashes.Contains(row.DefinitionHash)).Select(row => row.DefinitionHash).ToArrayAsync(cancellationToken);
        var versions = Scoped(db.Versions).AsNoTracking(); var drafts = Scoped(db.Drafts).AsNoTracking();
        // Correlated scalar projections leave version history in SQL. Neither authored state nor layout payloads are loaded.
        var projections = await DefinitionRows.AsNoTracking().Where(row => hashes.Contains(row.IdLookupHash)).Select(row => new LibraryProjection {
            Hash = row.IdLookupHash,
            VersionCount = versions.Count(version => version.DefinitionIdLookupHash == row.IdLookupHash),
            LatestVersionId = versions.Where(version => version.DefinitionIdLookupHash == row.IdLookupHash).OrderByDescending(version => version.SemVerSortKey)
                .ThenByDescending(version => version.IdLookupHash).Select(version => version.Id).FirstOrDefault(),
            LatestVersion = versions.Where(version => version.DefinitionIdLookupHash == row.IdLookupHash).OrderByDescending(version => version.SemVerSortKey)
                .ThenByDescending(version => version.IdLookupHash).Select(version => version.Version).FirstOrDefault(),
            DraftId = drafts.Where(draft => draft.WorkflowDefinitionIdLookupHash == row.IdLookupHash).OrderByDescending(draft => EF.Property<long>(draft, "LibraryModifiedTicks"))
                .ThenByDescending(draft => EF.Property<long>(draft, "LibraryCreatedTicks")).ThenByDescending(draft => EF.Property<string>(draft, "LibraryIdentitySortKey")).Select(draft => draft.Id).FirstOrDefault(),
            DraftSourceVersionId = drafts.Where(draft => draft.WorkflowDefinitionIdLookupHash == row.IdLookupHash).OrderByDescending(draft => EF.Property<long>(draft, "LibraryModifiedTicks"))
                .ThenByDescending(draft => EF.Property<long>(draft, "LibraryCreatedTicks")).ThenByDescending(draft => EF.Property<string>(draft, "LibraryIdentitySortKey")).Select(draft => draft.SourceVersionId).FirstOrDefault()
        }).ToArrayAsync(cancellationToken);
        var sourceIds = projections.Where(projection => projection.DraftSourceVersionId is not null).Select(projection => projection.DraftSourceVersionId!).Distinct(StringComparer.Ordinal).ToArray();
        var sourceHashes = sourceIds.Select(EfDesignSupport.LookupHash).ToArray();
        var sourceVersions = sourceHashes.Length == 0 ? [] : await versions.Where(version => sourceHashes.Contains(version.IdLookupHash))
            .Select(version => new { version.Id, version.Version }).ToArrayAsync(cancellationToken);
        var breadcrumbMap = await PageBreadcrumbsAsync(organizationRows.Select(row => row.FolderId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
        var items = definitions.Select(definition => {
            var org = organizationRows.SingleOrDefault(row => row.DefinitionHash == definition.IdLookupHash);
            var projection = projections.Single(row => row.Hash == definition.IdLookupHash);
            var orgTags = tagRows.Where(row => row.DefinitionHash == definition.IdLookupHash).Select(row => new WorkflowTagAssignment(row.TagId, row.ValueKey.Length == 0 ? null : row.ValueKey)).ToArray();
            var organization = new WorkflowOrganization(definition.Id, org?.FolderId, org?.FolderId is { } folder ? breadcrumbMap[folder] : [], orgTags, org?.Revision ?? "0");
            var sourceVersion = sourceVersions.SingleOrDefault(version => StringComparer.Ordinal.Equals(version.Id, projection.DraftSourceVersionId));
            return new WorkflowLibraryDefinition(definition, organization, favoriteHashes.Contains(definition.IdLookupHash, StringComparer.Ordinal),
                projection.DraftId, projection.LatestVersionId, projection.LatestVersion, projection.VersionCount,
                projection.DraftSourceVersionId, sourceVersion?.Version, PublicationState: org?.Published == true ? "published" : null);
        }).ToArray();
        var last = definitions[^1];
        return new(items, selected.Length > query.PageSize ? Encode(new(binding, last.NameSearchKey ?? "", last.IdLookupHash, selected[query.PageSize - 1].Ticks)) : null, total);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowFolder>>> PageBreadcrumbsAsync(string[] folderIds, CancellationToken token)
    {
        if (folderIds.Length == 0) return new Dictionary<string, IReadOnlyList<WorkflowFolder>>(StringComparer.Ordinal);
        var containing = await FolderRows.AsNoTracking().Where(row => folderIds.Contains(row.Id)).ToArrayAsync(token);
        var ids = containing.SelectMany(row => row.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > 1600 || containing.Length != folderIds.Length) throw new InvalidOperationException("Stored folder breadcrumbs exceed their bound or contain a missing folder.");
        var rows = await FolderRows.AsNoTracking().Where(row => ids.Contains(row.Id)).ToArrayAsync(token);
        var byId = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return containing.ToDictionary(row => row.Id, row => (IReadOnlyList<WorkflowFolder>)row.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => byId.TryGetValue(id, out var ancestor) ? Folder(ancestor) : throw new InvalidOperationException("A folder ancestor is unavailable.")).ToArray(), StringComparer.Ordinal);
    }

    private sealed class LibraryProjection
    {
        public string Hash { get; init; } = "";
        public int VersionCount { get; init; }
        public string? LatestVersionId { get; init; }
        public string? LatestVersion { get; init; }
        public string? DraftId { get; init; }
        public string? DraftSourceVersionId { get; init; }
    }
}
