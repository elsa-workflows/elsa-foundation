# Extension points — Activities.Design.Persistence.EntityFrameworkCore

This opt-in provider replaces the Activities Design persistence contracts with one provider-neutral
EF Core relational model. It is selected through the same replacement-contract guard as the
implementation; applications must select exactly one Activities Design persistence family.

## Replacement contracts

The registration replaces the following contracts with the indicated implementation family:

| Contract family | Implementation |
|---|---|
| Activity definition, version, authoring, draft, publication, layout, validation, dependency, availability, upgrade, and picker stores | `EfActivityDesignStores` |
| Activity authoring, draft, lifecycle, recommendation, fork, upgrade, and in-place version refresh (`IRefreshActivityDefinitionVersionCommand`) commands | `EfActivityDesignStores` |
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
their mutations. Activity definition version `Hash` and `InputsSource` are write-once after insert;
the single sanctioned exception is `IRefreshActivityDefinitionVersionCommand`, which compare-and-swaps
those columns on the expected previous hash, stamps a new `ConcurrencyToken` and `LastModifiedAt`,
and treats a row already at the incoming hash as success. Reads and list operations use bounded pages with deterministic ordering. JSON
conversion failures cross the adapter boundary as `DesignPersistenceException` serialization failures;
provider failures retain cancellation, concurrency, and domain-conflict contracts. `EfDesignAtomicWrite`
accepts a transaction factory and `EfActivityManagementProjectionWriter.WriteInCurrentTransactionAsync`
writes into the caller's transaction, so a cross-module owner such as `EfSharedTransaction` can include
Activities Design writes in its unit. A management projection whose head names an existing but not yet
published version carries the head id without a head reference; a head naming no version
of that definition in that tenant still fails closed.

## Registration

`AddActivitiesDesignEntityFrameworkCore()` validates provider selection and exclusive replacement
ownership before registering the EF contexts, stores, projection writer, and atomic writer. The
`ActivitiesDesignEntityFrameworkCore` feature also registers `AddEfModuleMigrations<ActivitiesDesignDbContext>`,
so the shared `EfModuleMigrator<TContext>` applies or validates the four-provider migrations under
`Migrations/ActivitiesDesign/` in the CShells Prepare phase.

## Exact reusable-publication source snapshots

| Contract | Kind, layer and default | Signature | Replacement guidance and callers |
|---|---|---|---|
| `IActivityPublicationSnapshotReader` | Replacement in Activity Design Persistence.Core; an owned alias of selected `EfActivityDesignStores` | `ValueTask<ActivityPublicationSnapshot?> FindAsync(string versionId, string expectedPartition, ActivityPublicationSnapshotLimits limits, CancellationToken cancellationToken = default)` | Preserve original contract/provider JSON and publication projection fragments in a bounded consistent source snapshot. Copied arrays cannot mutate the snapshot. Future native release catalog acquisition consumes this capability; no capture source is installed here. |

The expected physical partition is independent of the row's nullable logical owner tenant. An observed global
row remains explicitly known global. The profile admits current-schema, uncompressed UTF-8 SQLite, refuses caller-owned
transactions/pending changes, and checks actual stored types/combined JSON byte bounds before payload reads.
Provider failures retain the Design persistence exception and cause. No compiler or typed contract projection runs.
