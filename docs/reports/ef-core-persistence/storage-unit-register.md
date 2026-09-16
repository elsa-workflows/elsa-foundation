# EF Core persistence storage-unit register

Status: complete for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671). Every
one of the 95 rows names the merged PR(s) that landed its EF Core implementation, the PR that flips
its default, and the PR that deletes the Groundwork unit.

This register is the entry-level map for the 95 Groundwork storage units declared on `main` at
`7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8`. A row is not complete until its replacement,
default-flip, and deletion cells name merged PRs and its disposition is backed by current evidence.
Replacement and default-flip cells are filled; #1764 is a literal placeholder that the
Groundwork-removal PR replaces with its own number.
The physical names are inventory facts, not a requirement that the EF model preserve Groundwork's
shape.

## Evidence and dependency keys

| Key | Required timing-independent evidence |
|---|---|
| E-CRUD | Create/read/update/delete, query filtering and paging, optimistic concurrency, tenant isolation, restart, and four-provider parity. |
| E-DESIGN | E-CRUD plus version/draft/layout lifecycle, idempotent operation receipts, bounded search, and projection consistency. |
| E-IAM | E-CRUD plus normalized unique lookups, reservation and mutation-receipt atomicity, relationship integrity, ASP.NET Identity behavior, conflict mapping, sign-in, and restart. |
| E-OBS | Atomic/idempotent append, stable ordering and high-water behavior, exact retention, bounded queries, redaction/failure isolation, disposal, restart, and four-provider parity. |
| E-RUNTIME | Runtime CRUD/query semantics, tenant and collection isolation, checkpoint participation, crash/restart recovery, and four-provider parity. |
| E-CLAIM | E-RUNTIME plus conditional claim/lease, ownership, fencing, compare-and-delete, concurrent callers, retries, deadlock/conflict handling, and SQLite contention. |
| E-PUBLISH | E-DESIGN plus publication authority, receipts, deletion guards, idempotent redrive, partial failure, activation, and cross-module transaction behavior. |
| E-HOST | Provider/artifact pairing, isolated migration history, fresh install, pending-model detection, concurrent migration locking, shell activation/reload/teardown, split-database refusal, and runtime/out-of-process apply. |

