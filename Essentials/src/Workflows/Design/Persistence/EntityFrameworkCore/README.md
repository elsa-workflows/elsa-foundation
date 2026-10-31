# Workflow Design EF persistence

This provider family implements the contracts owned by Workflow Design Persistence Core. The module binds its SQLite, PostgreSQL, SQL Server or MySQL context through the standard EF module composition and migrations.

## Workflow library organization

`IWorkflowLibraryStore` is the optional organization port. Its complete override contract, command/query signatures and capability behavior are documented in the [Design API extension-point catalog](../../Api/EXTENSION_POINTS.md). The user concepts are defined in the [Elsa glossary](../../../../../../docs/glossary/elsa.md); see the [user guide](../../../../../../website/docs/studio/organizing-workflows.md) for interaction behavior.

Folders, assignment rows, saved views and favorites are separate from authored state. The same module transaction/fence admits hierarchy edits, placement and create-in-folder. Its database revision token is the concurrency authority. A configured distributed lock additionally coordinates contention and propagates lease loss; minimal EF hosts without that optional service retain transactional fencing, publication and organization support. Tenant scope comes from the persistence access context; subject identity comes from the API's authenticated session. Physical scope/hash keys, revision checks, operation receipts and committed audit facts protect durable writes. Cascade relationships remove target-owned organization on permanent definition deletion; soft deletion and source reconciliation preserve it.

The advertised bounded page route filters before paging and projects version/draft facts in SQL. It deliberately preserves the separate legacy all-results route. Cursors bind query, tenant and subject. Personal/shared views have bounded versioned query documents. Ordinary tags have no runtime policy meaning.

## Cross-domain contributions

`WorkflowLibraryPublicationObserver` implements Runtime Core's `IWorkflowTriggerIndexObserver`. It handles only authority-refresh notifications that identify the affected logical definition, including triggerless workflows. While holding the organization fence it reads current activation slots through `IWorkflowActivationAuthority` and updates the library's published-membership projection. Delayed notifications therefore read current authority rather than replaying an old Boolean. The projection never changes manual organization revisions, authored state or publication authority. Ordinary pre-authority indexing notifications do not update it.

Observer exceptions retain the existing activation-coordinator failure/recovery contract. Backend ownership tracks this observer's exact additive descriptor: EF reconfiguration removes and rebinds it, and replacing EF with another backend removes it while preserving unrelated observers. A replacement provider supplies its own projection contribution if it advertises published-library queries. The projection is query metadata; serving authority remains Runtime's slot ledger. Runtime-only imported artifacts without a local definition are ignored.

## Schema and qualification

Follow [EF migration tooling](../../../../../../tools/ef/README.md) after model changes. This pre-GA module regenerates Initial migrations for all four providers; existing disposable development databases need a fresh installation. Do not infer a production upgrade path.

Qualification uses existing SQLite durable tests, shared native-provider scenarios, API contracts and normal-host Studio journeys. Deterministic command/result-size budgets and a large library fixture establish bounded query behavior without reviving retired timing benchmarks. The current program's [validation guide](../../../../../../specs/1173-workflow-library/quickstart.md) lists the integrated journeys; executed receipts belong on the delivery PR.
