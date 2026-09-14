# EF Core persistence storage-unit register

Status: active completion evidence for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671).

This register is the entry-level map for the 95 Groundwork storage units declared on `main` at
`7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8`. A row is not complete until its replacement,
default-flip, and deletion cells name merged PRs and its disposition is backed by current evidence.
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
| Shared Groundwork session/transaction | `GroundworkStorageSessionSource`, `GroundworkStorageSessionGate`, `GroundworkStorageTransaction`, `GroundworkRuntimeRowStore` | All units; provider connection/session and atomic unit-of-work boundary | #1674/#1678; S1-S3 | E-HOST plus A01-A20 in the surface register | | | | Pending |
| Runtime | `GroundworkV2ActivityExecutionHierarchyStore`, `GroundworkV2ActivityExecutionInspectionStore`, `GroundworkV2ActivityExecutionStateStore`, `GroundworkV2BookmarkStateStore`, `GroundworkV2DurableTimerStateStore`, `GroundworkV2DurableValueStateStore`, `GroundworkV2ExecutableActivityTemplateStore`, `GroundworkV2ExecutionLivenessStateStore`, `GroundworkV2IncidentStateStore`, `GroundworkV2RecurringTriggerScheduleStore`, `GroundworkV2RuntimePostCommitOutboxStore`, `GroundworkV2SchedulerStateStore`, `GroundworkV2WorkflowAlterationStore`, `GroundworkV2WorkflowDispatchStore`, `GroundworkV2WorkflowExecutableSourceReferenceStore`, `GroundworkV2WorkflowExecutableStore`, `GroundworkV2WorkflowExecutionStateStore`, `GroundworkV2WorkflowHoldStateStore`, `GroundworkV2WorkflowSchedulerPoisonStore`, `GroundworkV2WorkflowTestScopeCleanupStore`, `GroundworkV2WorkflowTestScopeStore`, `GroundworkV2WorkflowTriggerBindingStore`, `GroundworkV2WorkflowSchedulerWorkQueue`, `GroundworkV2WorkflowActivationAuthority`, `GroundworkV2RuntimeCheckpointWriter`, `GroundworkV2RuntimeRecoveryScanner`, `GroundworkV2WorkflowRuntimeAttentionQuery` | R01-R29 | #1672/#1676; S1-S4 | E-RUNTIME/E-CLAIM | | | | Pending |
| Distributed Runtime | `GroundworkExecutionPlacementStore`, `GroundworkExecutionCommandTransport` | D01-D03 | #1672/#1676; S1-S4 | E-CLAIM | #1718 (D01); #1721 (D02-D03 task #1720) | | | D01-D03 EF implementations are merged; Groundwork remains default and migration/default-flip/deletion evidence remains pending |
| Activities Design | `GroundworkV2ActivityDesignStore`, `GroundworkDesignAtomicWrite`, `GroundworkActivityDefinitionStore`, `GroundworkActivityDefinitionVersionStore`, `GroundworkActivityAvailabilitySettingsStore`, `GroundworkActivityUpgradePlanStore`, `GroundworkRecommendedActivityDefinitionPickerStore`, `GroundworkReusableActivityStores`, `GroundworkActivityDefinitionManagementProjectionStore`, `GroundworkActivityManagementProjectionWriter`, `GroundworkActivityManagementProjectionRetention`, `GroundworkActivityDependencyProjection`, `GroundworkAddActivityDefinitionCommand`, `GroundworkAddActivityDefinitionVersionCommand` | A01-A21 | #1677 child; S1-S3 | E-DESIGN/E-PUBLISH | | | | Pending |
| Workflows Design | `GroundworkWorkflowDefinitionStore`, `GroundworkWorkflowDefinitionVersionStore`, `GroundworkWorkflowDefinitionDraftStore`, `GroundworkWorkflowDefinitionDraftDocumentStore`, `GroundworkWorkflowDefinitionVersionLayoutStore`, `GroundworkWorkflowDefinitionListProjectionStore`, `GroundworkAddWorkflowDefinitionCommand`, `GroundworkAddWorkflowDefinitionVersionCommand`, `GroundworkCloneDraftFromVersionCommand`, `GroundworkCreateDraftCommand`, `GroundworkDeleteWorkflowDefinitionPermanentlyCommand`, `GroundworkDiscardDraftCommand`, `GroundworkMaterializeWorkflowDefinitionCommand`, `GroundworkMaterializeWorkflowDefinitionVersionCommand`, `GroundworkPromoteDraftToVersionCommand`, `GroundworkSaveWorkflowDefinitionCommand`, `GroundworkSubmitWorkflowDefinitionCommand`, `GroundworkUpdateDraftCommand`, `GroundworkDesignAtomic` | W01-W05 | #1677 child; S1-S3 | E-DESIGN/E-CLAIM | #1727 (opt-in EF) | | | EF correction wave in progress; behavior, provider, and lifecycle evidence remain incomplete; Groundwork remains default |
| Publishing | `GroundworkPublishingStore`, `GroundworkPublicationRecordStore`, `GroundworkPublicationPolicyStore`, `GroundworkPublicationProjectionIntentStore`, `GroundworkPublicationSnapshotReviewStore`, `GroundworkActivityPublicationReceiptStore`, `GroundworkActivityDraftTestRunStore`, `GroundworkActivityPublicationCommand`, `GroundworkSourceActivityPublicationCommand`, `GroundworkActivityUpgradePlanStore`, `GroundworkActivityDependencyProjectionRebuildCoordinator` | P01-P06 plus A/R/W participants | #1677 child; S1-S3 | E-PUBLISH | | | | Pending |
| Dashboard | `GroundworkV2WorkflowPortfolioDataSource`, `GroundworkV2WorkflowRunHealthDataSource` | Reads A/W/R projections; declares no unit | #1677 child; Runtime/Design consistency | E-DESIGN/E-RUNTIME bounded reads and partial availability | | | | Pending |
| Identity and ASP.NET Identity | `GroundworkUserStore`, `GroundworkRoleStore`, `GroundworkApplicationStore`, `GroundworkCredentialStore`, `GroundworkClaimMappingStore`, `GroundworkProviderConfigurationStore`, `GroundworkExternalIdentityStore`, `GroundworkTenantMembershipStore`, `GroundworkIdentityRowStore`, `GroundworkIdentityAtomicMutation`, `GroundworkIdentityAtomicWrite`, `GroundworkIdentityMutationBatch`, `GroundworkIdentityAuthorityAggregateCoordinator`, `GroundworkIdentityAuthorityRelationshipCoordinator`, `GroundworkIdentityUserStore` (user/claim/login/role/token partials), `GroundworkIdentityRoleStore`, `GroundworkIdentityFailureMapper`, `AspNetCoreIdentityAuthorityMapper`, `GroundworkIdentitySessionInvalidator` | I01-I17 | #1682; S1-S3 | E-IAM | | | | Pending |
| Diagnostics | `GroundworkOpenTelemetryStore`, `GroundworkStructuredLogStore`, opt-in EF `OpenTelemetry` and `StructuredLogs` adapters | O01-O09 | #1681/#1695/#1697; S1-S3 | E-OBS | #1696 (O09); #1701 (O01-O08) | | | Structured Logs EF is delivered by #1696 and OpenTelemetry EF by #1701; Groundwork remains default and default-flip/deletion gates remain pending |
| Secrets | `GroundworkSecretRepository` | S01 | #1679; S1/S3 | E-CRUD | | | | Pending |
| Studio Preferences | `GroundworkStudioPreferenceStore`, opt-in `EfStudioPreferenceStore` | U01 | #1680; S1/S3 | E-CRUD | #1693 | | | EF implementation merged; Groundwork remains default and migration/default-flip/deletion gates are deferred |
| Elsa 3 import | `GroundworkReusableActivityImportOperationStore`, `GroundworkReusableActivityImportCommand` | L01-L03 plus A/R participants | #1677 child; S1-S3 | E-PUBLISH | | | | Pending |