Dependencies use `S1` for the MySQL spike (#1675), `S2` for the cross-module transaction spike
(#1674), `S3` for the migration-lifecycle work (#1669 after #1657), and `S4` for the Runtime proving
slice (#1676). All replacement implementations must preserve provider-neutral domain contracts and
must not expose `DbContext`, `IQueryable`, persistence entities, or provider SQL through them.

## Store, repository, command, and read-model implementation families

Each named implementation below is an inventory entry. Names grouped in one row share the stated
owner, blockers, evidence profile, and PR cells; their individual storage semantics are mapped by
the unit rows that follow. Supporting codecs, serializers, record shapes, storage conventions and
registration classes live and die with the same family but are also covered by the production-project
and repository-surface registers.

| Family | Current primary implementations | Unit rows / domain boundary | Owner and blockers | Evidence | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| Shared Groundwork session/transaction | `GroundworkStorageSessionSource`, `GroundworkStorageSessionGate`, `GroundworkStorageTransaction`, `GroundworkRuntimeRowStore` | All units; provider connection/session and atomic unit-of-work boundary | #1674/#1678; S1-S3 | E-HOST plus A01-A20 in the surface register | #1624, #1693, #1724, #1755, #1760 | #1763 | #1764 | Replaced by the EF relational foundation in `Elsa.Persistence.EntityFramework`: `EfDatabaseMigrator`/`EfMigratePolicy`/`EfRelationalProviderBinding` (#1624), `EfRelationalExceptionClassifier` (#1693), `EfRelationalIdentity` (#1724), the four-provider `EfModuleMigrator` (#1755), and `EfSharedTransaction` (#1760) |
| Runtime | `GroundworkV2ActivityExecutionHierarchyStore`, `GroundworkV2ActivityExecutionInspectionStore`, `GroundworkV2ActivityExecutionStateStore`, `GroundworkV2BookmarkStateStore`, `GroundworkV2DurableTimerStateStore`, `GroundworkV2DurableValueStateStore`, `GroundworkV2ExecutableActivityTemplateStore`, `GroundworkV2ExecutionLivenessStateStore`, `GroundworkV2IncidentStateStore`, `GroundworkV2RecurringTriggerScheduleStore`, `GroundworkV2RuntimePostCommitOutboxStore`, `GroundworkV2SchedulerStateStore`, `GroundworkV2WorkflowAlterationStore`, `GroundworkV2WorkflowDispatchStore`, `GroundworkV2WorkflowExecutableSourceReferenceStore`, `GroundworkV2WorkflowExecutableStore`, `GroundworkV2WorkflowExecutionStateStore`, `GroundworkV2WorkflowHoldStateStore`, `GroundworkV2WorkflowSchedulerPoisonStore`, `GroundworkV2WorkflowTestScopeCleanupStore`, `GroundworkV2WorkflowTestScopeStore`, `GroundworkV2WorkflowTriggerBindingStore`, `GroundworkV2WorkflowSchedulerWorkQueue`, `GroundworkV2WorkflowActivationAuthority`, `GroundworkV2RuntimeCheckpointWriter`, `GroundworkV2RuntimeRecoveryScanner`, `GroundworkV2WorkflowRuntimeAttentionQuery` | R01-R29 | #1672/#1676; S1-S4 | E-RUNTIME/E-CLAIM | #1724, #1730, #1741, #1743, #1744, #1745, #1746, #1749, #1758 | #1763 | #1764 | Replaced by EF Core across nine waves; #1758 made EF Runtime a standalone composition with its own shell features |
| Distributed Runtime | `GroundworkExecutionPlacementStore`, `GroundworkExecutionCommandTransport` | D01-D03 | #1672/#1676; S1-S4 | E-CLAIM | #1718 (D01); #1721 (D02-D03) | #1763 | #1764 | Replaced by EF Core |
| Activities Design | `GroundworkV2ActivityDesignStore`, `GroundworkDesignAtomicWrite`, `GroundworkActivityDefinitionStore`, `GroundworkActivityDefinitionVersionStore`, `GroundworkActivityAvailabilitySettingsStore`, `GroundworkActivityUpgradePlanStore`, `GroundworkRecommendedActivityDefinitionPickerStore`, `GroundworkReusableActivityStores`, `GroundworkActivityDefinitionManagementProjectionStore`, `GroundworkActivityManagementProjectionWriter`, `GroundworkActivityManagementProjectionRetention`, `GroundworkActivityDependencyProjection`, `GroundworkAddActivityDefinitionCommand`, `GroundworkAddActivityDefinitionVersionCommand` | A01-A21 | #1677 child; S1-S3 | E-DESIGN/E-PUBLISH | #1742; #1759 (publication commit); #1762 (upgrade/dependency bridge) | #1763 | #1764 | Replaced by EF Core |
| Workflows Design | `GroundworkWorkflowDefinitionStore`, `GroundworkWorkflowDefinitionVersionStore`, `GroundworkWorkflowDefinitionDraftStore`, `GroundworkWorkflowDefinitionDraftDocumentStore`, `GroundworkWorkflowDefinitionVersionLayoutStore`, `GroundworkWorkflowDefinitionListProjectionStore`, `GroundworkAddWorkflowDefinitionCommand`, `GroundworkAddWorkflowDefinitionVersionCommand`, `GroundworkCloneDraftFromVersionCommand`, `GroundworkCreateDraftCommand`, `GroundworkDeleteWorkflowDefinitionPermanentlyCommand`, `GroundworkDiscardDraftCommand`, `GroundworkMaterializeWorkflowDefinitionCommand`, `GroundworkMaterializeWorkflowDefinitionVersionCommand`, `GroundworkPromoteDraftToVersionCommand`, `GroundworkSaveWorkflowDefinitionCommand`, `GroundworkSubmitWorkflowDefinitionCommand`, `GroundworkUpdateDraftCommand`, `GroundworkDesignAtomic` | W01-W05 | #1677 child; S1-S3 | E-DESIGN/E-CLAIM | #1732 | #1763 | #1764 | Replaced by EF Core |
| Publishing | `GroundworkPublishingStore`, `GroundworkPublicationRecordStore`, `GroundworkPublicationPolicyStore`, `GroundworkPublicationProjectionIntentStore`, `GroundworkPublicationSnapshotReviewStore`, `GroundworkActivityPublicationReceiptStore`, `GroundworkActivityDraftTestRunStore`, `GroundworkActivityPublicationCommand`, `GroundworkSourceActivityPublicationCommand`, `GroundworkActivityUpgradePlanStore`, `GroundworkActivityDependencyProjectionRebuildCoordinator` | P01-P06 plus A/R/W participants | #1677 child; S1-S3 | E-PUBLISH | #1748 (P04); #1753 (P02-P03); #1759 (P01, P05, P06); #1762 (upgrade bridge) | #1763 | #1764 | Replaced by EF Core. The EF counterparts of the last two are `EfActivityUpgradePlanStore` and `EfActivityDependencyProjectionRebuildCoordinator`, owned by the `activity-upgrade-mutation` Publishing persistence family and landed by #1762 |
| Dashboard | `GroundworkV2WorkflowPortfolioDataSource`, `GroundworkV2WorkflowRunHealthDataSource` | Reads A/W/R projections; declares no unit | #1677 child; Runtime/Design consistency | E-DESIGN/E-RUNTIME bounded reads and partial availability | #1746 (run health); #1752 (portfolio); #1758 (feature) | #1763 | #1764 | Replaced by EF Core; declares no storage unit of its own |
| Identity and ASP.NET Identity | `GroundworkUserStore`, `GroundworkRoleStore`, `GroundworkApplicationStore`, `GroundworkCredentialStore`, `GroundworkClaimMappingStore`, `GroundworkProviderConfigurationStore`, `GroundworkExternalIdentityStore`, `GroundworkTenantMembershipStore`, `GroundworkIdentityRowStore`, `GroundworkIdentityAtomicMutation`, `GroundworkIdentityAtomicWrite`, `GroundworkIdentityMutationBatch`, `GroundworkIdentityAuthorityAggregateCoordinator`, `GroundworkIdentityAuthorityRelationshipCoordinator`, `GroundworkIdentityUserStore` (user/claim/login/role/token partials), `GroundworkIdentityRoleStore`, `GroundworkIdentityFailureMapper`, `AspNetCoreIdentityAuthorityMapper`, `GroundworkIdentitySessionInvalidator` | I01-I17 | #1682; S1-S3 | E-IAM | #1709, #1711, #1715 | #1763 | #1764 | Replaced by EF Core |
| Diagnostics | `GroundworkOpenTelemetryStore`, `GroundworkStructuredLogStore`, opt-in EF `OpenTelemetry` and `StructuredLogs` adapters | O01-O09 | #1681/#1695/#1697; S1-S3 | E-OBS | #1696 (O09); #1701 (O01-O08) | #1763 | #1764 | Replaced by EF Core. #1763 keeps Diagnostics on its own database file, as it was under Groundwork (#1569) |
| Secrets | `GroundworkSecretRepository` | S01 | #1679; S1/S3 | E-CRUD | #1624, #1633, #1634, #1646, #1729 | #1763 | #1764 | Replaced by EF Core; this family was the pilot that produced the shared EF policy package |
| Studio Preferences | `GroundworkStudioPreferenceStore`, opt-in `EfStudioPreferenceStore` | U01 | #1680; S1/S3 | E-CRUD | #1693 | #1763 | #1764 | Replaced by EF Core |
| Elsa 3 import | `GroundworkReusableActivityImportOperationStore`, `GroundworkReusableActivityImportCommand` | L01-L03 plus A/R participants | #1677 child; S1-S3 | E-PUBLISH | #1760 | n/a — no shipped host composes this feature | #1764 | Replaced by EF Core. `Elsa3ImportActivitiesGroundwork` is named by no shell file and is not auto-enabled by any composed feature, so there was no default to flip in #1763 |

## Runtime: 29 units

Owner: Runtime program #1672, with the hard proving slice #1676. Unless narrowed in the row,
dependencies are S1-S4 and the evidence profile is E-RUNTIME. The implementation mapping is the
corresponding `GroundworkV2*Store` or internal coordination row registered by
`GroundworkV2RuntimeRegistration`; its EF counterpart is the matching `Ef*Store` registered by the
`WorkflowsRuntimeEntityFrameworkCore` shell features (#1758). Provider migrations for every context
landed in #1755.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| R01 | `bookmarkState` / `runtime_bookmark_state` | `IBookmarkStateStore`, `IBookmarkStimulusIndex` | E-RUNTIME | S1-S4 | #1724 | #1763 | #1764 | Replaced by EF Core |
| R02 | `workflowExecutable` / `runtime_workflow_executable` | `IWorkflowExecutableStore`; immutable executable materialization | E-RUNTIME | S1-S4 | #1730 | #1763 | #1764 | Replaced by EF Core |
| R03 | `workflowExecutableCoordination` / `runtime_workflow_executable_coordination` | Executable retention and publication coordination used by R02 | E-CLAIM | S1-S4 | #1730 | #1763 | #1764 | Replaced by EF Core |
| R04 | `executableActivityTemplate` / `runtime_executable_activity_template` | `IExecutableActivityTemplateStore`, reader, writer | E-RUNTIME | S1-S4 | #1730 | #1763 | #1764 | Replaced by EF Core |
| R05 | `executableActivityTemplateHashClaim` / `runtime_executable_activity_template_hash_claim` | Unique template-hash claim used by R04 | E-CLAIM | S1-S4 | #1730 | #1763 | #1764 | Replaced by EF Core. #1759 fixed a read-committed race in the EF template/claim staging found by the native-provider smoke: the pair is re-read after a concurrent identical create |
| R06 | `workflowExecutableSourceReference` / `runtime_workflow_executable_source_reference` | `IWorkflowExecutableSourceReferenceStore`, reader, writer | E-RUNTIME | S1-S4 | #1730 | #1763 | #1764 | Replaced by EF Core |
| R07 | `activityExecutionState` / `runtime_activity_execution_state` | `IActivityExecutionStateStore` | E-RUNTIME | S1-S4 | #1741 | #1763 | #1764 | Replaced by EF Core |
| R08 | `activityExecutionInspection` / `runtime_activity_execution_inspection` | `IActivityExecutionInspectionStore`, writer | E-RUNTIME | S1-S4 | #1741 | #1763 | #1764 | Replaced by EF Core |
| R09 | `activityExecutionHierarchy` / `runtime_activity_execution_hierarchy` | `IActivityExecutionHierarchyStore`, reader, writer | E-RUNTIME | S1-S4 | #1741 | #1763 | #1764 | Replaced by EF Core |
| R10 | `workflowExecutionState` / `runtime_workflow_execution_state` | `IWorkflowExecutionStateStore`; history and provider-side pinned-artifact projections (attention composition remains R18) | E-RUNTIME | S1-S4 | #1743 | #1763 | #1764 | Replaced by EF Core; the attention query composed over this state is R18 |
| R11 | `workflowAlterationPlan` / `runtime_workflow_alteration_plan` | `IWorkflowAlterationStore`; plan lifecycle and idempotency | E-CLAIM | S1-S4 | #1744 | #1763 | #1764 | Replaced by EF Core |
| R12 | `workflowAlterationJob` / `runtime_workflow_alteration_job` | `IWorkflowAlterationStore`; claimable jobs and checkpoint linkage | E-CLAIM | S1-S4 | #1744 | #1763 | #1764 | Replaced by EF Core |
| R13 | `workflowTestScope` / `runtime_workflow_test_scope` | `IWorkflowTestScopeStore`, admission, cleanup | E-CLAIM | S1-S4 | #1744 (lifecycle/admission); #1746 (atomic cleanup) | #1763 | #1764 | Replaced by EF Core in two parts: #1744 landed the lifecycle/admission ledger and #1746 the checkpoint-atomic dispatch/outbox cleanup. E05/E06 end-to-end evidence was not run against this row; see the test-and-e2e register |
| R14 | `durableValueState` / `runtime_durable_value_state` | `IDurableValueStateStore` | E-RUNTIME | S1-S4 | #1745 | #1763 | #1764 | Replaced by EF Core |
| R15 | `schedulerState` / `runtime_scheduler_state` | `ISchedulerStateStore` | E-RUNTIME | S1-S4 | #1745 | #1763 | #1764 | Replaced by EF Core |
| R16 | `operationalState` / `runtime_execution_liveness_state` | `IExecutionLivenessStateStore`; recovery scanner and heartbeat/lease state | E-CLAIM | S1-S4 | #1745 | #1763 | #1764 | Replaced by EF Core |
| R17 | `controlPlaneState` / `runtime_workflow_hold_state` | `IWorkflowHoldStateStore` | E-RUNTIME | S1-S4 | #1745 | #1763 | #1764 | Replaced by EF Core |
| R18 | `incidentState` / `runtime_incident_state` | `IIncidentStateStore`; attention query | E-RUNTIME | S1-S4 | #1745 | #1763 | #1764 | Replaced by EF Core, including the EF-composed attention query over R10; status/tenant projection drift fails closed |
| R19 | `checkpointCommit` / `runtime_checkpoint_commit` | `IRuntimeCheckpointCommitStore`; create-only commit marker | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core. #1763 rekeyed commit rows by hash with the encoded residual checked in code, after a composed fault commit id exceeded the 128-code-unit cap and faulted a dispatched child workflow |
| R20 | `postCommitOutbox` / `runtime_post_commit_outbox` | Runtime outbox store, lookup, claim, completion, and redrive | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core. Two provider-neutral limitations are explicit and unresolved: a prefix-plus-digest order key cannot preserve full Groundwork ordinal order for arbitrary common-prefix identities, and candidate-row projection drift fails closed rather than being detected, since detecting a tampered eligibility value that hides a row would need an unbounded or provider-specific scan |
| R21 | `workflowDispatch` / `runtime_workflow_dispatch` | Dispatch store, query, delete, retention, admission, cancellation | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core, including shared-context outbox dispatch, follow-up projection and redrive |
| R22 | `schedulerWorkItem` / `runtime_scheduler_work_item` | `IWorkflowSchedulerWorkQueue`; claim inspection | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core. #1763 lifted the 128-code-unit identity cap that stopped a poisoned dispatch from being recorded and stalled the recurring-trigger pump; work-item ids now allow the composed length, keyed by a bounded prefix with the identity hash as tie-break |
| R23 | `schedulerPoison` / `runtime_scheduler_poison` | `IWorkflowSchedulerPoisonStore` | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core |
| R24 | `durableTimer` / `runtime_durable_timer` | `IDurableTimerStore` | E-CLAIM | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core. Selected due/workflow/visibility projection drift fails closed; corruption that moves a row outside a relational due/workflow/visibility predicate remains an explicit integrity limitation until a bounded integrity mechanism is designed |
| R25 | `workflowRunHealthState` / `runtime_workflow_run_health_state` | Run-health projection and bounded dashboard queries | E-RUNTIME | S1-S4 | #1746 | #1763 | #1764 | Replaced by EF Core, together with the Dashboard `EfWorkflowRunHealthDataSource` and its checkpoint participant staging |
| R26 | `workflowTriggerBinding` / `runtime_workflow_trigger_binding` | `IWorkflowTriggerBindingStore` | E-CLAIM | S1-S4 | #1749 | #1763 | #1764 | Replaced by EF Core |
| R27 | `recurringTriggerSchedule` / `runtime_recurring_trigger_schedule` | `IRecurringTriggerScheduleStore` | E-CLAIM | S1-S4 | #1749 | #1763 | #1764 | Replaced by EF Core; maximum legal 1,539-UTF-16-unit fan-out identities use provider-safe unindexed projections with hash-backed indexes and residual exact rechecks |
| R28 | `workflowActivationSlot` / `runtime_workflow_activation_slot` | `IWorkflowActivationAuthority`; exclusive activation slot | E-CLAIM | S1-S4 | #1749 | #1763 | #1764 | Replaced by EF Core for owner identity, revision/fencing, competition and stale-owner rejection. Lease/expiry acceptance is not covered because the `IWorkflowActivationAuthority` request/slot contract exposes no lease or expiry fields; that gap stays open under #1739 rather than being closed by inventing a domain API |
| R29 | `publicationProjectionState` / `runtime_publication_projection_state` | Publication projection reconciliation state | E-PUBLISH | S1-S4; #1677 | #1749 | #1763 | #1764 | Replaced by EF Core, but not one-for-one. This unit is Groundwork-internal, shared only by `GroundworkV2WorkflowTriggerBindingStore` and `GroundworkV2RecurringTriggerScheduleStore`; the EF stores carry it as `WorkflowTriggerBindingProjectionStateEntity` and `RecurringTriggerScheduleProjectionStateEntity`, and `RuntimeSharedProjectionStateTransition` withdraws the Groundwork declaration once both R26 and R27 select EF. The Publishing-side projection-intent contract is P03, which this row does not cover |

## Distributed Runtime: 3 units

Owner: Runtime program #1672 and proving slice #1676. Dependencies: S1-S4.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| D01 | `elsa-distributed-execution-placement` / `elsa_distributed_execution_placement` | `IExecutionPlacementStore`; exclusive placement, ownership and fencing | E-CLAIM | S1-S4 | #1718 | #1763 | #1764 | Replaced by EF Core |
| D02 | `elsa-distributed-command-stream-head` / `elsa_distributed_command_stream_head` | Atomic per-execution sequence head used by command transport | E-CLAIM | S1-S4 | #1721 | #1763 | #1764 | Replaced by EF Core |
| D03 | `elsa-distributed-command-transport` / `elsa_distributed_command_transport` | `IExecutionCommandTransport`; ordered enqueue, lease, ack and redelivery | E-CLAIM | S1-S4 | #1721 | #1763 | #1764 | Replaced by EF Core |

## Activities Design: 21 units

Owner: Design/Publishing epic #1677; the Activities Design EF replacement was tracked by issue #1731
and landed as #1742. Dependencies: S1-S3, plus stable Runtime contracts where publication or upgrade
behavior crosses modules. #1742 landed the implementation opt-in; #1763 makes
`ActivitiesDesignEntityFrameworkCore` the composed default in the Workbench, Production and
docker-compose stacks, and #1755 gave the context its four-provider migration lifecycle.

Evidence boundary: focused SQLite behavioral coverage, provider-neutral EF model creation, and live
SQL Server, PostgreSQL, and MySQL smoke. The provider smoke passes 6/6 with no skips, covering
schema-required TenantKey, CRUD/query/transaction/concurrency, concurrent global uniqueness, scoped
hashed identity, and keyset paging across the 256/500 batch boundaries. MySQL's binary collation does
not order case variants like .NET ordinal, so exact cross-provider case-sort parity is not claimed.
Groundwork-to-EF switching withdraws the Activities lane binding and all 21 unit declarations from a
shared Groundwork catalog and restores them if the switch fails.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| A01 | `activityDefinition` / `elsa_activity_definitions` | `IActivityDefinitionStore`; definition authority | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A02 | `activityDefinitionVersion` / `elsa_activity_definition_versions_v2` | `IActivityDefinitionVersionStore`; immutable versions | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A03 | `activityAvailabilitySettings` / `elsa_activity_availability_settings` | `IActivityAvailabilitySettingsStore` | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A04 | `activityDefinitionAuthoringState` / `elsa_activity_definition_authoring` | Authoring lifecycle state used by reusable-activity stores | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A05 | `activityDefinitionDraft` / `elsa_activity_definition_drafts` | Reusable-activity draft store and commands | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A06 | `activityDefinitionDraftLayout` / `elsa_activity_definition_draft_layouts` | Draft layout persistence | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A07 | `activityDraftValidation` / `elsa_activity_draft_validations` | Draft validation state | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A08 | `activityDefinitionVersionPublication` / `elsa_activity_version_publications` | Version publication state | E-PUBLISH | S1-S3; #1677 | #1742; #1759 (ordered publication commit) | #1763 | #1764 | Replaced by EF Core; the reusable-activity publication commands commit in ADR 0066 order via `EfActivityPublicationDesignCommit` |
| A09 | `activityDefinitionVersionLayout` / `elsa_activity_version_layouts` | Published version layout | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A10 | `activityDependencyEdge` / `elsa_activity_dependency_edges` | Dependency edge authority | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A11 | `activityDependencyProjection` / `elsa_activity_dependency_projection` | Dependency projection and reconciliation | E-DESIGN | S1-S3 | #1742; #1762 (rebuild coordinator) | #1763 | #1764 | Replaced by EF Core. Incremental upgrade updates are staged in the upgrade's own transaction, and `EfActivityDependencyProjectionRebuildCoordinator` (#1762) converges the view after an ordinary Design edit under an explicit privileged-across-scopes context, because it replaces one global row holding every tenant's facts |
| A12 | `activityUpgradePlan` / `elsa_activity_upgrade_plans` | `IActivityUpgradePlanStore`; cross-design upgrade staging | E-PUBLISH | S1-S3 | #1742; #1762 (Publishing-side bridge) | #1763 | #1764 | Replaced by EF Core. Its Publishing-side bridge — `IActivityUpgradeDiscoverySource`, `IActivityUpgradePlanMutationStore`, `IActivityUpgradePublishedDraftResolver` — is `EfActivityUpgradePlanStore` (#1762), which commits Activities Design and Workflows Design through one `EfSharedTransaction` |
| A13 | `activityUpgradeApplyReceipt` / `elsa_activity_upgrade_apply_receipts` | Idempotent upgrade-apply receipt | E-PUBLISH | S1-S3 | #1742; #1762 | #1763 | #1764 | Replaced by EF Core. The receipt's Applied transition commits inside the EF upgrade apply, so a rejected apply leaves it Preparing and writes nothing; an unknown commit outcome is not reported as a stale plan, because the durable receipt and its lease remain the authority |
| A14 | `activityForkCandidate` / `elsa_activity_fork_candidates` | Fork candidate staging | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A15 | `activityForkReceipt` / `elsa_activity_fork_receipts` | Idempotent fork receipt | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A16 | `activityDefinitionManagementProjection` / `elsa_activity_management_definitions` | `IActivityDefinitionManagementProjectionStore` | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A17 | `activityDraftManagementProjection` / `elsa_activity_management_drafts` | Draft management projection | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A18 | `activityVersionManagementProjection` / `elsa_activity_management_versions` | Version management projection | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A19 | `activityManagementProjectionWatermark` / `elsa_activity_management_watermarks` | Projection high-water mark | E-CLAIM | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A20 | `activityManagementProjectionSnapshot` / `elsa_activity_management_snapshots` | Temporal projection snapshot | E-DESIGN | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |
| A21 | `activityDesignOperation` / `elsa_activity_design_operations` | Idempotent design-operation ledger and atomic commands | E-CLAIM | S1-S3 | #1742 | #1763 | #1764 | Replaced by EF Core |

## Identity: 17 units

Owner: Identity feature #1682. Dependencies: S1-S3. OpenIddict remains in its separate vendor-owned
context and is not an owner of these rows. #1763 composes `IdentityIamEntityFrameworkCore`,
`IdentityProviderConfigurationEntityFrameworkCore` and
`FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` in place of their Groundwork features.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| I01 | `identityUser` / `identity_users` | `IUserStore`; ASP.NET Identity user adapter | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I02 | `identityRole` / `identity_roles` | `IRoleStore`; ASP.NET Identity role adapter | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I03 | `identityApplication` / `identity_applications` | `IApplicationStore` | E-IAM | S1-S3 | #1711 | #1763 | #1764 | Replaced by EF Core |
| I04 | `identityCredential` / `identity_credentials` | `ICredentialStore` | E-IAM | S1-S3 | #1711 | #1763 | #1764 | Replaced by EF Core |
| I05 | `identityClaimMapping` / `identity_claim_mappings` | `IClaimMappingStore` | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I06 | `identityProviderConfiguration` / `identity_provider_configurations` | `IProviderConfigurationStore`; tenant-scoped provider config | E-IAM | S1-S3 | #1709 | #1763 | #1764 | Replaced by EF Core |
| I07 | `identityGlobalProviderConfiguration` / `identity_global_provider_configurations` | `IProviderConfigurationStore`; global provider config | E-IAM | S1-S3 | #1709 | #1763 | #1764 | Replaced by EF Core |
| I08 | `identityUserClaim` / `identity_user_claims` | User-claim relationship operations | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I09 | `identityRoleClaim` / `identity_role_claims` | Role-claim relationship operations | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I10 | `identityExternalLogin` / `identity_external_logins` | `IExternalIdentityStore`; login relationship operations | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I11 | `identityUserRole` / `identity_user_roles` | User-role relationship operations | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I12 | `identityUserToken` / `identity_user_tokens` | User-token relationship operations | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I13 | `identityTenantMembership` / `identity_tenant_memberships` | `ITenantMembershipStore` | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I14 | `identityUserNameReservation` / `identity_user_name_reservations` | Atomic normalized-user-name reservation | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I15 | `identityEmailReservation` / `identity_email_reservations` | Atomic normalized-email reservation | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I16 | `identityRoleNameReservation` / `identity_role_name_reservations` | Atomic normalized-role-name reservation | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |
| I17 | `identityMutationReceipt` / `identity_mutation_receipts` | Replay-safe mutation receipt and expiry cleanup | E-IAM | S1-S3 | #1715 | #1763 | #1764 | Replaced by EF Core |

## Diagnostics: 9 units

Owner: Diagnostics feature #1681. Dependencies: S1-S3. #1763 composes
`DiagnosticsOpenTelemetryEntityFrameworkCore` and `DiagnosticsStructuredLogsEntityFrameworkCore` in
place of `DiagnosticsGroundworkPersistence`, keeping Diagnostics on its own database file because a
diagnostics drain sharing the runtime connection stalls requests (#1569).

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| O01 | `elsa-otel-traces-v2` / `elsa_otel_traces_v2` | OpenTelemetry trace append and detail query | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O02 | `elsa-otel-spans-v2` / `elsa_otel_spans_v2` | Span append and ordered trace detail | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O03 | `elsa-otel-metric-points-v2` / `elsa_otel_metric_points_v2` | Metric-point append and bounded time query | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O04 | `elsa-otel-logs-v2` / `elsa_otel_logs_v2` | Log append and trace/time query | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O05 | `elsa-otel-resources-v2` / `elsa_otel_resources_v2` | Resource upsert, status and retention | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O06 | `elsa-otel-instruments-v2` / `elsa_otel_instruments_v2` | Instrument upsert and retention | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O07 | `elsa-otel-capture-ledger-v3` / `elsa_otel_capture_ledger_v3` | Idempotent capture-batch ledger | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O08 | `elsa-otel-trace-summaries-v3` / `elsa_otel_trace_summaries_v3` | Optimistically concurrent trace-summary projection | E-OBS | S1-S3; #1697 | #1701 | #1763 | #1764 | Replaced by EF Core |
| O09 | `elsa-structured-logs` / `elsa_structured_logs` | Structured-log store; append, replay/high-water and retention | E-OBS | S1-S3; #1695 | #1696 | #1763 | #1764 | Replaced by EF Core |

## Publishing: 6 units

Owner: Design/Publishing epic #1677. Scoped as one child, issue #1737, and delivered in three waves:
#1748, #1753 (issue #1751) and #1759. Dependencies: S1-S3 and the relevant Runtime/Activities
Design/Workflows Design replacements. #1763 composes `WorkflowsPublishingEntityFrameworkCore` in
place of `WorkflowsPublishingGroundwork`.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| P01 | `publishingPublicationRecord` / `elsa_publication_records` | Publication-record store and slot authority | E-PUBLISH | S1-S3; R/A/W | #1759 | #1763 | #1764 | Replaced by EF Core |
| P02 | `publishingPublicationPolicy` / `elsa_publication_policies` | Publication-policy store | E-PUBLISH | S1-S3; R/A/W | #1753 | #1763 | #1764 | Replaced by EF Core |
| P03 | `publishingProjectionIntent` / `elsa_publication_projection_intents` | Projection intent and reconciliation | E-PUBLISH | S1-S3; R/A/W | #1753 | #1763 | #1764 | Replaced by EF Core. Cross-store consistency between this intent store and the Runtime R26/R27 projection state was left as separate work by #1749 and is not claimed here |
| P04 | `publishingSnapshotReview` / `elsa_publication_snapshot_reviews` | Expiring snapshot-review state | E-PUBLISH | S1-S3; R/A/W | #1748 | #1763 | #1764 | Replaced by EF Core |
| P05 | `publishingActivityPublicationReceipt` / `elsa_activity_publication_receipts` | Idempotent activity-publication receipt | E-PUBLISH | S1-S3; R/A/W | #1759 | #1763 | #1764 | Replaced by EF Core. The reusable-activity publication commands commit in ADR 0066 order with no shared connection or transaction, so a crash before the create-only receipt resumes at the receipt on replay and is reported as an outcome-unknown receipt-pending failure rather than a plain failure |
| P06 | `publishingActivityDraftTestRun` / `elsa_activity_draft_test_runs` | Activity draft test-run receipt and expiry | E-PUBLISH | S1-S3; R/A/W | #1759 | #1763 | #1764 | Replaced by EF Core |

## Workflows Design: 5 units

Owner: Design/Publishing epic #1677; tracked by issue #1727 and landed as #1732. Dependencies:
S1-S3. #1763 composes `WorkflowsDesignEntityFrameworkCore` in place of
`WorkflowsDesignGroundworkPersistence`; #1755 gave the context its four-provider migration lifecycle.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| W01 | `workflowDefinition` / `elsa_workflow_definitions_v2` | `IWorkflowDefinitionStore`; definition read authority | E-DESIGN | S1-S3 | #1732 | #1763 | #1764 | Replaced by EF Core |
| W02 | `workflowDefinitionVersion` / Groundwork v2: `elsa_workflow_definition_versions_v2`; EF: `elsa_workflow_definition_versions` | `IWorkflowDefinitionVersionStore` | E-DESIGN | S1-S3 | #1732 | #1763 | #1764 | Replaced by EF Core under a new physical name; the EF table is `elsa_workflow_definition_versions` |
| W03 | `workflowDefinitionDraft` / Groundwork v2: `elsa_workflow_definition_drafts_v2`; EF: `elsa_workflow_definition_drafts` | `IWorkflowDefinitionDraftStore` and draft commands | E-DESIGN | S1-S3 | #1732; #1762 (upgrade-apply draft commit) | #1763 | #1764 | Replaced by EF Core under a new physical name. The EF draft revision is the persisted `LastModifiedAt` floored to whole microseconds — the coarsest precision the four providers agree on — and the next revision is stamped strictly greater than the observed one rather than read from the clock |
| W04 | `workflowDefinitionVersionLayout` / `elsa_workflow_definition_version_layouts` | `IWorkflowDefinitionVersionLayoutStore` | E-DESIGN | S1-S3 | #1732 | #1763 | #1764 | Replaced by EF Core |
| W05 | `workflowDesignOperation` / `elsa_design_operations` | Idempotent design-operation ledger and atomic commands | E-CLAIM | S1-S3 | #1732 | #1763 | #1764 | Replaced by EF Core |

## Elsa 3 reusable-activity import: 3 units

Owner: Design/Publishing epic #1677; tracked by issue #1738 and landed as #1760. Dependencies: S1-S3
plus the Activities Design and Runtime owners.

No shipped host composes this lane: `Elsa3ImportActivitiesGroundwork` is named by none of
`src/Apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, `shells.Production.json` or
`docker/compose/elsa-workbench.shells.json`, and nothing auto-enables it — its own `DependsOn` points
at `ActivitiesDesignGroundworkPersistence`, not the other way round. There was therefore no default
for #1763 to flip, and these three rows go straight from opt-in EF to deletion.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| L01 | `elsa3ReusableImportCollection` / `elsa3_reusable_import_collections` | `IReusableActivityImportOperationStore`; collection state | E-PUBLISH | S1-S3; A/R | #1760 | n/a — see deletion; no shipped host composes this feature | #1764 | Replaced by EF Core |
| L02 | `elsa3ReusableImportReceipt` / `elsa3_reusable_import_receipts` | Idempotent import receipt | E-PUBLISH | S1-S3; A/R | #1760 | n/a — see deletion; no shipped host composes this feature | #1764 | Replaced by EF Core |
| L03 | `elsa3ReusableImportDefinitionBinding` / `elsa3_reusable_import_definition_bindings` | Imported definition binding used by `IReusableActivityImportCommand` | E-PUBLISH | S1-S3; A/R | #1760 | n/a — see deletion; no shipped host composes this feature | #1764 | Replaced by EF Core; the import commits across Activities Design and Workflows Design through the shared `EfSharedTransaction` owner introduced by #1760 |

## Secrets and Studio Preferences: 2 units

| ID | Groundwork unit / physical name | Domain contract or semantic role | EF owner | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| S01 | `elsa-secrets` / `elsa_secrets` | `ISecretRepository` | #1679 | E-CRUD plus normalization/search, projection reindex and HTTP CRUD | S1, S3 | #1624, #1633, #1634, #1646, #1729 | #1763 | #1764 | Replaced by EF Core. This was the pilot: #1624 landed `EfSecretRepository` and the shared policy package, #1633 dual migrate, #1634 the opt-in composition switch, #1646 the Groundwork call-down, and #1729 the fourth provider (MySQL) |
| U01 | `elsa-studio-preferences` / `elsa_studio_preferences` | `IStudioPreferenceStore` | #1680 | E-CRUD plus scope and last-write semantics | S1, S3 | #1693 | #1763 | #1764 | Replaced by EF Core |

## Count and closure check

The register contains exactly 95 rows: Runtime 29, distributed Runtime 3, Activities Design 21,
Identity 17, OpenTelemetry 8, Structured Logs 1, Publishing 6, Workflows Design 5, Elsa 3 import 3,
Secrets 1, and Studio Preferences 1. Dashboard declares no additional storage unit; it reads Design
and Runtime projections and is tracked separately in the production and test registers.

Every row names a merged replacement PR. By section:

| Section | Replacement PRs |
|---|---|
| Runtime (R01-R29) | #1724 (R01), #1730 (R02-R06), #1741 (R07-R09), #1743 (R10), #1744 (R11-R13), #1745 (R14-R18), #1746 (R13 cleanup, R19-R25), #1749 (R26-R29) |
| Distributed Runtime (D01-D03) | #1718 (D01), #1721 (D02-D03) |
| Activities Design (A01-A21) | #1742, with #1759 for the ordered publication commit and #1762 for the upgrade and dependency-projection bridge |
| Identity (I01-I17) | #1709 (I06-I07), #1711 (I03-I04), #1715 (I01-I02, I05, I08-I17) |
| Diagnostics (O01-O09) | #1701 (O01-O08), #1696 (O09) |
| Publishing (P01-P06) | #1748 (P04), #1753 (P02-P03), #1759 (P01, P05, P06) |
| Workflows Design (W01-W05) | #1732, with #1762 for the upgrade-apply draft commit |
| Elsa 3 import (L01-L03) | #1760 |
| Secrets (S01) | #1624, #1633, #1634, #1646, #1729 |
| Studio Preferences (U01) | #1693 |
| Shared EF foundation (all rows) | #1624, #1693, #1724, #1755, #1760 |

The Default-flip PR column names #1763 for every unit a shipped host composes, because that is the
PR where the Workbench default and baseline shells, the Production overlay, and the docker-compose
reference stack stop selecting Groundwork features and select the EF ones. The three Elsa 3 import
rows are the only exception, and they carry the reason in the cell rather than a PR number. The
Deletion PR column carries the literal placeholder #1764 throughout; the
Groundwork-removal PR substitutes its own number.

At final closure, compare this list to the then-current EF entity/context and contract-test maps.
Every row must either name the merged replacement/default/deletion PRs or record a precise,
owner-authorized disposition. A missing Groundwork string is not evidence that a row's semantics
survived.
