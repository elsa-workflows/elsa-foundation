# Research: EF payload columns, verified inventory

**Spec**: [spec.md](./spec.md)

**Verified against**: `origin/main` at `79cd3510d`, 2026-09-18.

The inventory first appeared as a comment on [#1805](https://github.com/elsa-workflows/elsa-foundation/issues/1805). Three merges have landed since (`#1841` renamed `BookmarkStateDbContext` to `RuntimeDbContext`, `#1842` renamed the execution-liveness state id, `#1836` rebuilt the activity-authority computed-column SQL), so every reference below has been re-read. **This file supersedes the comment's citations**; the comment's reasoning is unchanged.

---

## The shape that makes this feasible

Every first-party EF module uses one envelope:

- a lossless JSON document in one text column,
- a `SchemaVersion`,
- a `Revision` concurrency token,
- and **a sibling projection column for every attribute a query needs**.

That is why no query anywhere filters on JSON content: the queryable attributes were lifted out into their own columns by construction. Compression is feasible because of that pre-existing decision, not in spite of it.

Independently confirmed by [PR #1836](https://github.com/elsa-workflows/elsa-foundation/pull/1836), whose own sweep of hand-written provider SQL found "no `FromSql`/`ExecuteSql` in shipped code (every hit is test scaffolding), and no provider-specific index filters, `HasDefaultValueSql`, `HasCheckConstraint` or hand-edited `migrationBuilder.Sql` anywhere."

## Tree-wide facts

| Fact | Status |
|---|---|
| `EF.Functions.Json*` anywhere in `src/` | none |
| `FromSql` / `ExecuteSqlRaw` / `ExecuteSqlInterpolated` in `src/` | none |
| Payload column carrying `HasMaxLength` | none |
| Payload column participating in an index | none |
| Payload column in a translated predicate, `ORDER BY` or index | none |
| Database-side SQL reading a payload column | **one family only** (see *Excluded*) |

---

## Tier A — in scope

### Runtime execution state

Module: `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore`. Column types set per provider in `RuntimeProviderContexts.cs:32-70` (`TEXT` / `nvarchar(max)` / `text` / `longtext`), which is the existing enumeration this spec reuses.

| Column | Entity | Configuration |
|---|---|---|
| `WorkflowExecutionStateEntity.ContentJson` | `Entities/WorkflowExecutionStateEntity.cs:25` | `Configuration/WorkflowExecutionStateEntityConfiguration.cs:29` |
| `WorkflowExecutableEntity.ContentJson` | `Entities/RuntimeArtifactEntities.cs:12` | `Configuration/RuntimeArtifactEntityConfigurations.cs:20` |
| `WorkflowExecutableCoordinationEntity.ContentJson` | `Entities/RuntimeArtifactEntities.cs:24` | `Configuration/RuntimeArtifactEntityConfigurations.cs:37` |
| `ExecutableActivityTemplateEntity.ContentJson` | `Entities/RuntimeArtifactEntities.cs:40` | `Configuration/RuntimeArtifactEntityConfigurations.cs:58` |
| `ExecutableActivityTemplateHashClaimEntity.ContentJson` | `Entities/RuntimeArtifactEntities.cs:54` | `Configuration/RuntimeArtifactEntityConfigurations.cs:78` |
| `WorkflowExecutableSourceReferenceEntity.ContentJson` | `Entities/RuntimeArtifactEntities.cs:78` | `Configuration/RuntimeArtifactEntityConfigurations.cs:106` |
| `BookmarkStateEntity.PayloadJson` / `.ContentJson` / `.MetadataJson` | `Entities/BookmarkStateEntity.cs:22-24` | `Configuration/BookmarkStateEntityConfiguration.cs:29-31` |
| `WorkflowDispatchEntity.ContentJson` | `Entities/WorkflowDispatchEntity.cs:33` | `Configuration/WorkflowDispatchEntityConfiguration.cs:36` |
| `WorkflowAlterationPlanEntity.ContentJson`, `.CleanupSafeFailureJson` | `Entities/WorkflowAlterationEntities.cs:22, 19` | `Configuration/WorkflowAlterationEntityConfigurations.cs:24` |
| `WorkflowAlterationJobEntity.ContentJson` | `Entities/WorkflowAlterationEntities.cs:48` | `Configuration/WorkflowAlterationEntityConfigurations.cs:50` |
| `WorkflowTestScopeEntity.ContentJson` | `Entities/WorkflowTestScopeEntity.cs:20` | `Configuration/WorkflowTestScopeEntityConfiguration.cs:24` |
| `WorkflowSchedulerPoisonEntity.ContentJson` | `Entities/WorkflowSchedulerPoisonEntity.cs:17` | `Configuration/WorkflowSchedulerPoisonEntityConfiguration.cs:24` |
| `WorkflowRunHealthStateEntity.ContentJson` | `Entities/WorkflowRunHealthStateEntity.cs:21` | `ConfigureCommon`, `Configuration/RuntimeOperationalStateEntityConfigurations.cs:14` |
| `RuntimePostCommitOutboxEntity.ContentJson` | `Entities/RuntimePostCommitOutboxEntity.cs:22` | `Configuration/RuntimePostCommitOutboxEntityConfiguration.cs:26` |
| `WorkflowActivationSlotEntity.ContentJson` | `Entities/WorkflowActivationSlotEntity.cs:26` | `ConfigureCommon` |
| `WorkflowTriggerBindingEntity.ContentJson` | `Entities/WorkflowTriggerBindingEntities.cs:32` | `Configuration/WorkflowTriggerBindingEntityConfiguration.cs:11` |
| `ExecutionCommandTransportItemEntity.PayloadJson` | `…/Runtime/Distributed/…/Entities/ExecutionCommandTransportItemEntity.cs:21` | `…/Configuration/ExecutionCommandTransportItemEntityConfiguration.cs:28` |

### Activity execution state and inspections

All three share `ActivityExecutionStateEntityConfiguration.ConfigureEnvelope` at `Configuration/ActivityExecutionEntityConfigurations.cs:39`.

| Column | Entity |
|---|---|
| `ActivityExecutionStateEntity.ContentJson` | `Entities/ActivityExecutionEntities.cs:22` |
| `ActivityExecutionInspectionEntity.ContentJson` | `Entities/ActivityExecutionEntities.cs:44` |
| `ActivityExecutionHierarchyEntity.ContentJson` | `Entities/ActivityExecutionEntities.cs:66` |

### Durable values and operational state

All in `Entities/RuntimeOperationalStateEntities.cs`, all configured through `ConfigureCommon` at `Configuration/RuntimeOperationalStateEntityConfigurations.cs:14`:

`DurableValueStateEntity:14`, `SchedulerStateEntity:28`, `DurableTimerEntity:59`, `SchedulerWorkItemEntity:86`, `ExecutionLivenessStateEntity:111`, `WorkflowHoldStateEntity:128`, `IncidentStateEntity:149`, `RuntimeCheckpointCommitEntity:167`, `RecurringTriggerScheduleEntity:200`, `RecurringTriggerScheduleProjectionStateEntity:222`.

### Diagnostics records

| Column | Entity | Column type |
|---|---|---|
| `EfOpenTelemetrySignalEntity.PayloadJson` (traces, spans, metric points, logs) | `…/OpenTelemetry/…/Entities/OpenTelemetryEntities.cs:14` | `EfOpenTelemetryProviderContexts.cs:49` |
| `OpenTelemetryResourceEntity.PayloadJson` | `…/OpenTelemetryEntities.cs:28` | same |
| `OpenTelemetryMetricInstrumentEntity.PayloadJson` | `…/OpenTelemetryEntities.cs:77` | same |
| `OpenTelemetryTraceSummaryEntity.PayloadJson`, `.ServiceMembershipJson`, `.WorkflowMembershipJson` | `…/OpenTelemetryEntities.cs:136-138` | same |
| `StructuredLogRecord.PayloadJson` | `…/StructuredLogs/…/Entities/StructuredLogRecord.cs:16` | `Configuration/StructuredLogRecordConfiguration.cs:24` |
| `StructuredLogAppendOperation.OutcomeJson` | `…/Entities/StructuredLogAppendOperation.cs:12` | idempotency record |

Search is served by the `…SearchKey` projections, which `EfOpenTelemetryProviderContexts.cs:50-58` gives their own column types and `IsUnicode(false)`. Nothing reads `PayloadJson` in SQL.

### Design documents

| Column | Entity | Configuration |
|---|---|---|
| `WorkflowDefinitionVersion.StateSource` | `…/Design/Persistence/Core/Entities/WorkflowDefinitionVersion.cs:53` | `Configuration/DesignEntityConfigurations.cs:88`; column type via `WorkflowsDesignDbContext.ConfigureText`, `WorkflowsDesignDbContext.cs:155` |
| `WorkflowDefinitionDraft.StateSource` | `…/WorkflowDefinitionDraft.cs:47` | `DesignEntityConfigurations.cs:121` |
| `WorkflowDefinitionVersionLayout.RecordsJson` / `.ActivityPresentationJson` | `…/WorkflowDefinitionVersionLayout.cs:27, 29` | `DesignEntityConfigurations.cs:169-170` |
| `WorkflowDefinitionDraftLayout.RecordsJson` / `.ActivityPresentationJson` | `…/WorkflowDefinitionDraftLayout.cs:28, 30` | `DesignEntityConfigurations.cs:144-145` |
| `DesignOperationEntity.ResultJson` | `…/EntityFrameworkCore/Entities/DesignOperationEntity.cs:14` | `DesignEntityConfigurations.cs:195` |
| `ActivityDesignOperationRecord.AuthoritativeResultJson` / `.MutatedUnitsJson` | `ActivitiesDesignDbContext.cs:599-600` | |
| `ActivityUpgradePlanRecord.PlanJson` | `ActivitiesDesignDbContext.cs:607` | |
| `ActivityUpgradeApplyReceiptRecord.ReceiptJson` | `ActivitiesDesignDbContext.cs:616` | |
| `ActivityDefinitionVersion.DescriptorPayloadSource` / `.InputsSource` / `.OutputsSource` / `.DesignFacetsSource` | `…/Activities/Design/Persistence/Core/Entities/ActivityDefinitionVersion.cs:54, 65, 67, 69` | |
| `ActivityForkCandidate.DefinitionMaterialJson` | `…/Entities/ActivityForkEntities.cs:122` | |
| `Elsa3ImportCollectionRecord.ContentJson` / `…ReceiptRecord.ContentJson` | `…/Elsa3/…/Entities/Elsa3ImportRecords.cs:20, 40` | `Elsa3ImportDbContext.cs:55, 72` |
| `ActivityDraftTestRunEntity.Content` | `…/Publishing/…/Entities/ActivityDraftTestRunEntity.cs:17` | `Configuration/ActivityDraftTestRunEntityConfiguration.cs:21` |
| `ActivityPublicationReceiptEntity.Content` | `…/Entities/ActivityPublicationReceiptEntity.cs:15` | `Configuration/ActivityPublicationReceiptEntityConfiguration.cs:19` |

---

## Tier B — registered, left off

Short by construction; a frame is overhead with no upside. Listed so a later reader does not read their absence as an oversight.

`RuntimeCheckpointCommitEntity.PendingPostCommitWorkIdsJson` and `.ConsumedSchedulerWorkItemIdsJson` (`RuntimeOperationalStateEntities.cs:168-169`), `RecurringTriggerScheduleProjectionStateEntity.ScheduleIdsJson` and `.ScheduleFingerprintsJson` (`:220-221`), `StudioPreferenceRecord.ValueJson` (`…/Studio/Preferences/…/Entities/StudioPreferenceRecord.cs:15`), the `Elsa.Foundation.Identity` id-list columns (`Entities/AuthorityEntities.cs:24-30, 56-59, 76-77, 162-163`; `Entities/ApplicationEntity.cs:19-20`; `Entities/ProviderConfigurationEntities.cs:29`), and `WorkflowTriggerBindingProjectionStateEntity.ContentJson` (`WorkflowTriggerBindingEntities.cs:49`), which holds a 64-character hex fingerprint rather than a document.

---

## Excluded

### `ContentAuthority` and its `*Json` siblings — permanent

Entity `ActivityDefinitionManagementProjectionRevision`, property `ContentAuthorityJson` (`…/Activities/Design/Persistence/Core/Entities/ActivityManagementProjectionEntities.cs:87`), mapped to the column `ContentAuthority` at `ActivitiesDesignDbContext.cs:437-438`. Siblings `ContentAuthorityCanonicalJson:93`, `ContentAuthorityAuthorityKeyJson:96`, `ContentAuthoritySourceIdJson:99`.

The column is read by **database-side SQL in all four dialects**, composed since [#1836](https://github.com/elsa-workflows/elsa-foundation/pull/1836) from one shared clause list:

| Provider | Declaration | Stored |
|---|---|---|
| SQLite | `ActivitiesDesignProviderDbContexts.cs:13-14` → `ActivityAuthorityValiditySql.Sqlite()` | no |
| SQL Server | `ActivitiesDesignProviderDbContexts.cs:25-26` → `ActivityAuthorityValiditySql.SqlServer()` | no |
| PostgreSQL | `ActivitiesDesignProviderDbContexts.cs:58-59` → `ActivityAuthorityValiditySql.PostgreSql()` | **yes** |
| MySQL | `ActivitiesDesignProviderDbContexts.cs:70-71` → `ActivityAuthorityValiditySql.MySql()` | no |

A client-side encoding makes `ContentAuthorityIsValid` compute false for every row, and breaks the SQL Server variant's in-database digest comparison against `ContentAuthorityIntegrityHash`. The failure mode is **silent omission, not an error**: `EfActivityDesignStores.ReadDefinitionsAsync` and `FindDefinitionAsync` filter on `ContentAuthorityIsValid` before paging, so every activity definition would simply vanish from every paged read. #1836 documents exactly this failure mode occurring for an unrelated reason on SQLite.

Lifting the exclusion requires removing the computed columns first. #1836 explicitly declined to do that, and gave the reason: the column exists "precisely so the *database* re-derives validity from the stored bytes; computing it in the application would persist the writer's opinion and survive tampering". That is a different decision from this one. See also [#1809](https://github.com/elsa-workflows/elsa-foundation/issues/1809) for the sweep that produced it.

### `SecretRecord.Payload` — pending a security decision

`…/Secrets/Persistence/EntityFrameworkCore/Entities/SecretRecord.cs:19`. Compressing secret material before it is stored is the compress-then-encrypt shape that leaks length structure. Out of scope for a persistence spike; excluded until someone owns that question.

---

## Sites that read the stored string

The implementation checklist. Everything else that looks like a payload hash actually hashes a **re-serialization of the domain model** and is therefore unaffected by any storage encoding: `EfRuntimeCheckpointCommitStore.cs:41`, `EfWorkflowTriggerBindingStore.cs:350`, `EfRecurringTriggerScheduleStore.cs:628-629`, `EfActivityExecutionHierarchyStore.cs:452`, `EfActivityDesignStores.cs:1424`.

| Site | What it does | Failure if it sees a frame |
|---|---|---|
| `Elsa3ImportRecordCodec.cs:45, 91` writes `ContentHash = Hash(json)`; `:195` re-verifies on **every read** | hashes the stored string | fails closed on every read of a framed row |
| `EfDesignSupport.IsResultFingerprintValid` (`EfDesignSupport.cs:178-201`), called from `EfDesignAtomicWriter.cs:382, 410` | `JsonDocument.Parse` on `existing.ResultJson` read off the row | **throws**, rather than mismatching |
| `StructuredLogAppendFingerprint.Compute` (`StructuredLogAppendFingerprint.cs:21`) | folds `PayloadJson` into an append idempotency fingerprint, from the pre-write in-memory value (`EfStructuredLogStore.cs:295`) | a retried append after the flag flips computes a different fingerprint and stops deduplicating |

One in-memory equality also touches a payload column: `EfWorkflowTriggerBindingStore.cs:346` asserts `state.ContentJson == fingerprint` inside `ProjectionMatches`, over materialized entities rather than in SQL.

---

## Collations

Binary and ordinal collations are applied model-wide by six modules (see [#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837) for the divergence between them, which is a separate unit of work). `PublishingSnapshotReviewProviderContexts.cs:91-92` applies one to **every** string property of six publishing entities, `Content` included.

These compare bytes without interpreting them, and the payload columns are unindexed, so nothing depends on their collated ordering. Base64 is ASCII, so a framed value is strictly friendlier to them than plaintext JSON, and cannot carry a lone surrogate, which the `LosslessUtf16StringConverter` in `Stores/RuntimeArtifactJson.cs:31-45` exists to preserve.
