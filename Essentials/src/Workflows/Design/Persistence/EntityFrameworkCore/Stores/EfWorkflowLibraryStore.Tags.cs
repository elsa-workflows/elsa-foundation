using System.Text.Json;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    private static IReadOnlyList<WorkflowTagValue> Values(WorkflowTagEntity row) =>
        JsonSerializer.Deserialize<WorkflowTagValue[]>(row.ValuesJson, Json) ?? throw new InvalidDataException("The stored tag values are invalid.");
    private static WorkflowTag Tag(WorkflowTagEntity row) => new(row.Id, row.Name, row.Description, row.Mode,
        row.Cardinality, row.Deprecated, row.Revision, Values(row));

    public async Task<WorkflowTag?> GetTagAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "tag-");
        var row = await TagRows.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : Tag(row);
    }

    public async Task<WorkflowTagPage> ListTagsAsync(string? search = null, int pageSize = 50,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageSize);
        if (search?.Length > 256) throw new ArgumentException("Tag search cannot exceed 256 characters.");
        var term = string.IsNullOrWhiteSpace(search) ? null : Normalized(search.Trim());
        var binding = Binding("tags", new { term, pageSize }); var cursor = Decode(continuationToken, binding);
        var query = TagRows.AsNoTracking();
        if (term is not null) query = query.Where(row => row.NormalizedName.StartsWith(term));
        if (cursor is not null) query = query.Where(row => row.NormalizedName.CompareTo(cursor.Key) > 0 || row.NormalizedName == cursor.Key && row.Id.CompareTo(cursor.Id) > 0);
        var rows = await query.OrderBy(row => row.NormalizedName).ThenBy(row => row.Id).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var page = rows.Take(pageSize).ToArray();
        return new(page.Select(Tag).ToArray(), rows.Length > pageSize ? Encode(new(binding, page[^1].NormalizedName, page[^1].Id)) : null);
    }

    private static WorkflowTagInput ValidateTag(WorkflowTagInput input, WorkflowTagEntity? previous)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = Text(input.Name, "Tag name");
        if (input.Description?.Length > 2048) throw new ArgumentException("Tag descriptions cannot exceed 2048 characters.");
        if (input.Mode is not ("marker" or "controlled") || input.Cardinality is not ("single" or "multiple"))
            throw new ArgumentException("Tags require marker/controlled mode and single/multiple cardinality.");
        if (input.Mode == "marker" && (input.Cardinality != "single" || input.Values?.Count > 0))
            throw new ArgumentException("Marker tags are single-valued and do not define values.");
        if (previous?.EverAssigned == true && (input.Mode != previous.Mode || input.Cardinality != previous.Cardinality))
            throw new ArgumentException("A tag's value mode and cardinality cannot change after it has been assigned.");
        var supplied = input.Values ?? [];
        if (supplied.Count > 100 || input.Mode == "controlled" && supplied.Count == 0)
            throw new ArgumentException("Controlled tags require between 1 and 100 values.");
        var oldValues = previous is null ? [] : Values(previous);
        var normalized = supplied.Select(value => {
            ArgumentNullException.ThrowIfNull(value);
            var valueName = Text(value.Name, "Tag value");
            var id = string.IsNullOrWhiteSpace(value.Id) ? NewId("value-") : value.Id;
            ValidateStoredId(id, "value-");
            if (previous is null && !string.IsNullOrWhiteSpace(value.Id))
                throw new ArgumentException("New controlled values must omit their identity.");
            if (previous is not null && !oldValues.Any(old => old.Id == id) && !string.IsNullOrWhiteSpace(value.Id))
                throw new ArgumentException("New controlled values must omit their identity.");
            return new WorkflowTagValue(id, valueName, value.Deprecated);
        }).ToArray();
        if (normalized.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != normalized.Length ||
            normalized.Select(value => Normalized(value.Name)).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Controlled tag values require distinct identities and normalized names.");
        if (oldValues.Any(old => !normalized.Any(value => value.Id == old.Id)))
            throw new ArgumentException("Retire controlled values instead of removing their identities.");
        return input with { Name = name, Values = normalized };
    }

    public Task<WorkflowTag> CreateTagAsync(WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default)
    {
        input = ValidateTag(input, null); ValidateActor(actorId); var key = NewId("create-tag-");
        var normalized = Normalized(input.Name); var hash = EfDesignSupport.LookupHash(normalized);
        return WriteAsync(key, "workflow.tag.create.v1", new { input, actorId }, async token =>
        {
            if (await TagRows.AnyAsync(row => row.NameHash == hash, token)) Conflict("A tag with this name already exists.");
            if (await TagRows.CountAsync(token) >= MaximumTags) throw new ArgumentException($"The tag catalog limit is {MaximumTags}.");
            var now = DateTimeOffset.UtcNow;
            var row = new WorkflowTagEntity { Id = NewId("tag-"), TenantId = Tenant, Name = input.Name,
                NormalizedName = normalized, NameHash = hash, Description = input.Description,
                Mode = input.Mode, Cardinality = input.Cardinality, Deprecated = input.Deprecated,
                Revision = Revision(), ValuesJson = JsonSerializer.Serialize(input.Values, Json), CreatedAt = now, LastModifiedAt = now };
            db.Tags.Add(row); var result = Tag(row); Audit(row.Id, actorId, "tag.create", key, null, result); return result;
        }, cancellationToken);
    }

    public Task<WorkflowTag> UpdateTagAsync(string id, WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "tag-"); ValidateActor(actorId); ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Revision); var key = NewId("update-tag-");
        return WriteAsync(key, "workflow.tag.update.v1", new { id, input, actorId }, async token =>
        {
            var row = await TagRows.SingleOrDefaultAsync(candidate => candidate.Id == id, token) ?? Missing<WorkflowTagEntity>(id);
            CheckRevision(input.Revision, row.Revision); var before = Tag(row); var validated = ValidateTag(input, row);
            var normalized = Normalized(validated.Name); var hash = EfDesignSupport.LookupHash(normalized);
            if (await TagRows.AnyAsync(candidate => candidate.Id != id && candidate.NameHash == hash, token)) Conflict("A tag with this name already exists.");
            row.Name = validated.Name; row.NormalizedName = normalized; row.NameHash = hash; row.Description = validated.Description;
            row.Mode = validated.Mode; row.Cardinality = validated.Cardinality; row.Deprecated = validated.Deprecated;
            row.ValuesJson = JsonSerializer.Serialize(validated.Values, Json); row.Revision = Revision(); row.LastModifiedAt = DateTimeOffset.UtcNow;
            var result = Tag(row); Audit(id, actorId, "tag.update", key, before, result); return result;
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<WorkflowTagAssignment>> ValidateAssignmentsAsync(IReadOnlyList<WorkflowTagAssignment> assignments,
        IReadOnlyList<WorkflowTagAssignment> previous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count > MaximumAssignments) throw new ArgumentException($"A workflow supports at most {MaximumAssignments} tag assignments.");
        foreach (var assignment in assignments)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            ValidateStoredId(assignment.TagId, "tag-");
            if (assignment.ValueId is not null) ValidateStoredId(assignment.ValueId, "value-");
        }
        var normalized = assignments.Distinct().OrderBy(assignment => assignment.TagId, StringComparer.Ordinal)
            .ThenBy(assignment => assignment.ValueId, StringComparer.Ordinal).ToArray();
        if (normalized.Length != assignments.Count) throw new ArgumentException("Tag assignments must not be duplicated.");
        var ids = normalized.Select(assignment => assignment.TagId).Distinct(StringComparer.Ordinal).ToArray();
        var tags = ids.Length == 0 ? [] : await TagRows.Where(row => ids.Contains(row.Id)).ToArrayAsync(token);
        if (tags.Length != ids.Length) Missing<WorkflowTag>("selected tag");
        foreach (var tag in tags)
        {
            var selected = normalized.Where(assignment => assignment.TagId == tag.Id).ToArray();
            if (tag.Cardinality == "single" && selected.Length > 1) throw new ArgumentException("A single-valued tag accepts one value per workflow.");
            var values = Values(tag);
            foreach (var assignment in selected)
            {
                var retained = previous.Contains(assignment);
                if (tag.Deprecated && !retained) throw new ArgumentException("Retired tags cannot be newly assigned.");
                if (tag.Mode == "marker" && assignment.ValueId is not null) throw new ArgumentException("Marker tags do not accept values.");
                if (tag.Mode == "controlled")
                {
                    var value = values.SingleOrDefault(candidate => candidate.Id == assignment.ValueId)
                        ?? throw new ArgumentException("Choose an existing controlled tag value.");
                    if (value.Deprecated && !retained) throw new ArgumentException("Retired tag values cannot be newly assigned.");
                }
            }
            tag.EverAssigned = true;
        }
        return normalized;
    }

    public Task<WorkflowOrganization> ReplaceTagsAsync(string definitionId, string revision,
        IReadOnlyList<WorkflowTagAssignment> tags, string actorId, string operationKey, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId); ArgumentException.ThrowIfNullOrWhiteSpace(revision); ArgumentNullException.ThrowIfNull(tags);
        if (tags.Count > MaximumAssignments) throw new ArgumentException($"A workflow supports at most {MaximumAssignments} tags.");
        return WriteAsync(operationKey, "workflow.tags.replace.v1", new { definitionId, revision, tags, actorId },
            token => ReplaceTagsStageAsync(definitionId, revision, tags, actorId, operationKey, token), cancellationToken);
    }

    private async Task<WorkflowOrganization> ReplaceTagsStageAsync(string definitionId, string revision,
        IReadOnlyList<WorkflowTagAssignment> tags, string actorId, string operationKey, CancellationToken token)
    {
        var definition = await DefinitionAsync(definitionId, true, token); var hash = definition.IdLookupHash;
        var row = await OrganizationRows.SingleOrDefaultAsync(candidate => candidate.DefinitionHash == hash, token);
        CheckRevision(revision, row?.Revision ?? "0");
        var oldRows = await AssignmentRows.Where(candidate => candidate.DefinitionHash == hash).OrderBy(candidate => candidate.Id)
            .Take(MaximumAssignments + 1).ToArrayAsync(token);
        if (oldRows.Length > MaximumAssignments) throw new InvalidOperationException("The stored tag set exceeds its limit.");
        var old = oldRows.Select(candidate => new WorkflowTagAssignment(candidate.TagId, candidate.ValueKey.Length == 0 ? null : candidate.ValueKey)).ToArray();
        var next = await ValidateAssignmentsAsync(tags, old, token);
        // Preserve retained rows to avoid a delete/insert race on the same unique key in one EF save.
        foreach (var oldRow in oldRows)
            if (!next.Contains(new WorkflowTagAssignment(oldRow.TagId, oldRow.ValueKey.Length == 0 ? null : oldRow.ValueKey)))
                db.TagAssignments.Remove(oldRow);
        foreach (var assignment in next.Except(old))
            db.TagAssignments.Add(new WorkflowTagAssignmentEntity { Id = NewId("assignment-"), TenantId = Tenant,
                DefinitionHash = hash, TagId = assignment.TagId, ValueKey = assignment.ValueId ?? "",
                CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow });
        row ??= NewOrganization(definition); var before = new { row.FolderId, tags = old, row.Revision };
        row.Revision = Revision(); row.LastModifiedAt = DateTimeOffset.UtcNow;
        // The definition's existing concurrency token makes a concurrent soft-delete compete
        // with this write, rather than allowing tags to land on a target deleted after preflight.
        definition.LastModifiedAt = row.LastModifiedAt;
        var result = new WorkflowOrganization(definition.Id, row.FolderId, await BreadcrumbAsync(row.FolderId, token), next, row.Revision);
        Audit(definition.Id, actorId, "workflow.tags.replace", operationKey, before, result); return result;
    }

    public async Task<IReadOnlyList<WorkflowTagEditOutcome>> EditTagsAsync(WorkflowTagBulkEdit edit, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit); ValidateActor(actorId);
        if (edit.Targets is null || edit.Targets.Count is < 1 or > 100) throw new ArgumentException("Select between 1 and 100 workflows.");
        if (edit.Add is null || edit.Remove is null || edit.Add.Count + edit.Remove.Count > MaximumAssignments)
            throw new ArgumentException("A bulk tag edit supports at most 64 additions/removals.");
        foreach (var target in edit.Targets)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(target.DefinitionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(target.Revision);
        }
        foreach (var assignment in edit.Add.Concat(edit.Remove))
        {
            ArgumentNullException.ThrowIfNull(assignment); ValidateStoredId(assignment.TagId, "tag-");
            if (assignment.ValueId is not null) ValidateStoredId(assignment.ValueId, "value-");
        }
        if (edit.Add.Intersect(edit.Remove).Any()) throw new ArgumentException("A bulk edit cannot add and remove the same tag assignment.");
        if (edit.Targets.Select(target => DefinitionHash(target.DefinitionId)).Distinct(StringComparer.Ordinal).Count() != edit.Targets.Count)
            throw new ArgumentException("A workflow can occur only once in a bulk edit.");
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.OperationKey);
        if (edit.OperationKey.Length > 256) throw new ArgumentException("Operation keys cannot exceed 256 characters.");
        var outcomes = new List<WorkflowTagEditOutcome>();
        foreach (var target in edit.Targets)
        {
            var key = "bulk-tags-" + EfDesignSupport.LookupHash(edit.OperationKey + "\0" + DefinitionHash(target.DefinitionId));
            try
            {
                var result = await WriteAsync(key, "workflow.tags.bulk-target.v1", new { target, edit.Add, edit.Remove, actorId }, async token =>
                {
                    var definition = await DefinitionAsync(target.DefinitionId, true, token);
                    var current = await OrganizationAsync(definition.Id, definition.IdLookupHash, token);
                    var desired = current.Tags.Except(edit.Remove).Union(edit.Add).ToArray();
                    return await ReplaceTagsStageAsync(definition.Id, target.Revision, desired, actorId, edit.OperationKey, token);
                }, cancellationToken);
                outcomes.Add(new(target.DefinitionId, "success", result.Revision));
            }
            catch (WorkflowLibraryConflictException exception) { outcomes.Add(new(target.DefinitionId, "conflict", Message: exception.Message)); }
            catch (EntityNotFoundException) { outcomes.Add(new(target.DefinitionId, "not-found", Message: "The workflow or classification is unavailable.")); }
            catch (ArgumentException exception) { outcomes.Add(new(target.DefinitionId, "invalid", Message: exception.Message)); }
        }
        return outcomes;
    }
}
