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
        if (filter.Name is not null)
            basePredicates.Add(storage.Equal(unit, WorkflowsDesignStorageManifest.DefinitionNameField, filter.Name));
        if (filter.Names is not null)
        {
            if (filter.Names.Count == 0)
                return Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);
            basePredicates.Add(storage.In(unit, WorkflowsDesignStorageManifest.DefinitionNameField, filter.Names.Cast<object?>()));
        }
        if (filter.Description is not null)
            basePredicates.Add(storage.Equal(unit, WorkflowsDesignStorageManifest.DefinitionDescriptionField, filter.Description));

        var acrossScopes = filter.TenantAgnostic == true;
        var rows = new Dictionary<GroundworkDesignRowIdentity, GroundworkDesignEntry>();
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var searchTerm = filter.SearchTerm;
            var probe = storage.Probe(
                unit,
                And(basePredicates),
                [storage.Order(unit, WorkflowsDesignStorageManifest.DefinitionIdField)],
                acrossScopes,
                cancellationToken);

            foreach (var (field, index) in new[]
                    {
                         (WorkflowsDesignStorageManifest.IdField, WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex),
                         (WorkflowsDesignStorageManifest.DefinitionNameField, WorkflowsDesignStorageManifest.DefinitionByNameIndex),
                         (WorkflowsDesignStorageManifest.DefinitionDescriptionField, WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex)
                     })
            {
                var predicates = new List<Predicate>(basePredicates)
                {
                    storage.Contains(unit, field, filter.SearchTerm)
                };
                foreach (var row in storage.Query(
                             unit,
                             And(predicates),
                             SearchOrder(unit, field),
                             index,
                             acrossScopes,
                             cancellationToken))
                {
                    var id = row.Entry.Values.Values.TryGetValue(WorkflowsDesignStorageManifest.IdField, out var value)
                        ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                        : null;
                    if (id is not null)
                        rows[GroundworkDesignStorage.Identity(row)] = row;
                }
            }
        }
        else
        {
            var index = filter.Description is not null
                ? WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex
                : filter.Name is not null || filter.Names is not null
                    ? WorkflowsDesignStorageManifest.DefinitionByNameIndex
                    : filter.Id is not null || filter.Ids is not null
                        ? WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex
                    : WorkflowsDesignStorageManifest.DefinitionByIdIndex;
            foreach (var row in storage.Query(
                         unit,
                         And(basePredicates),
                         SearchOrder(unit, index == WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex
                             ? WorkflowsDesignStorageManifest.DefinitionDescriptionField
                             : index == WorkflowsDesignStorageManifest.DefinitionByNameIndex
                                 ? WorkflowsDesignStorageManifest.DefinitionNameField
                                 : WorkflowsDesignStorageManifest.IdField),
                         index,
                         acrossScopes,
                         cancellationToken))
            {
                var id = Convert.ToString(row.Entry.Values.Values[WorkflowsDesignStorageManifest.IdField], System.Globalization.CultureInfo.InvariantCulture)!;
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
