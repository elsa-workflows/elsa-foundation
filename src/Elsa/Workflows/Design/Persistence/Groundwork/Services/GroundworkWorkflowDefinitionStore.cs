using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Public Groundwork v2 implementation of the workflow-definition read port.</summary>
public sealed class GroundworkWorkflowDefinitionStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null,
    IGroundworkPrivilegedQueryAuditSink? auditSink = null) : IWorkflowDefinitionStore
{
    private readonly GroundworkDesignStorage storage = new(sessions, accessContextAccessor, targetName, auditSink);

    public async Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
        await FindByIdAsync(id, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id);

    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, id);
        return Task.FromResult(entry is null ? null : storage.MapDefinition(entry));
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
        WorkflowDefinitionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind;
        var basePredicates = new List<Predicate>();
        if (filter.Id is not null)
            basePredicates.Add(storage.Equal(unit, WorkflowsDesignStorageManifest.IdField, filter.Id));
        if (filter.Ids is not null)
        {
            if (filter.Ids.Count == 0)
                return Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);
            basePredicates.Add(storage.In(unit, WorkflowsDesignStorageManifest.IdField, filter.Ids.Cast<object?>()));
        }
        if (filter.Names is not null && filter.Names.Count == 0)
            return Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);

        var acrossScopes = filter.TenantAgnostic == true;
        var rows = new Dictionary<GroundworkDesignRowIdentity, GroundworkDesignEntry>();
        // Exact name/description filters retain the normal paged ID route, so their public
        // equality semantics remain complete for catalogs larger than the SearchTerm bound.
        var requiresCandidateScan = !string.IsNullOrWhiteSpace(filter.SearchTerm);
        if (requiresCandidateScan)
        {
            var candidates = filter.Id is not null || filter.Ids is not null
                ? storage.Query(
                    unit,
                    And(basePredicates),
                    SearchOrder(unit, WorkflowsDesignStorageManifest.IdField),
                    WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
                    acrossScopes,
                    cancellationToken)
                : storage.Probe(
                    unit,
                    Predicate.AlwaysTrue.Instance,
                    [storage.Order(unit, WorkflowsDesignStorageManifest.DefinitionIdField)],
                    acrossScopes,
                    cancellationToken);

            foreach (var row in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = storage.MapDefinition(row);
                if (Matches(definition, filter))
                    rows[GroundworkDesignStorage.Identity(row)] = row;
            }
        }
        else
        {
            var index = filter.Id is not null || filter.Ids is not null
                ? WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex
                : WorkflowsDesignStorageManifest.DefinitionByIdIndex;
            foreach (var row in storage.Query(
                         unit,
                         And(basePredicates),
                         SearchOrder(unit, WorkflowsDesignStorageManifest.IdField),
                         index,
                         acrossScopes,
                         cancellationToken))
            {
                var definition = storage.MapDefinition(row);
                if (Matches(definition, filter))
                    rows[GroundworkDesignStorage.Identity(row)] = row;
            }
        }

        return Task.FromResult<IReadOnlyList<WorkflowDefinition>>(
            rows.Values
                .Select(storage.MapDefinition)
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ThenBy(x => x.TenantId, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool Matches(WorkflowDefinition definition, WorkflowDefinitionFilter filter)
    {
        if (filter.Id is not null && !GroundworkDesignStorage.SameDefinitionIdentity(definition.Id, filter.Id))
            return false;
        if (filter.Ids is not null && !filter.Ids.Any(id => GroundworkDesignStorage.SameDefinitionIdentity(definition.Id, id)))
            return false;
        if (filter.Name is not null && !StringComparer.Ordinal.Equals(definition.Name, filter.Name))
            return false;
        if (filter.Names is not null && !filter.Names.Contains(definition.Name, StringComparer.Ordinal))
            return false;
        if (filter.Description is not null && !StringComparer.Ordinal.Equals(definition.Description, filter.Description))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = QuerySearchKeys.Encode(filter.SearchTerm, QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase);
            if (!ContainsIdentity(definition.Id, term) &&
                !ContainsText(definition.Name, term) &&
                !ContainsText(definition.Description, term))
                return false;
        }

        return true;
    }

    private static bool ContainsIdentity(string value, string encodedTerm) =>
        QuerySearchKeys.Encode(value, QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase)
            .Contains(encodedTerm, StringComparison.Ordinal);

    private static bool ContainsText(string? value, string encodedTerm) =>
        value is not null &&
        QuerySearchKeys.Encode(value, QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase)
            .Contains(encodedTerm, StringComparison.Ordinal);

    private static Predicate And(IReadOnlyCollection<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates.Single(),
        _ => new Predicate.And(predicates.ToArray())
    };

    private IReadOnlyList<OrderTerm> SearchOrder(string unit, string field) =>
        field is WorkflowsDesignStorageManifest.IdField or WorkflowsDesignStorageManifest.DefinitionIdField
            ? [storage.Order(unit, WorkflowsDesignStorageManifest.DefinitionIdField)]
            :
            [
                storage.Order(unit, field),
                storage.Order(unit, WorkflowsDesignStorageManifest.DefinitionIdField)
            ];
}
