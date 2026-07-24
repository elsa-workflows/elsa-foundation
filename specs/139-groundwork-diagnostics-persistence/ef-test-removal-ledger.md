# Diagnostics EF test-removal ledger

**Work unit:** `139-groundwork-diagnostics-persistence`
**Scope:** pending T053/T054 only; no EF project or test deletion is authorized by this intake.
**Captured:** 2026-07-24 on `codex/642-groundwork-diagnostics-replay` (`46f276651`).

This follows Spec 093's source-method ledger and its shared-host addendum. It inventories every
`[Fact]` in the two EF test projects, including the SQLite feature/host tests that a token-only
EF scan can miss. A row is not deletion approval: framework §2.21.1 requires explicit recorded
architect approval before the original test can be removed. `Covered` means a named Groundwork or
provider-neutral test has been inspected and exercises the objective; `blocked` means the old
objective is now represented by a red Groundwork test and T054 cannot delete it.

## Structured Logs — 30 facts

| EF test identity | Exact objective | Provider-neutral/Groundwork evidence and intake state |
|---|---|---|
| `EfCoreStructuredLogStoreTests.GetHighWaterMarkReturnsMaxSequence` | Highest committed logical sequence. | `GroundworkStructuredLogReplayTests.Restart_preserves_cursor_replay_and_lifetime_logical_high_water` — covered. |
| `.GetHighWaterMarkReturnsZeroWhenTableMissing` | Missing table looked like never-written history. | `DiagnosticsPersistenceReadinessTests.Durable_readiness_failures_remain_visible_and_never_degrade_to_an_empty_or_in_memory_store` — covered with the deliberate new contract: missing schema fails readiness rather than returning zero. |
| `.GetHighWaterMarkPropagatesProviderFailureInsteadOfReportingNeverWritten` | Operational failure remains visible. | `GroundworkStructuredLogReplayTests.Operational_query_failure_is_not_reported_as_cursor_unavailable` — covered. |
| `.GetHighWaterMarkDoesNotMisclassifyCorruptTableShapeAsNeverWritten` | Schema corruption is not empty history. | `DiagnosticsPersistenceReadinessTests.Durable_readiness_failures_remain_visible_and_never_degrade_to_an_empty_or_in_memory_store` (drift case) — covered. |
| `.GetRecentIsNewestLastAndClampedToRequestedCount` | Bounded newest window, ascending result. | `DiagnosticsGroundworkProviderConformanceTests.Query_filters_ordering_limits_and_catalog_capacity_match_across_providers` — covered on all four providers. |
| `.GetRecentAppliesLevelFilter` | Minimum-level filter. | `DiagnosticsGroundworkProviderConformanceTests.Query_filters_ordering_limits_and_catalog_capacity_match_across_providers` — covered on all four providers. |
| `.ReplayPagesThroughTheWholeSnapshotBeyondThePerQueryLimit` | Paged cursor replay beyond one query. | `DiagnosticsGroundworkProviderConformanceTests.Structured_log_replay_retry_and_failure_semantics_match_across_providers` — covered on all four providers. |
| `.ReadAfterRejectsDefaultCursorAtTheEfStoreBoundary` | Default cursor is non-disclosing unavailable. | `GroundworkStructuredLogReplayTests.Malformed_or_foreign_binding_fails_with_one_non_disclosing_outcome` — covered. |
| `.TrimToZeroAndRestartPreserveLifetimeLogicalHighWater` | Trimmed anchor expires, high-water survives restart. | `GroundworkStructuredLogReplayTests.Trimmed_anchor_expires_while_lifetime_high_water_survives_restart` — covered. |
| `.AppendRejectsTheExactReservedHighWaterRepresentation` | EF state-row sentinel cannot collide with data. | EF persistence mechanism only; Groundwork has no state-row sentinel. Candidate for architect-approved removal, not a behavior conversion. |
| `.ConcurrentFirstInitializationPreservesTheMaximumWithoutExposingStateRows` | Concurrent writers retain distinct commits and maximum high-water. | `DiagnosticsDurableOperationConformanceTests.Concurrent_writers_commit_distinct_records_in_one_durable_order` plus `GroundworkStructuredLogReplayTests.Tied_entries_replay_in_committed_cursor_order` — covered. |
| `.FailedBatchCannotAdvanceLifetimeHighWaterBeforeCommit` | Failed append has no visible durable/high-water mutation. | `DiagnosticsDurableOperationConformanceTests.Cancellation_before_or_during_provider_work_does_not_mutate` — covered at the provider-neutral operation boundary. |
| `.DrainPersistsAppendedEntriesRoundTrippingComplexFields` | Properties, scopes, and exception survive durable round trip. | `GroundworkStructuredLogReplayTests.Complex_structured_log_fields_round_trip_through_durable_storage` — **added in this intake; covered.** |
| `.DrainPrunesOldestBeyondRetentionCap` | Exact newest retention after regular draining. | `GroundworkStructuredLogStoreTests.Automatic_retention_acknowledgement_loss_retries_the_same_operation_and_keeps_exact_newest_records` — covered. |
| `.CompleteDrainingPrunesTailBelowPruneInterval` | Final drain applies retention even below interval. | `DiagnosticsDrainShutdownTests.Graceful_stop_drains_tail_applies_final_retention_and_completes_all_acks` — covered. |
| `.CompleteDrainingAsyncThrowsWhenDrainingWasNeverStarted` | Direct pre-start completion is invalid. | `GroundworkStructuredLogStoreTests.Lifecycle_stop_before_start_is_terminal_without_retention_io` — covered by the replacement lifecycle contract. |
| `.ShellTerminatorWhenDrainingWasNeverStartedIsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsyncPersistsBufferedEntriesBeforeCancelling` | Async disposal drains accepted entries. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` — covered. |
| `.DisposeAsyncClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `DiagnosticsDrainShutdownTests.Timeout_is_bounded_and_completes_the_inflight_acknowledgement` — covered by the provider-neutral bounded shutdown contract. |
| `.HardStopCompletesAcceptedAppendWithFailure` | Uncooperative provider cannot strand an acknowledgement. | `GroundworkStructuredLogStoreTests.Hard_stop_settles_every_accepted_append_when_provider_ignores_cancellation` — covered. |
| `.DisposeIsIdempotentAcrossSyncAndAsyncPaths` | Mixed disposal is idempotent. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` and `.Synchronous_disposal_settles_queued_acknowledgements_and_is_idempotent` — covered. |
| `EfCoreStructuredLogStoreResilienceTests.ExhaustedPersistRetriesLogTheDroppedBatchAndKeepTheDrainLoopAlive` | Retry exhaustion is observable and later work recovers. | `DiagnosticsDrainLoadTests.Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers` — covered (provider-neutral counters/loss reason replace EF logger text). |
| `.ChannelOverflowShedsOldestEntriesAndLogsAWarning` | Oldest queued item is shed and observable. | `DiagnosticsDrainLoadTests.Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement` — covered. |
| `EfCoreStructuredLogStorePruneRetryTests.SingleTransientPruneFailureDoesNotAbandonRetention` | Transient final-retention failure retries. | `DiagnosticsDrainShutdownTests.Transient_final_retention_failure_retries_and_recovers` — covered. |
| `.ExhaustedPruneRetriesKeepTheCounterArmedForTheNextBatch` | Retention failure does not kill later retention. | `DiagnosticsDrainShutdownTests.Exhausted_final_retention_failure_is_classified_without_invalidating_commits` — covered. |
| `SqliteStructuredLogsPersistenceFeatureTests.RegistersEfCoreStoreAsTheStructuredLogStore` | One selected durable replacement. | `DiagnosticsPersistenceFeatureTests.Enabled_Groundwork_persistence_features_replace_each_default_store_once` — covered. |
| `.PersistentStoreResolvesWhenStructuredLogCaptureIsEnabled` | Selected durable store is reachable through host composition. | `DiagnosticsPersistenceReadinessTests.Selected_durable_stores_resolve_and_start_through_the_shared_host_lifecycle` — **added in this intake; covered.** |
| `.RegistersTheDrainingStartupTask` | Durable drain enters host lifecycle. | `DiagnosticsPersistenceLifecycleTests.Registration_uses_one_hosted_coordinator_and_the_selected_singleton_drain_instances` — covered. |
| `.RegistersTheDrainingShellTerminator` | Shell termination shares the coordinator. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency` | EF factory lifetime avoids captive dependency. | EF-only infrastructure; replacement has no DbContext factory. Candidate for architect-approved removal. |

