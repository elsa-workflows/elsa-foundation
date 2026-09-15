# Extension points — Activities.Design.Persistence.EntityFrameworkCore

This opt-in provider replaces the Activities Design persistence contracts with one provider-neutral
EF Core relational model. It is selected through the same replacement-contract guard as the
Groundwork implementation; applications must select exactly one Activities Design persistence family.

## Replacement contracts

The registration replaces the following contracts with the indicated implementation family:

| Contract family | Implementation |
|---|---|
| Activity definition, version, authoring, draft, publication, layout, validation, dependency, availability, upgrade, and picker stores | `EfActivityDesignStores` |
| Activity authoring, draft, lifecycle, recommendation, fork, and upgrade commands | `EfActivityDesignStores` |
| `IDesignAtomicWriter` | `EfDesignAtomicWrite` |
| Management projection checkpoint writer | `EfActivityManagementProjectionWriter` |

The marker `ActivityDesignPersistenceReplacementContractAttribute` identifies the exclusive
replacement seam. `IActivityDefinitionLookup` remains a core composition service and resolves the
selected definition store.

## Provider boundary

The production adapter references only provider-neutral EF Core and relational APIs. SQLite, SQL
Server, PostgreSQL, and MySQL engines remain host/test responsibilities. Provider-specific contexts
bind column details without exposing EF entities, `IQueryable`, SQL, or provider types through the
Activities Design contracts.

## Persistence semantics

The model retains exact identity material through deterministic hashes where provider collation could
collapse distinct values, uses optimistic concurrency tokens, and commits operation markers with
their mutations. Reads and list operations use bounded pages with deterministic ordering. JSON
conversion failures cross the adapter boundary as `DesignPersistenceException` serialization failures;
provider failures retain cancellation, concurrency, and domain-conflict contracts.

## Registration

`AddActivitiesDesignEntityFrameworkCore()` validates provider selection and exclusive replacement
ownership before registering the EF contexts, stores, projection writer, and atomic writer. Schema
creation is test-owned in this slice; migrations and a default-composition flip are separate gates.
