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
| Distributed Runtime | `GroundworkExecutionPlacementStore`, `GroundworkExecutionCommandTransport` | D01-D03 | #1672/#1676; S1-S4 | E-CLAIM | | | | Pending |
| Activities Design | `GroundworkV2ActivityDesignStore`, `GroundworkDesignAtomicWrite`, `GroundworkActivityDefinitionStore`, `GroundworkActivityDefinitionVersionStore`, `GroundworkActivityAvailabilitySettingsStore`, `GroundworkActivityUpgradePlanStore`, `GroundworkRecommendedActivityDefinitionPickerStore`, `GroundworkReusableActivityStores`, `GroundworkActivityDefinitionManagementProjectionStore`, `GroundworkActivityManagementProjectionWriter`, `GroundworkActivityManagementProjectionRetention`, `GroundworkActivityDependencyProjection`, `GroundworkAddActivityDefinitionCommand`, `GroundworkAddActivityDefinitionVersionCommand` | A01-A21 | #1677 child; S1-S3 | E-DESIGN/E-PUBLISH | | | | Pending |
| Workflows Design | `GroundworkWorkflowDefinitionStore`, `GroundworkWorkflowDefinitionVersionStore`, `GroundworkWorkflowDefinitionDraftStore`, `GroundworkWorkflowDefinitionDraftDocumentStore`, `GroundworkWorkflowDefinitionVersionLayoutStore`, `GroundworkWorkflowDefinitionListProjectionStore`, `GroundworkAddWorkflowDefinitionCommand`, `GroundworkAddWorkflowDefinitionVersionCommand`, `GroundworkCloneDraftFromVersionCommand`, `GroundworkCreateDraftCommand`, `GroundworkDeleteWorkflowDefinitionPermanentlyCommand`, `GroundworkDiscardDraftCommand`, `GroundworkMaterializeWorkflowDefinitionCommand`, `GroundworkMaterializeWorkflowDefinitionVersionCommand`, `GroundworkPromoteDraftToVersionCommand`, `GroundworkSaveWorkflowDefinitionCommand`, `GroundworkSubmitWorkflowDefinitionCommand`, `GroundworkUpdateDraftCommand`, `GroundworkDesignAtomic` | W01-W05 | #1677 child; S1-S3 | E-DESIGN/E-CLAIM | | | | Pending |
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
| R01 | `bookmarkState` / `runtime_bookmark_state` | `IBookmarkStateStore`, `IBookmarkStimulusIndex` | E-RUNTIME | S1-S4 | | | | Pending |
| R02 | `workflowExecutable` / `runtime_workflow_executable` | `IWorkflowExecutableStore`; immutable executable materialization | E-RUNTIME | S1-S4 | | | | Pending |
| R03 | `workflowExecutableCoordination` / `runtime_workflow_executable_coordination` | Executable retention and publication coordination used by R02 | E-CLAIM | S1-S4 | | | | Pending |
| R04 | `executableActivityTemplate` / `runtime_executable_activity_template` | `IExecutableActivityTemplateStore`, reader, writer | E-RUNTIME | S1-S4 | | | | Pending |
| R05 | `executableActivityTemplateHashClaim` / `runtime_executable_activity_template_hash_claim` | Unique template-hash claim used by R04 | E-CLAIM | S1-S4 | | | | Pending |
| R06 | `workflowExecutableSourceReference` / `runtime_workflow_executable_source_reference` | `IWorkflowExecutableSourceReferenceStore`, reader, writer | E-RUNTIME | S1-S4 | | | | Pending |
| R07 | `activityExecutionState` / `runtime_activity_execution_state` | `IActivityExecutionStateStore` | E-RUNTIME | S1-S4 | | | | Pending |
| R08 | `activityExecutionInspection` / `runtime_activity_execution_inspection` | `IActivityExecutionInspectionStore`, writer | E-RUNTIME | S1-S4 | | | | Pending |
| R09 | `activityExecutionHierarchy` / `runtime_activity_execution_hierarchy` | `IActivityExecutionHierarchyStore`, reader, writer | E-RUNTIME | S1-S4 | | | | Pending |
| R10 | `workflowExecutionState` / `runtime_workflow_execution_state` | `IWorkflowExecutionStateStore`; history and attention reads | E-RUNTIME | S1-S4 | | | | Pending |
| R11 | `workflowAlterationPlan` / `runtime_workflow_alteration_plan` | `IWorkflowAlterationStore`; plan lifecycle and idempotency | E-CLAIM | S1-S4 | | | | Pending |
| R12 | `workflowAlterationJob` / `runtime_workflow_alteration_job` | `IWorkflowAlterationStore`; claimable jobs and checkpoint linkage | E-CLAIM | S1-S4 | | | | Pending |
| R13 | `workflowTestScope` / `runtime_workflow_test_scope` | `IWorkflowTestScopeStore`, admission, cleanup | E-CLAIM | S1-S4 | | | | Pending |
| R14 | `durableValueState` / `runtime_durable_value_state` | `IDurableValueStateStore` | E-RUNTIME | S1-S4 | | | | Pending |
| R15 | `schedulerState` / `runtime_scheduler_state` | `ISchedulerStateStore` | E-RUNTIME | S1-S4 | | | | Pending |
| R16 | `operationalState` / `runtime_execution_liveness_state` | `IExecutionLivenessStateStore`; recovery scanner and heartbeat/lease state | E-CLAIM | S1-S4 | | | | Pending |
| R17 | `controlPlaneState` / `runtime_workflow_hold_state` | `IWorkflowHoldStateStore` | E-RUNTIME | S1-S4 | | | | Pending |
| R18 | `incidentState` / `runtime_incident_state` | `IIncidentStateStore`; attention query | E-RUNTIME | S1-S4 | | | | Pending |
| R19 | `checkpointCommit` / `runtime_checkpoint_commit` | `IRuntimeCheckpointCommitStore`; create-only commit marker | E-CLAIM | S1-S4 | | | | Pending |
| R20 | `postCommitOutbox` / `runtime_post_commit_outbox` | Runtime outbox store, lookup, claim, completion, and redrive | E-CLAIM | S1-S4 | | | | Pending |
| R21 | `workflowDispatch` / `runtime_workflow_dispatch` | Dispatch store, query, delete, retention, admission, cancellation | E-CLAIM | S1-S4 | | | | Pending |
| R22 | `schedulerWorkItem` / `runtime_scheduler_work_item` | `IWorkflowSchedulerWorkQueue`; claim inspection | E-CLAIM | S1-S4 | | | | Pending |
| R23 | `schedulerPoison` / `runtime_scheduler_poison` | `IWorkflowSchedulerPoisonStore` | E-CLAIM | S1-S4 | | | | Pending |
| R24 | `durableTimer` / `runtime_durable_timer` | `IDurableTimerStore` | E-CLAIM | S1-S4 | | | | Pending |
| R25 | `workflowRunHealthState` / `runtime_workflow_run_health_state` | Run-health projection and bounded dashboard queries | E-RUNTIME | S1-S4 | | | | Pending |
| R26 | `workflowTriggerBinding` / `runtime_workflow_trigger_binding` | `IWorkflowTriggerBindingStore` | E-CLAIM | S1-S4 | | | | Pending |
| R27 | `recurringTriggerSchedule` / `runtime_recurring_trigger_schedule` | `IRecurringTriggerScheduleStore` | E-CLAIM | S1-S4 | | | | Pending |
| R28 | `workflowActivationSlot` / `runtime_workflow_activation_slot` | `IWorkflowActivationAuthority`; exclusive activation slot | E-CLAIM | S1-S4 | | | | Pending |
| R29 | `publicationProjectionState` / `runtime_publication_projection_state` | Publication projection reconciliation state | E-PUBLISH | S1-S4; #1677 | | | | Pending |