## OpenTelemetry — 16 facts

| EF test identity | Exact objective | Provider-neutral/Groundwork evidence and intake state |
|---|---|---|
| `EfCoreOpenTelemetryStoreTests.WriteAsync_PopulatesSourceRegistrySynchronouslyAndPersistsAllSignalTypes` | Accepted batch marks source and persists every signal kind. | `GroundworkOpenTelemetryStoreTests.Capture_marks_sources_only_when_the_drain_accepts_the_batch` plus `GroundworkOpenTelemetryRestartTests.Exact_catalog_and_immutable_counts_survive_store_restart` — covered. |
| `.QueryTracesAsync_WhenTraceIdAppearsInMultipleBatches_ReturnsMergedSummary` | Repeat trace records merge earliest start, latest end, worst status, summed span count, and workflow ids. | `GroundworkOpenTelemetryQueryConformanceTests.Repeated_trace_records_merge_to_one_summary_across_durable_batches` — **added red; blocked.** Current `LatestPerKeyField` returns only the newer record. |
| `.QueryMetricsAndLogs_FilterByServiceNameThroughDurableResources` | Metric/log service-name filtering follows durable resource metadata. | `GroundworkOpenTelemetryQueryConformanceTests.Metric_and_log_service_name_filters_follow_the_durable_resource_catalog` plus `DiagnosticsGroundworkProviderConformanceTests.Query_filters_ordering_limits_and_catalog_capacity_match_across_providers` — covered by the adapter suite and all four providers. |
| `.QueryMetricsAsync_WhenInstrumentIdsDifferOnlyByCase_CollapsesLikeInMemoryStore` | Case-equivalent catalog ids coalesce but retain all points. | `GroundworkOpenTelemetryQueryConformanceTests.Case_equivalent_instrument_ids_collapse_while_retaining_both_points` — covered. |
| `.DrainPrunesHighVolumeSignalsToConfiguredCapacities` | Exact retention for all immutable signal streams and catalogs. | `GroundworkOpenTelemetryQueryConformanceTests.Signal_retention_keeps_the_exact_newest_window_for_every_stream` plus `GroundworkOpenTelemetryCatalogTests.Catalog_upserts_and_capacity_keep_the_newest_entries` — covered. |
| `.CompleteDrainingAsync_WhenDrainingWasNeverStarted_Throws` | Direct pre-start completion is invalid. | `GroundworkOpenTelemetryStoreTests.Lifecycle_stop_before_start_is_terminal_without_retention_io` — covered by replacement lifecycle contract. |
| `.ShellTerminator_WhenDrainingWasNeverStarted_IsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsync_ClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `DiagnosticsDrainShutdownTests.Timeout_is_bounded_and_completes_the_inflight_acknowledgement` — covered. |
| `.DisposeAsync_PersistsBufferedBatchesBeforeCancelling` | Async shutdown drains accepted batches. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` — covered. |
| `EfCoreOpenTelemetryStoreResilienceTests.ExhaustedPersistRetriesDropTheBatchLogAndSurfaceDroppedCounts` | Retry exhaustion exposes per-signal loss and later work recovers. | `GroundworkOpenTelemetryStoreTests.Capture_marks_sources_only_when_the_drain_accepts_the_batch` and `DiagnosticsDrainLoadTests.Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers` — covered. |
| `.ChannelOverflowShedsOldestBatchesLogsAndSurfacesDroppedCounts` | Queue overflow sheds oldest batch and counts dropped signals. | `DiagnosticsDrainLoadTests.Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement` and `DiagnosticsPersistenceObservabilityTests.Every_loss_reason_is_counted_independently` — covered. |
| `SqliteOpenTelemetryPersistenceFeatureTests.RegistersEfCoreStoreAsReplacementAfterDefaultFeature` | Explicit durable replacement wins after default registration. | `DiagnosticsPersistenceFeatureTests.Enabled_Groundwork_persistence_features_replace_each_default_store_once` — covered. |
| `.PreventsDefaultStoreInterfaceRegistrationWhenPersistenceFeatureRunsFirst` | Selection is registration-order independent. | `DiagnosticsPersistenceArchitectureTests.Default_and_explicit_registration_have_order_independent_selection_semantics` — covered. |
| `.RegistersTheDrainingStartupTask` | Durable drain enters host lifecycle. | `DiagnosticsPersistenceLifecycleTests.Registration_uses_one_hosted_coordinator_and_the_selected_singleton_drain_instances` — covered. |
| `.RegistersTheDrainingShellTerminator` | Shell termination shares the coordinator. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency` | EF factory lifetime avoids captive dependency. | EF-only infrastructure; replacement has no DbContext factory. Candidate for architect-approved removal. |

## Inventory and exit conditions

The two project trees contain exactly **46 facts**: 30 Structured Logs and 16 OpenTelemetry.
Support-only EF sources were opened as part of the reachability review: `StructuredLogsTestHost`,
`OpenTelemetryTestHost`, `OpenTelemetryPersistenceTestContext`, and the fault-injecting factories.
The added shared-host lifecycle test is the resulting non-token-scan coverage.

Before T053/T054 can be approved and performed:

1. Resolve the one remaining OpenTelemetry test above without broad client-side evaluation, then
   rerun its Groundwork suite and the four-provider conformance relevant to the repaired behavior.
2. Obtain explicit recorded architect approval for every original EF-test removal, including the two
   EF-mechanism-only factory/sentinel rows.
3. Satisfy the existing T050-T052 performance/remediation gates and the uncompleted T047/T057 final
   architecture and zero-EF checks. This ledger does not mark any task complete.
