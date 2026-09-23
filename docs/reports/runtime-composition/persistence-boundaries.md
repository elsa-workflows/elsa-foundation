# Persistence resource and store boundaries

Discovery for [#1965](https://github.com/elsa-workflows/elsa-foundation/issues/1965), under [Runtime Composition & Configuration #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959).

Source baseline: `e738badd1f9ddc2974248079de43d21440ba5afd`, inspected 2026-09-23. This is a bounded discovery report, not an implemented resource configuration contract or a ratification of framework settings classification (§2.12 remains deferred).

## Recommendation

Proceed to configuration-integration discovery #1966. Then specify one shared PostgreSQL resource for the reviewed Runtime, Workflows Design, Activities Design, and Publishing consumers. Preserve each existing context and migration history. A shared resource selects a provider and connection together; it does not merge stores or change their transaction semantics.

Validate overrides against **the context and enabled operation**, not only the feature ID. Multiple Runtime features configure one context. Some operations enlist multiple contexts in one transaction, while reusable-activity publication uses an ordered cross-target sequence. These are different constraints. A blanket rule that every module must share a target would remove supported granularity; a rule that every feature may split freely would admit invalid compositions.

Treat a separate diagnostics resource as the next candidate layout, with real host/database proof required by #1969 before support is claimed. Leave host-owned OpenIddict outside automatic inheritance until its separately owned provider work is reconciled. Keep unknown third-party layouts unresolved in the new planner instead of guessing that module metadata proves transaction compatibility.

## Existing policy and reusable metadata

| Concern | Current evidence | Consequence for the resource layer |
|---|---|---|
| Feature-to-module mapping | `[UsesEfModule]` maps stable feature IDs to canonical EF module names, including read-only consumers and features declared in another assembly. | Reuse this map. A module is not necessarily one feature or one package. A mapping does not prove write ownership or transaction affinity. |
| Module identity | `[EfModule]` and `EfModuleDescriptor` carry base/provider context types, migration-history ownership, connection defaults, migration order, and post-migration actions. | Reuse descriptors rather than maintaining a second first-party context catalog. `DependsOn` is migration order, not a same-database constraint. |
| Connection fallback | Nonblank explicit `ConnectionString`, then explicit `ConnectionName`, then module default name, then SQLite file only. A missing/blank explicitly named connection is refused. | Preserve this exact legacy path when resource mode is absent. New bindings must not silently fall back. |
| Provider availability | Four engine bindings exist: SQLite, SQL Server, PostgreSQL, MySQL. Engines arrive through the host package closure; `Nuplane:Capabilities:ef-provider` selects engine packages for package-hosted hosts. | Engine availability and feature provider selection remain distinct. Resolve the proposed provider before existing agreement/binding checks. |
| Schema | Feature/module schema overrides the shared EF schema key. SQLite ignores schemas; MySQL refuses a nonblank schema and requires the database in the connection string. | Do not promise one identical schema behavior across all providers. Keep schema policy separate from resource identity until #1966 specifies layering. |
| Prepare validation | `EfProviderBindingValidator` validates engine binding without creating a context or opening a database. | This proves package/method availability, not connection validity, database reachability, migration readiness, or layout validity. |
| Activation validation | `EfPendingMigrationActivationGuard` maps selected features to modules. Under `Validate` it probes pending migrations; under `AutoMigrate` it deliberately opens no database. | “Allowed” from this guard is not a universal “ready to run” or physical-layout verdict. Retain separate validation stages. |

Evidence: [module descriptor](../../../src/essentials/Persistence/EntityFramework/EfModuleDescriptor.cs), [feature mapping](../../../src/essentials/Persistence/EntityFramework/UsesEfModuleAttribute.cs), [module catalog](../../../src/essentials/Persistence/EntityFramework/EfModuleCatalog.cs), [binding](../../../src/essentials/Persistence/EntityFramework/EfModuleBinding.cs), [connection defaults](../../../src/essentials/Persistence/EntityFramework/EfConnectionDefaults.cs), [schema policy](../../../src/essentials/Persistence/EntityFramework/EfSchema.cs), [engine binding](../../../src/essentials/Persistence/EntityFramework/EfRelationalProviderBinding.cs), [provider availability validator](../../../src/essentials/Persistence/EntityFramework/EfProviderBindingValidator.cs), and [activation guard](../../../src/essentials/Modularity/EntityFramework/EfPendingMigrationActivationGuard.cs).

## Consumer and layout matrix

The following table describes existing registrations, not new resource-mode support. “Four” means the module declares all four provider-derived contexts. The configuring EF features in this matrix default to `Sqlite`. Standard connection fallback means `ConnectionStrings:Elsa`, then `Data Source=elsa.db` for SQLite only, after explicit feature settings.

| Configuring feature(s) | EF module / context | Ownership and providers | Default / layout constraint |
|---|---|---|---|
| `WorkflowsRuntimeEntityFrameworkCore` and the seven granular participants listed below | `Workflows.Runtime` / `RuntimeDbContext` | Module-owned, shell registration; four | Standard fallback. One context for the aggregate and granular Runtime stores; incompatible participant options are refused. |
| `WorkflowsDesignEntityFrameworkCore` | `Workflows.Design` / `WorkflowsDesignDbContext` | Module-owned, shell registration; four | Standard fallback. Must share an exact provider/connection with Activities Design for activity-upgrade apply. |
| `ActivitiesDesignEntityFrameworkCore` | `Activities.Design` / `ActivitiesDesignDbContext` | Module-owned, shell registration; four | Standard fallback. The activity-upgrade transaction also includes Workflows Design; optional Elsa3 import adds its own transaction participant. |
| `WorkflowsPublishingEntityFrameworkCore` | `Workflows.Publishing` / `PublishingSnapshotReviewDbContext` | Module-owned, shell registration; four | Standard fallback. Publishing records and receipts share this context. Reusable-activity publication does not require it to share a transaction with Runtime or Activities Design. |
| `DiagnosticsStructuredLogsEntityFrameworkCore` | `Diagnostics.StructuredLogs` / `StructuredLogsDbContext` | Module-owned, shell registration; four | Standard fallback. Its append/retention transactions use this context; no cross-module enlistment was identified in the inspected store. |
| `DiagnosticsOpenTelemetryEntityFrameworkCore` | `Diagnostics.OpenTelemetry` / `EfOpenTelemetryDbContext` | Module-owned, shell registration; four | `ConnectionStrings:ElsaOpenTelemetry`, then SQLite `Data Source=elsa-opentelemetry.db`. A capture batch and its replay ledger share one local transaction. |
| `IdentityIamEntityFrameworkCore` and `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` | `Identity.Iam` / `IdentityIamDbContext` | Module-owned, shell registration; four | Standard fallback. Both features configure the same context and must use matching options. This is not two independently bindable stores. |
| `IdentityProviderConfigurationEntityFrameworkCore` | `Identity.ProviderConfiguration` / `IdentityProviderConfigurationDbContext` | Module-owned, shell registration; four | Standard fallback. Separate from the IAM context despite living in the same assembly. Enrollment beyond the boundary example remains later work. |
| `FoundationIdentityOpenIddict` | Workbench vendor `OpenIddictIdentityDbContext`; no corresponding configurable EF module | Host-owned vendor boundary | Development/demo uses in-memory storage; durable Workbench registration uses SQLite. An explicit vendor connection does not select another engine. Keep outside automatic resource inheritance pending #1895. |

The seven granular Runtime participants are `WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence`, `WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence`, `WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence`, `WorkflowsRuntimeAlterationEntityFrameworkCorePersistence`, `WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence`, `WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence`, and `WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence`. Additional registration helpers for timers, scheduler work, and poison storage also participate in this context; their presence does not create independently splittable databases.

`WorkflowsDashboardEntityFrameworkCore` is a reader of both Runtime and Workflows Design, mapped to both modules without its own provider setting. It must inherit the configured contexts, not acquire another persistence resource. Distributed Runtime declares separate placement and command-transport contexts; those are not part of the first enrollment recommendation, and separate declarations alone do not establish a complete distributed deployment proof.

Source declarations: [Runtime](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Workflows Design](../../../src/essentials/Workflows/Design/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Activities Design](../../../src/essentials/Activities/Design/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Publishing](../../../src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Structured Logs](../../../src/essentials/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [OpenTelemetry](../../../src/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Identity](../../../src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [Dashboard](../../../src/essentials/Workflows/Dashboard/Persistence/EntityFrameworkCore/WorkflowsDashboardEntityFrameworkCoreFeature.cs), [distributed contexts](../../../src/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/AssemblyInfo.cs), and [Workbench vendor registration](../../../src/apps/Elsa.Workbench/WorkbenchOpenIddictVendorRegistration.cs).

**Registration compatibility is stricter than physical equality.** Runtime compares normalized provider plus the authored connection choice (inline string, named reference, or default), schema, and pooling before resolving the connection. IAM compares its registration record after provider normalization. Two settings that eventually resolve to the same database can still conflict if expressed differently. #1966 must establish one coherent materialization form for participants using the same resource before these existing checks run; it must not remove the checks. [Runtime compatibility](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeEfContextRegistration.cs), [IAM registration](../../../src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/DependencyInjection/IdentityIamEntityFrameworkCoreRegistration.cs).

## Transaction constraints

`EfSharedTransaction.BeginAsync` requires relational contexts with the same EF provider name and **exactly equal resolved connection strings**. It checks before opening connections, creates fresh contexts, and enlists them in one physical connection and transaction. Differently spelled connection strings are refused even if an operator believes they name the same database. Refusal messages do not echo connection strings. A logical resource alias alone cannot prove this equality.

This helper is a production mechanism; the older transaction-topology spike is a separate test-only demonstration. The existing topology fixture uses its own three borrower contexts and does not demonstrate every production feature combination. [Production helper](../../../src/essentials/Persistence/EntityFramework/EfSharedTransaction.cs), [production-helper tests](../../../src/extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/EfSharedTransactionTests.cs), [historical topology report](../ef-core-persistence/transaction-topology-spike.md).

| Enabled operation | Actual constraint | Evidence |
|---|---|---|
| Activity-upgrade apply | Activities Design and Workflows Design contexts enlist in `EfSharedTransaction`; same resolved provider/connection required. Publishing's receipt context is not in that transaction. | [Upgrade store](../../../src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityUpgradePlanStore.cs), especially line 138; [atomic apply and rollback tests](../../../tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/EfActivityUpgradePlanStoreTests.cs). |
| Reusable-activity publication | Ordered Runtime → Activities Design → Publishing receipt commits, with retry/recovery. Distinct targets are supported by the existing path; no global three-context affinity should be invented. | [Command](../../../src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityPublicationCommand.cs), [separate-database crash/replay tests](../../../tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/EfActivityPublicationCommandTests.cs), and [ADR 0066](../../adr/0066-reusable-activity-publication-orders-writes-instead-of-one-transaction.md). |
| Source-owned activity publication | Ordered Runtime → Activities Design, without a Publishing receipt phase. | [Source command](../../../src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfSourceActivityPublicationCommand.cs). |
| Publication activation | Publishing journal plus Runtime activation orchestration/compensation; not a shared Publishing/Runtime EF transaction. | [Activator](../../../src/essentials/Workflows/Publishing/Services/PublicationActivator.cs). |
| Optional Elsa3 import | Import, Activities Design, and Workflows Design contexts share the production transaction helper. This optional feature requires its own enrollment constraints; exclude it from the first supported resource slice unless explicitly validated. | [Production-helper tests using real import/design contexts](../../../src/extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/EfSharedTransactionTests.cs). |
| Diagnostics capture/retention | Each inspected diagnostic store owns its local transactions. Separate resource assignment is a plausible next layout, not yet a demonstrated composed-host guarantee. | [Structured log store](../../../src/essentials/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Stores/EfStructuredLogStore.cs), [OpenTelemetry store](../../../src/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Stores/EfOpenTelemetryStore.cs). |

The recommendation to begin with one shared primary target is an initial delivery sequence, not a permanent restriction against the split publication path already supported by the engine. The source/tests take precedence over the broader same-transaction wording still present in Publishing's extension-point prose; reconcile that documentation when refining #1967 instead of changing production semantics to match it.

## Proposed EF validation boundary

The next specification should consume existing module/feature metadata and add only the missing layout evidence. Required inputs are:

1. Enabled feature identities and their module usages, with the source configuration that supplied each setting.
2. Each configuring participant's effective provider, resolved connection, schema, pooling, and context ownership; distinguish readers from configuring participants.
3. Enabled operations' required transaction participant sets and whether they support an ordered cross-target path.
4. The selected shell and host/package closure, engine availability, and the relevant migration/action obligations.

Keep resolved secret-bearing connection values inside the trusted resolver/validator. Explanations should identify resource names, consumers, provenance, and constraints without emitting credentials or driver exception messages. Resource equality must not erase tenant isolation or imply that contexts are merged.

Validation should distinguish: an unresolved plan; a known invalid binding/layout; a configuration-valid candidate whose live prerequisites remain unchecked; and the supported layout after its required host/database evidence. Do not invent permanent wire enums or new public interfaces in this spike. The integration spike must locate the real extension seam before those contracts are specified.

## Existing issue ownership and decisions still needed

| Item | Disposition / next owner |
|---|---|
| #1902 connection-guard gaps | Still open. Reuse its scope and legacy cases; this report does not duplicate or fix its scanner/provider-parser/shared-context coverage. |
| #1895 OpenIddict provider module | Still open. Keep the host/vendor exception explicit; do not implicitly convert it while adding defaults. |
| #1900 Secrets migration policy | Open issue, historical body: current code uses shared `EfMigrateOptions` and rejects the retired per-feature `MigratePolicy`. Do not recreate the old defect or preserve the obsolete workaround as accepted configuration. The issue's closure remains with its owner. |
| Resource precedence, explicit-value presence, environment/CLI layering, effective runtime/tooling path | #1966 owns discovery; implementation remains blocked. |
| Exact enrolled feature list, configuration schema, material unknowns | #1967 must reconcile both spikes and review the supported matrix before unblocking #1968. |
| Real shared-resource workflow and migration proof | #1968: rebuilt host, PostgreSQL, design/publish/execute/restart and tooling agreement. Existing helper tests do not prove this new mode. |
| Diagnostics isolation | #1969: prove both diagnostic consumers write to the secondary target while other enrolled stores retain the primary target, with restart/tooling and rejection cases. |
| Arbitrary custom layouts and other store enrollment | Deferred until a named consumer and owner establish its constraints. Preserve the legacy path; do not declare new-mode readiness from absent metadata. |

Secrets evidence: [feature refusal of the retired setting](../../../src/essentials/Secrets/Persistence/EntityFrameworkCore/SecretsEntityFrameworkCoreFeature.cs), [shared migration registration](../../../src/essentials/Secrets/Persistence/EntityFrameworkCore/DependencyInjection/SecretsEntityFrameworkCoreRegistration.cs), and [migration-policy tests](../../../tests/essentials/Secrets/Persistence/EntityFrameworkCore/Tests/SecretsEfModuleMigrationTests.cs). These tests were inspected, not run in this spike. The stale issue body is not evidence that its old behavior still exists.

## Verification and confidence

Executed on the recorded baseline:

```text
dotnet test tests/essentials/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj --filter 'FullyQualifiedName~EfConnectionDefaultsTests|FullyQualifiedName~EfProviderGuardTests|FullyQualifiedName~EfSharedTransactionTests|FullyQualifiedName~EfSchemaTests' --logger 'console;verbosity=minimal'
```

Result: **38 passed, 0 failed, 0 skipped**. This project contains the connection-default, provider-guard, and schema tests; its `EfSharedTransactionTests` filter term matches no class. The production shared-transaction tests live in the Elsa3 import project and were inspected, not executed in this command. No transaction, diagnostics-split, complete-host, new resource-mode, or four-provider runtime claim is derived from these 38 tests.

The baseline's hosted [transaction-topology job](https://github.com/elsa-workflows/elsa-foundation/actions/runs/35837914170/job/107105719612) reports success. Its workflow points to the test-only topology project; it is supporting evidence for the existing topology mechanics, not acceptance of the proposed runtime-builder composition.

The baseline's [Publishing job](https://github.com/elsa-workflows/elsa-foundation/actions/runs/35837914170/job/107105719497) also reports success. The [workflow](../../../.github/workflows/ci.yml) runs the full Publishing EF test project with required native providers enabled. Inspected tests cover atomic upgrade rollback and separate-database publication crash/replay. This supports preserving those existing semantics; it does not validate named-resource resolution or a new host composition.

No production source, database, generated map, or constitution was changed by this investigation. Source links and document structure are checked separately before publication. The supported-layout recommendation remains subject to the integration and specification gates above.

Independent review: Sol 5.6 High reviewed the report against the recorded source and #1965 acceptance criteria on 2026-09-23. No blocking findings remained. The root reviewed the delegated evidence and verified source links and the claimed test scope before publication.