## Distributed Runtime: 3 units

Owner: Runtime program #1672 and proving slice #1676. Dependencies: S1-S4.

| ID | Groundwork unit / physical name | Domain contract or semantic role | Evidence | Blockers | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|
| D01 | `elsa-distributed-execution-placement` / `elsa_distributed_execution_placement` | `IExecutionPlacementStore`; exclusive placement, ownership and fencing | E-CLAIM | S1-S4 | | | | Pending |
| D02 | `elsa-distributed-command-stream-head` / `elsa_distributed_command_stream_head` | Atomic per-execution sequence head used by command transport | E-CLAIM | S1-S4 | | | | Pending |
| D03 | `elsa-distributed-command-transport` / `elsa_distributed_command_transport` | `IExecutionCommandTransport`; ordered enqueue, lease, ack and redelivery | E-CLAIM | S1-S4 | | | | Pending |

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
| I01 | `identityUser` / `identity_users` | `IUserStore`; ASP.NET Identity user adapter | E-IAM | S1-S3 | | | | Pending |
| I02 | `identityRole` / `identity_roles` | `IRoleStore`; ASP.NET Identity role adapter | E-IAM | S1-S3 | | | | Pending |
| I03 | `identityApplication` / `identity_applications` | `IApplicationStore` | E-IAM | S1-S3 | | | | Pending |
| I04 | `identityCredential` / `identity_credentials` | `ICredentialStore` | E-IAM | S1-S3 | | | | Pending |
| I05 | `identityClaimMapping` / `identity_claim_mappings` | `IClaimMappingStore` | E-IAM | S1-S3 | | | | Pending |
| I06 | `identityProviderConfiguration` / `identity_provider_configurations` | `IProviderConfigurationStore`; tenant-scoped provider config | E-IAM | S1-S3 | | | | Pending |
| I07 | `identityGlobalProviderConfiguration` / `identity_global_provider_configurations` | `IProviderConfigurationStore`; global provider config | E-IAM | S1-S3 | | | | Pending |
| I08 | `identityUserClaim` / `identity_user_claims` | User-claim relationship operations | E-IAM | S1-S3 | | | | Pending |
| I09 | `identityRoleClaim` / `identity_role_claims` | Role-claim relationship operations | E-IAM | S1-S3 | | | | Pending |
| I10 | `identityExternalLogin` / `identity_external_logins` | `IExternalIdentityStore`; login relationship operations | E-IAM | S1-S3 | | | | Pending |
| I11 | `identityUserRole` / `identity_user_roles` | User-role relationship operations | E-IAM | S1-S3 | | | | Pending |
| I12 | `identityUserToken` / `identity_user_tokens` | User-token relationship operations | E-IAM | S1-S3 | | | | Pending |
| I13 | `identityTenantMembership` / `identity_tenant_memberships` | `ITenantMembershipStore` | E-IAM | S1-S3 | | | | Pending |
| I14 | `identityUserNameReservation` / `identity_user_name_reservations` | Atomic normalized-user-name reservation | E-IAM | S1-S3 | | | | Pending |
| I15 | `identityEmailReservation` / `identity_email_reservations` | Atomic normalized-email reservation | E-IAM | S1-S3 | | | | Pending |
| I16 | `identityRoleNameReservation` / `identity_role_name_reservations` | Atomic normalized-role-name reservation | E-IAM | S1-S3 | | | | Pending |
| I17 | `identityMutationReceipt` / `identity_mutation_receipts` | Replay-safe mutation receipt and expiry cleanup | E-IAM | S1-S3 | | | | Pending |

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
| W01 | `workflowDefinition` / `elsa_workflow_definitions_v2` | `IWorkflowDefinitionStore`; definition read authority | E-DESIGN | S1-S3 | | | | Pending |
| W02 | `workflowDefinitionVersion` / `elsa_workflow_definition_versions` | `IWorkflowDefinitionVersionStore` | E-DESIGN | S1-S3 | | | | Pending |
| W03 | `workflowDefinitionDraft` / `elsa_workflow_definition_drafts` | `IWorkflowDefinitionDraftStore` and draft commands | E-DESIGN | S1-S3 | | | | Pending |
| W04 | `workflowDefinitionVersionLayout` / `elsa_workflow_definition_version_layouts` | `IWorkflowDefinitionVersionLayoutStore` | E-DESIGN | S1-S3 | | | | Pending |
| W05 | `workflowDesignOperation` / `elsa_design_operations` | Idempotent design-operation ledger and atomic commands | E-CLAIM | S1-S3 | | | | Pending |

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