## Runtime: 29 units

Owner: Runtime program #1672, with the hard proving slice #1676. Unless narrowed in the row,
dependencies are S1-S4 and the evidence profile is E-RUNTIME. The implementation mapping is the
corresponding `GroundworkV2*Store` or internal coordination row registered by
`GroundworkV2RuntimeRegistration`.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| R01 | `bookmarkState` / `runtime_bookmark_state` | `IBookmarkStateStore`, `IBookmarkStimulusIndex` | E-RUNTIME | S1-S4 | #1724 | | | Opt-in EF implementation and focused SQLite proof merged in #1724; exact local native-provider smoke is 6/6 across the Runtime bookmark and artifact suites, including PostgreSQL bookmark proof; migration/default-flip/deletion gates remain pending and Groundwork remains default |
| R02 | `workflowExecutable` / `runtime_workflow_executable` | `IWorkflowExecutableStore`; immutable executable materialization | E-RUNTIME | S1-S4 | #1725 | | | EF parity wave implemented for fresh schemas only; comprehensive SQLite proof and exact local native-provider smoke are green (6/6 across Runtime bookmark and artifact suites, including PostgreSQL bookmark proof); migration lifecycle, default flip and deletion remain pending |
| R03 | `workflowExecutableCoordination` / `runtime_workflow_executable_coordination` | Executable retention and publication coordination used by R02 | E-CLAIM | S1-S4 | #1725 | | | EF parity wave implemented for fresh schemas only; SQLite proves lease/deletion-guard mutual exclusion, fencing, stale-owner rejection, expiry recovery, and restart behavior; native-provider smoke covers the shared Runtime artifact model and representative CRUD/concurrency; migration lifecycle and default flip remain pending |
| R04 | `executableActivityTemplate` / `runtime_executable_activity_template` | `IExecutableActivityTemplateStore`, reader, writer | E-RUNTIME | S1-S4 | #1725 | | | EF parity wave implemented for fresh schemas only; comprehensive SQLite proof and exact local native-provider smoke are green (6/6 across Runtime bookmark and artifact suites); migration lifecycle and default flip remain pending |
| R05 | `executableActivityTemplateHashClaim` / `runtime_executable_activity_template_hash_claim` | Unique template-hash claim used by R04 | E-CLAIM | S1-S4 | #1725 | | | EF parity wave implemented for fresh schemas only; SQLite proves atomic template/claim lifecycle, replay, collision, and successor-safe deletion; native-provider smoke covers the shared Runtime artifact model; migration lifecycle and default flip remain pending |
| R06 | `workflowExecutableSourceReference` / `runtime_workflow_executable_source_reference` | `IWorkflowExecutableSourceReferenceStore`, reader, writer | E-RUNTIME | S1-S4 | #1725 | | | EF parity wave implemented for fresh schemas only; comprehensive SQLite proof and exact local native-provider smoke are green (6/6 across Runtime bookmark and artifact suites); migration lifecycle and default flip remain pending |
| R07 | `activityExecutionState` / `runtime_activity_execution_state` | `IActivityExecutionStateStore` | E-RUNTIME | S1-S4 | #1741 | | | Opt-in EF implementation and focused SQLite/native-provider proof merged in #1741; Groundwork remains default and migration/default-flip/deletion gates remain pending |
| R08 | `activityExecutionInspection` / `runtime_activity_execution_inspection` | `IActivityExecutionInspectionStore`, writer | E-RUNTIME | S1-S4 | #1741 | | | Opt-in EF implementation and focused SQLite/native-provider proof merged in #1741; Groundwork remains default and migration/default-flip/deletion gates remain pending |
| R09 | `activityExecutionHierarchy` / `runtime_activity_execution_hierarchy` | `IActivityExecutionHierarchyStore`, reader, writer | E-RUNTIME | S1-S4 | #1741 | | | Opt-in EF implementation and focused SQLite/native-provider proof merged in #1741; Groundwork remains default and migration/default-flip/deletion gates remain pending |
| R10 | `workflowExecutionState` / `runtime_workflow_execution_state` | `IWorkflowExecutionStateStore`; history and provider-side pinned-artifact projections (attention composition remains #1736/R18) | E-RUNTIME | S1-S4 | #1735 | | | Opt-in EF implementation merged in #1743; Groundwork remains default, attention query/R18 remains owned by #1736, and migration/default-flip/deletion gates remain pending |
| R11 | `workflowAlterationPlan` / `runtime_workflow_alteration_plan` | `IWorkflowAlterationStore`; plan lifecycle and idempotency | E-CLAIM | S1-S4 | #1734 | | | Implemented as opt-in EF plan ledger in #1734; combined focused SQLite behavior suite passes 24/24, including provider-neutral case-sensitive active-plan paging; migration/default-flip/deletion remain pending and Groundwork remains default |
| R12 | `workflowAlterationJob` / `runtime_workflow_alteration_job` | `IWorkflowAlterationStore`; claimable jobs and checkpoint linkage | E-CLAIM | S1-S4 | #1734 | | | Implemented as opt-in EF job ledger in #1734; combined focused SQLite behavior suite passes 24/24 and native PostgreSQL/SQL Server/MySQL smoke passes 3/3 for alteration paging plus representative scope/query, rollback, and optimistic-concurrency behavior; migration/default-flip/deletion remain pending |
| R13 | `workflowTestScope` / `runtime_workflow_test_scope` | `IWorkflowTestScopeStore`, admission, cleanup | E-CLAIM | S1-S4; cleanup depends on #1740 (R20/R21) | #1734 | | | Partial in #1734: opt-in EF lifecycle/admission entities and SQLite proof pass 24/24; native PostgreSQL/SQL Server/MySQL smoke passes 3/3 for lifecycle/query, rollback, and optimistic-concurrency behavior; atomic dispatch/outbox cleanup is deferred to #1740; E05/E06 e2e evidence remains unrun and Groundwork remains default |
| R14 | `durableValueState` / `runtime_durable_value_state` | `IDurableValueStateStore` | E-RUNTIME | S1-S4 | #1736 | | | Opt-in EF implementation in #1736; focused SQLite R14/R15 behavior suite passes 12/12, and native SQL Server/PostgreSQL/MySQL smoke passes 3/3 for stable paging, rollback, restart, and optimistic concurrency; migration/default-flip/deletion remain pending and Groundwork remains default |
| R15 | `schedulerState` / `runtime_scheduler_state` | `ISchedulerStateStore` | E-RUNTIME | S1-S4 | #1736 | | | Opt-in EF implementation in #1736; focused SQLite R14/R15 behavior suite passes 12/12, and native SQL Server/PostgreSQL/MySQL smoke passes 3/3 for scoped CRUD/query, rollback, restart, and optimistic concurrency; migration/default-flip/deletion remain pending and Groundwork remains default |
| R16 | `operationalState` / `runtime_execution_liveness_state` | `IExecutionLivenessStateStore`; recovery scanner and heartbeat/lease state | E-CLAIM | S1-S4 | #1736 | | | Opt-in EF liveness/CAS/recovery implementation is committed locally for #1736; combined Runtime EF SQLite suite passes 213/213, including due-ordered recovery, owner filtering, fencing, and a request-bound continuation regression; native SQL Server/PostgreSQL/MySQL operational smoke passes 3/3. Merge, migration/default-flip/deletion, and R19 checkpoint transaction proof remain pending |
| R17 | `controlPlaneState` / `runtime_workflow_hold_state` | `IWorkflowHoldStateStore` | E-RUNTIME | S1-S4 | #1736 | | | Opt-in EF workflow-hold implementation is committed locally for #1736; combined Runtime EF SQLite suite passes 213/213, including restart and global embedded workflow visibility; native SQL Server/PostgreSQL/MySQL operational smoke passes 3/3. Merge, migration/default-flip/deletion, and R19 checkpoint transaction proof remain pending |
| R18 | `incidentState` / `runtime_incident_state` | `IIncidentStateStore`; attention query | E-RUNTIME | S1-S4 | #1736 | | | Opt-in EF incident store and EF-composed attention query are committed locally for #1736; combined Runtime EF SQLite suite passes 213/213, including create-only insertion, write-once resolution, exact tenant-bound attention totals beyond one provider page, urgency, and fail-closed status/tenant projection drift; native SQL Server/PostgreSQL/MySQL operational smoke passes 3/3. Merge, migration/default-flip/deletion, and R19 checkpoint transaction proof remain pending |
| R19 | `checkpointCommit` / `runtime_checkpoint_commit` | `IRuntimeCheckpointCommitStore`; create-only commit marker | E-CLAIM | S1-S4 | #1740 | | | Thin EF marker slice committed locally on `codex/ef-runtime-r19-r24`: create-only marker, fingerprinted replay/conflict handling, explicit transaction/rollback and ambiguous-ack reconciliation; focused SQLite proof 6/6 and native PostgreSQL/SQL Server/MySQL marker/replay smoke 3/3. The complete checkpoint writer, fencing, and R20-R24 participant staging remain pending; this preview does not replace the runtime contract |
| R20 | `postCommitOutbox` / `runtime_post_commit_outbox` | Runtime outbox store, lookup, claim, completion, and redrive | E-CLAIM | S1-S4 | | | | Thin EF outbox slice committed locally on `codex/ef-runtime-r19-r24`: create-only pending replay/conflict handling, scope isolation, bounded deterministic deliverable queries, claims/reclaims with fencing and stale-owner protection, retry/finalization, plus shared-context atomic dispatch/follow-up completion and failed-final redrive. Focused SQLite proof is 11/11 and native PostgreSQL/SQL Server/MySQL smoke remains 3/3 from the outbox-only slice. Candidate-row projection drift fails closed; a tampered eligibility value that hides a row cannot be detected without an unbounded/provider-specific scan. Equal-time long identities are bounded and restart-stable, but a prefix+digest order key cannot preserve full Groundwork ordinal order for arbitrary common-prefix values; this is an explicit provider-neutral limitation. Complete checkpoint composition remains pending; preview registration does not replace runtime contracts and Groundwork remains default |
| R21 | `workflowDispatch` / `runtime_workflow_dispatch` | Dispatch store, query, delete, retention, admission, cancellation | E-CLAIM | S1-S4 | | | | Local EF dispatch slice on `codex/ef-runtime-r19-r24`: real serialized entity/projections/configuration and concrete adapter prove create-only replay/conflict, scope isolation, bounded ordered query/continuation, normal admission/cancellation CAS, snapshot-fenced terminal deletion, retention roots, tamper rejection, and shared-context outbox dispatch/follow-up projection plus redrive in focused SQLite proof 8/8. PostgreSQL/SQL Server/MySQL model and representative CRUD/query/CAS smoke passes 3/3. Test-scope admission remains fail-closed pending a safe shared participant seam; public dispatch contracts remain unreplaced and Groundwork remains default. Full checkpoint participant composition remains pending |
| R22 | `schedulerWorkItem` / `runtime_scheduler_work_item` | `IWorkflowSchedulerWorkQueue`; claim inspection | E-CLAIM | S1-S4 | | | | Pending |
| R23 | `schedulerPoison` / `runtime_scheduler_poison` | `IWorkflowSchedulerPoisonStore` | E-CLAIM | S1-S4 | | | | Pending |
| R24 | `durableTimer` / `runtime_durable_timer` | `IDurableTimerStore` | E-CLAIM | S1-S4 | #1740 | | | Opt-in EF implementation and focused SQLite proof are committed locally; selected due/workflow/visibility projection drift fails closed, while corruption that moves a row outside a relational due/workflow/visibility predicate remains an explicit integrity limitation until a bounded integrity mechanism is designed; native PostgreSQL/SQL Server/MySQL smoke passes 3/3 for model creation, restart, due ordering/paging, rollback, and fenced claim recovery; checkpoint participation, migration/default-flip/deletion gates remain pending and Groundwork remains default |
| R25 | `workflowRunHealthState` / `runtime_workflow_run_health_state` | Run-health projection and bounded dashboard queries | E-RUNTIME | S1-S4 | | | | Pending |
| R26 | `workflowTriggerBinding` / `runtime_workflow_trigger_binding` | `IWorkflowTriggerBindingStore` | E-CLAIM | S1-S4 | | | | Pending |
| R27 | `recurringTriggerSchedule` / `runtime_recurring_trigger_schedule` | `IRecurringTriggerScheduleStore` | E-CLAIM | S1-S4 | | | | Pending |
| R28 | `workflowActivationSlot` / `runtime_workflow_activation_slot` | `IWorkflowActivationAuthority`; exclusive activation slot | E-CLAIM | S1-S4 | | | | Pending |
| R29 | `publicationProjectionState` / `runtime_publication_projection_state` | Publication projection reconciliation state | E-PUBLISH | S1-S4; #1677 | | | | Pending |

## Distributed Runtime: 3 units

Owner: Runtime program #1672 and proving slice #1676. Dependencies: S1-S4.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| D01 | `elsa-distributed-execution-placement` / `elsa_distributed_execution_placement` | `IExecutionPlacementStore`; exclusive placement, ownership and fencing | E-CLAIM | S1-S4 | #1718 | | | Opt-in EF Core replacement implemented; Groundwork remains default and migration/default-flip/deletion evidence is explicitly pending |
| D02 | `elsa-distributed-command-stream-head` / `elsa_distributed_command_stream_head` | Atomic per-execution sequence head used by command transport | E-CLAIM | S1-S4 | #1721 (task #1720) | | | EF entities/model/store implementation and focused proof merged in #1721; default flip and deletion remain pending |
| D03 | `elsa-distributed-command-transport` / `elsa_distributed_command_transport` | `IExecutionCommandTransport`; ordered enqueue, lease, ack and redelivery | E-CLAIM | S1-S4 | #1721 (task #1720) | | | EF entities/model/store implementation and focused proof merged in #1721; default flip and deletion remain pending |

## Activities Design: 21 units

Owner: Design/Publishing epic #1677; a worker-ready child is required before implementation.
Dependencies: S1-S3, plus stable Runtime contracts where publication or upgrade behavior crosses
modules.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| A01 | `activityDefinition` / `elsa_activity_definitions` | `IActivityDefinitionStore`; definition authority | E-DESIGN | S1-S3 | | | | Pending |
| A02 | `activityDefinitionVersion` / `elsa_activity_definition_versions_v2` | `IActivityDefinitionVersionStore`; immutable versions | E-DESIGN | S1-S3 | | | | Pending |
| A03 | `activityAvailabilitySettings` / `elsa_activity_availability_settings` | `IActivityAvailabilitySettingsStore` | E-DESIGN | S1-S3 | | | | Pending |
| A04 | `activityDefinitionAuthoringState` / `elsa_activity_definition_authoring` | Authoring lifecycle state used by reusable-activity stores | E-DESIGN | S1-S3 | | | | Pending |
| A05 | `activityDefinitionDraft` / `elsa_activity_definition_drafts` | Reusable-activity draft store and commands | E-DESIGN | S1-S3 | | | | Pending |
| A06 | `activityDefinitionDraftLayout` / `elsa_activity_definition_draft_layouts` | Draft layout persistence | E-DESIGN | S1-S3 | | | | Pending |
| A07 | `activityDraftValidation` / `elsa_activity_draft_validations` | Draft validation state | E-DESIGN | S1-S3 | | | | Pending |
| A08 | `activityDefinitionVersionPublication` / `elsa_activity_version_publications` | Version publication state | E-PUBLISH | S1-S3; #1677 | | | | Pending |
| A09 | `activityDefinitionVersionLayout` / `elsa_activity_version_layouts` | Published version layout | E-DESIGN | S1-S3 | | | | Pending |
| A10 | `activityDependencyEdge` / `elsa_activity_dependency_edges` | Dependency edge authority | E-DESIGN | S1-S3 | | | | Pending |
| A11 | `activityDependencyProjection` / `elsa_activity_dependency_projection` | Dependency projection and reconciliation | E-DESIGN | S1-S3 | | | | Pending |
| A12 | `activityUpgradePlan` / `elsa_activity_upgrade_plans` | `IActivityUpgradePlanStore`; cross-design upgrade staging | E-PUBLISH | S1-S3 | | | | Pending |
| A13 | `activityUpgradeApplyReceipt` / `elsa_activity_upgrade_apply_receipts` | Idempotent upgrade-apply receipt | E-PUBLISH | S1-S3 | | | | Pending |
| A14 | `activityForkCandidate` / `elsa_activity_fork_candidates` | Fork candidate staging | E-DESIGN | S1-S3 | | | | Pending |
| A15 | `activityForkReceipt` / `elsa_activity_fork_receipts` | Idempotent fork receipt | E-DESIGN | S1-S3 | | | | Pending |
| A16 | `activityDefinitionManagementProjection` / `elsa_activity_management_definitions` | `IActivityDefinitionManagementProjectionStore` | E-DESIGN | S1-S3 | | | | Pending |
| A17 | `activityDraftManagementProjection` / `elsa_activity_management_drafts` | Draft management projection | E-DESIGN | S1-S3 | | | | Pending |
| A18 | `activityVersionManagementProjection` / `elsa_activity_management_versions` | Version management projection | E-DESIGN | S1-S3 | | | | Pending |
| A19 | `activityManagementProjectionWatermark` / `elsa_activity_management_watermarks` | Projection high-water mark | E-CLAIM | S1-S3 | | | | Pending |
| A20 | `activityManagementProjectionSnapshot` / `elsa_activity_management_snapshots` | Temporal projection snapshot | E-DESIGN | S1-S3 | | | | Pending |
| A21 | `activityDesignOperation` / `elsa_activity_design_operations` | Idempotent design-operation ledger and atomic commands | E-CLAIM | S1-S3 | | | | Pending |

## Identity: 17 units

Owner: Identity feature #1682. Dependencies: S1-S3. OpenIddict remains in its separate vendor-owned
context and is not an owner of these rows.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| I01 | `identityUser` / `identity_users` | `IUserStore`; ASP.NET Identity user adapter | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I02 | `identityRole` / `identity_roles` | `IRoleStore`; ASP.NET Identity role adapter | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I03 | `identityApplication` / `identity_applications` | `IApplicationStore` | E-IAM | S1-S3 | #1711 | | | Opt-in EF implementation merged in #1711; Groundwork remains default and default-flip/deletion gates remain pending |
| I04 | `identityCredential` / `identity_credentials` | `ICredentialStore` | E-IAM | S1-S3 | #1711 | | | Opt-in EF implementation merged in #1711; Groundwork remains default and default-flip/deletion gates remain pending |
| I05 | `identityClaimMapping` / `identity_claim_mappings` | `IClaimMappingStore` | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I06 | `identityProviderConfiguration` / `identity_provider_configurations` | `IProviderConfigurationStore`; tenant-scoped provider config | E-IAM | S1-S3 | #1709 | | | Opt-in EF implementation merged in #1709; Groundwork remains default and default-flip/deletion gates remain pending |
| I07 | `identityGlobalProviderConfiguration` / `identity_global_provider_configurations` | `IProviderConfigurationStore`; global provider config | E-IAM | S1-S3 | #1709 | | | Opt-in EF implementation merged in #1709; Groundwork remains default and default-flip/deletion gates remain pending |
| I08 | `identityUserClaim` / `identity_user_claims` | User-claim relationship operations | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I09 | `identityRoleClaim` / `identity_role_claims` | Role-claim relationship operations | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I10 | `identityExternalLogin` / `identity_external_logins` | `IExternalIdentityStore`; login relationship operations | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I11 | `identityUserRole` / `identity_user_roles` | User-role relationship operations | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I12 | `identityUserToken` / `identity_user_tokens` | User-token relationship operations | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I13 | `identityTenantMembership` / `identity_tenant_memberships` | `ITenantMembershipStore` | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I14 | `identityUserNameReservation` / `identity_user_name_reservations` | Atomic normalized-user-name reservation | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I15 | `identityEmailReservation` / `identity_email_reservations` | Atomic normalized-email reservation | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I16 | `identityRoleNameReservation` / `identity_role_name_reservations` | Atomic normalized-role-name reservation | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |
| I17 | `identityMutationReceipt` / `identity_mutation_receipts` | Replay-safe mutation receipt and expiry cleanup | E-IAM | S1-S3 | #1715 | | | Opt-in EF implementation proposed by #1715; Groundwork remains default and default-flip/deletion gates remain pending |

## Diagnostics: 9 units

Owner: Diagnostics feature #1681. Dependencies: S1-S3.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| O01 | `elsa-otel-traces-v2` / `elsa_otel_traces_v2` | OpenTelemetry trace append and detail query | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O02 | `elsa-otel-spans-v2` / `elsa_otel_spans_v2` | Span append and ordered trace detail | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O03 | `elsa-otel-metric-points-v2` / `elsa_otel_metric_points_v2` | Metric-point append and bounded time query | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O04 | `elsa-otel-logs-v2` / `elsa_otel_logs_v2` | Log append and trace/time query | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O05 | `elsa-otel-resources-v2` / `elsa_otel_resources_v2` | Resource upsert, status and retention | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O06 | `elsa-otel-instruments-v2` / `elsa_otel_instruments_v2` | Instrument upsert and retention | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O07 | `elsa-otel-capture-ledger-v3` / `elsa_otel_capture_ledger_v3` | Idempotent capture-batch ledger | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O08 | `elsa-otel-trace-summaries-v3` / `elsa_otel_trace_summaries_v3` | Optimistically concurrent trace-summary projection | E-OBS | S1-S3; #1697 | #1701 | | | Opt-in EF implementation and SQLite/provider proof delivered by #1701 |
| O09 | `elsa-structured-logs` / `elsa_structured_logs` | Structured-log store; append, replay/high-water and retention | E-OBS | S1-S3; #1695 | #1696 | | | Opt-in EF implementation with SQLite behavioral proof and focused PostgreSQL, SQL Server, and MySQL smoke; Groundwork/default flip/deletion deferred |

## Publishing: 6 units

Owner: Design/Publishing epic #1677; a worker-ready child is required. Dependencies: S1-S3 and the
relevant Runtime/Activities Design/Workflows Design replacements.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| P01 | `publishingPublicationRecord` / `elsa_publication_records` | Publication-record store and slot authority | E-PUBLISH | S1-S3; R/A/W | | | | Pending |
| P02 | `publishingPublicationPolicy` / `elsa_publication_policies` | Publication-policy store | E-PUBLISH | S1-S3; R/A/W | | | | Pending |
| P03 | `publishingProjectionIntent` / `elsa_publication_projection_intents` | Projection intent and reconciliation | E-PUBLISH | S1-S3; R/A/W | | | | Pending |
| P04 | `publishingSnapshotReview` / `elsa_publication_snapshot_reviews` | Expiring snapshot-review state | E-PUBLISH | S1-S3; R/A/W | | | | Pending |
| P05 | `publishingActivityPublicationReceipt` / `elsa_activity_publication_receipts` | Idempotent activity-publication receipt | E-PUBLISH | S1-S3; R/A/W | | | | Pending |
| P06 | `publishingActivityDraftTestRun` / `elsa_activity_draft_test_runs` | Activity draft test-run receipt and expiry | E-PUBLISH | S1-S3; R/A/W | | | | Pending |

## Workflows Design: 5 units

Owner: Design/Publishing epic #1677; a worker-ready child is required. Dependencies: S1-S3.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| W01 | `workflowDefinition` / `elsa_workflow_definitions_v2` | `IWorkflowDefinitionStore`; definition read authority | E-DESIGN | S1-S3 | #1727 (opt-in EF) | | | Correction wave in progress; provider and lifecycle evidence incomplete |
| W02 | `workflowDefinitionVersion` / `elsa_workflow_definition_versions` | `IWorkflowDefinitionVersionStore` | E-DESIGN | S1-S3 | #1727 (opt-in EF) | | | Correction wave in progress; provider and lifecycle evidence incomplete |
| W03 | `workflowDefinitionDraft` / `elsa_workflow_definition_drafts` | `IWorkflowDefinitionDraftStore` and draft commands | E-DESIGN | S1-S3 | #1727 (opt-in EF) | | | Correction wave in progress; provider and lifecycle evidence incomplete |
| W04 | `workflowDefinitionVersionLayout` / `elsa_workflow_definition_version_layouts` | `IWorkflowDefinitionVersionLayoutStore` | E-DESIGN | S1-S3 | #1727 (opt-in EF) | | | Correction wave in progress; provider and lifecycle evidence incomplete |
| W05 | `workflowDesignOperation` / `elsa_design_operations` | Idempotent design-operation ledger and atomic commands | E-CLAIM | S1-S3 | #1727 (opt-in EF) | | | Correction wave in progress; replay/recovery and provider evidence incomplete |

## Elsa 3 reusable-activity import: 3 units

Owner: Design/Publishing epic #1677; a worker-ready child is required. Dependencies: S1-S3 plus the
Activities Design and Runtime owners.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| L01 | `elsa3ReusableImportCollection` / `elsa3_reusable_import_collections` | `IReusableActivityImportOperationStore`; collection state | E-PUBLISH | S1-S3; A/R | | | | Pending |
| L02 | `elsa3ReusableImportReceipt` / `elsa3_reusable_import_receipts` | Idempotent import receipt | E-PUBLISH | S1-S3; A/R | | | | Pending |
| L03 | `elsa3ReusableImportDefinitionBinding` / `elsa3_reusable_import_definition_bindings` | Imported definition binding used by `IReusableActivityImportCommand` | E-PUBLISH | S1-S3; A/R | | | | Pending |

## Secrets and Studio Preferences: 2 units

| ID | Groundwork unit / physical name | Domain contract or semantic role | EF owner | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| S01 | `elsa-secrets` / `elsa_secrets` | `ISecretRepository` | #1679 | E-CRUD plus normalization/search, projection reindex and HTTP CRUD | S1, S3 | | | | Three-provider opt-in EF exists; MySQL/default/deletion pending |
| U01 | `elsa-studio-preferences` / `elsa_studio_preferences` | `IStudioPreferenceStore` | #1680 | E-CRUD plus scope and last-write semantics | S1, S3 | #1693 | | | Opt-in EF implementation merged with SQLite behavioral proof and focused PostgreSQL, SQL Server, and MySQL smoke; migration/default-flip/deletion gates deferred |

## Count and closure check

The register contains exactly 95 rows: Runtime 29, distributed Runtime 3, Activities Design 21,
Identity 17, OpenTelemetry 8, Structured Logs 1, Publishing 6, Workflows Design 5, Elsa 3 import 3,
Secrets 1, and Studio Preferences 1. Dashboard declares no additional storage unit; it reads Design
and Runtime projections and is tracked separately in the production and test registers.

At final closure, compare this list to the then-current EF entity/context and contract-test maps.
Every row must either name the merged replacement/default/deletion PRs or record a precise,
owner-authorized disposition. A missing Groundwork string is not evidence that a row's semantics
survived.
