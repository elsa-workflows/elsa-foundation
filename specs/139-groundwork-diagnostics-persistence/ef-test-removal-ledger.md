# Diagnostics EF test-removal ledger

**Work unit:** `139-groundwork-diagnostics-persistence`
**Scope:** pending T053/T054 only; no EF project or test deletion is authorized by this intake.
**Captured:** 2026-09-01 on `codex/642-diagnostics-03-recertification`, against Elsa `main` `8b92d34b7`.
**Groundwork baseline:** exact `0.4.0-preview.3` (tag `v0.4.0-preview.3`, release SHA `9de7aa0e6271c311536d2a78a1e6c1f0260e1fda`).

## Active v3 closeout checkpoint — 2026-09-02

The historical inventory below remains the deletion ledger, but its preview.3 header is no longer the
active implementation boundary. Diagnostics #642 now consumes Groundwork `0.4.0-preview.8` and the
OpenTelemetry adapter has a v3 capture/summary contract with the following executable local evidence:

- one exact multi-unit transaction commits all four signal streams, resource/instrument catalogs, the
  trace summary, and the versioned v3 capture ledger;
- the ledger fingerprints caller-supplied values with type/length framing and remains stable when a
  referenced catalog entry changes between acknowledgement and replay;
- summaries provide bounded canonical trace/name search keys, exact resource/service element filters,
  locale-independent workflow substring matching, deterministic merge/order, and case-equivalent
  span/log detail lookup;
- trace retention and summary recomputation/deletion commit in the same exact unit of work; a post-commit
  lost acknowledgement replays the identical retention `OperationId`, including multi-resource service
  preservation and the zero-retention case;
- readiness rejects providers lacking atomic commit, exact append outcomes, exact retention, or exact
  retention affected keys before schema application or drain startup. Transaction-capable MongoDB replica
  sets remain in the supported matrix; standalone MongoDB is an explicit refusal case.

`GroundworkV2OpenTelemetryTests` passes 25/25 on SQLite and the strengthened native SQLite matrix row
passes locally. PostgreSQL, SQL Server, transaction-capable MongoDB, and standalone-Mongo refusal are
compiled but await the dedicated CI provider run on the exact PR head. This checkpoint advances no
performance verdict: #646 continues to own performance evidence as a separate gate, and #647 continues
to own EF-source deletion after correctness and provider evidence are green.

This follows Spec 093's source-method ledger and its shared-host addendum. It inventories every
`[Fact]` in the two EF test projects, including the SQLite feature/host tests that a token-only
EF scan can miss. A row is not deletion approval: framework §2.21.1 requires explicit recorded
architect approval before the original test can be removed. `Covered` means a named Groundwork or
provider-neutral test has been inspected and exercises the objective. `EF-mechanism-only` means the
behavior is an implementation detail of the EF oracle with no provider-neutral contract to preserve.
The current certification explicitly records those rows as retired at the Groundwork boundary; this
is a disposition of the EF test objective, not authorization to delete the EF oracle. T053/T054
remain separately gated by #646 and #647.

**Disposition: 43 covered; 3 EF-mechanism-only facts retired at the Groundwork boundary.** Every one
of the 46 original EF facts has an explicit row and named outcome; the ledger is mechanically checked by
`DiagnosticsPersistenceArchitectureTests`.

## Structured Logs — 30 facts

| EF test identity | Exact objective | Provider-neutral/Groundwork evidence and intake state |
|---|---|---|
| `EfCoreStructuredLogStoreTests.GetHighWaterMarkReturnsMaxSequence` | Highest committed logical sequence. | `GroundworkV2StructuredLogStoreTests.Provider_sequence_is_the_public_sequence_and_replays_authoritative_cursor` — covered. |
| `.GetHighWaterMarkReturnsZeroWhenTableMissing` | Missing table looked like never-written history. | `GroundworkV2StructuredLogStoreTests.Connection_constructor_applies_schema_and_publishes_actual_provider_capabilities` and `DiagnosticsPersistenceLifecycleTests.Startup_failure_releases_prior_leases_preserves_the_exact_failure_and_starts_no_drains` — covered with the deliberate new contract: startup applies the declared schema and any startup failure remains visible rather than degrading to zero or in-memory state. |
| `.GetHighWaterMarkPropagatesProviderFailureInsteadOfReportingNeverWritten` | Operational failure remains visible. | `GroundworkV2StructuredLogStoreTests.Operational_query_failure_is_not_reported_as_cursor_unavailable` — covered. |
| `.GetHighWaterMarkDoesNotMisclassifyCorruptTableShapeAsNeverWritten` | Schema corruption is not empty history. | `DiagnosticsPersistenceLifecycleTests.Startup_failure_releases_prior_leases_preserves_the_exact_failure_and_starts_no_drains` — covered by the shared fail-closed startup contract. |
| `.GetRecentIsNewestLastAndClampedToRequestedCount` | Bounded newest window, ascending result. | `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered on all four providers. |
| `.GetRecentAppliesLevelFilter` | Minimum-level filter. | `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered on all four providers. |
| `.ReplayPagesThroughTheWholeSnapshotBeyondThePerQueryLimit` | Paged cursor replay beyond one query. | `GroundworkV2StructuredLogStoreTests.Read_after_scans_filtered_positions_and_restart_continues_provider_sequence` and `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered. |
| `.ReadAfterRejectsDefaultCursorAtTheEfStoreBoundary` | Default cursor is non-disclosing unavailable. | `GroundworkV2StructuredLogStoreTests.Malformed_or_foreign_cursors_have_one_non_disclosing_outcome` — covered. |
| `.TrimToZeroAndRestartPreserveLifetimeLogicalHighWater` | Trimmed anchor expires, high-water survives restart. | `GroundworkV2StructuredLogStoreTests.Scope_isolation_and_zero_retention_preserve_lifetime_high_water_without_reserved_data_rows` and `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered. |
| `.AppendRejectsTheExactReservedHighWaterRepresentation` | EF state-row sentinel cannot collide with data. | Retired at the Groundwork boundary: Groundwork has no reserved high-water data row; provider sequence/high-water metadata is separate from visible records. Current v2 coverage is `GroundworkV2StructuredLogStoreTests.Scope_isolation_and_zero_retention_preserve_lifetime_high_water_without_reserved_data_rows`, including the post-trim empty data set and preserved lifetime high-water. |
| `.ConcurrentFirstInitializationPreservesTheMaximumWithoutExposingStateRows` | Concurrent writers retain distinct commits and maximum high-water. | `GroundworkV2StructuredLogStoreTests.Tied_writers_replay_in_provider_sequence_order` — covered. |
| `.FailedBatchCannotAdvanceLifetimeHighWaterBeforeCommit` | Failed append has no visible durable/high-water mutation. | `GroundworkV2StructuredLogStoreTests.Canceled_append_is_refused_before_provider_work` — covered at the current adapter boundary. |
| `.DrainPersistsAppendedEntriesRoundTrippingComplexFields` | Properties, scopes, and exception survive durable round trip. | `GroundworkV2StructuredLogStoreTests.Complex_structured_log_fields_round_trip_through_the_v2_payload` — covered. |
| `.DrainPrunesOldestBeyondRetentionCap` | Exact newest retention after regular draining. | `GroundworkV2StructuredLogStoreTests.Exact_retention_acknowledgement_loss_retries_one_operation_and_keeps_newest_rows` — covered. |
| `.CompleteDrainingPrunesTailBelowPruneInterval` | Final drain applies retention even below interval. | `DiagnosticsDrainShutdownTests.Graceful_stop_drains_tail_applies_final_retention_and_completes_all_acks` — covered. |
| `.CompleteDrainingAsyncThrowsWhenDrainingWasNeverStarted` | Direct pre-start completion is invalid. | `GroundworkV2StructuredLogStoreTests.Lifecycle_refuses_prestart_and_stops_without_accepting_new_work` — covered by the replacement lifecycle contract. |
| `.ShellTerminatorWhenDrainingWasNeverStartedIsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsyncPersistsBufferedEntriesBeforeCancelling` | Async disposal drains accepted entries. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` — covered. |
| `.DisposeAsyncClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `DiagnosticsDrainShutdownTests.Timeout_is_bounded_and_completes_the_inflight_acknowledgement` — covered by the provider-neutral bounded shutdown contract. |
| `.HardStopCompletesAcceptedAppendWithFailure` | Uncooperative provider cannot strand an acknowledgement. | `GroundworkV2StructuredLogStoreTests.Hard_stop_settles_accepted_append_when_provider_ignores_cancellation` — covered. |
| `.DisposeIsIdempotentAcrossSyncAndAsyncPaths` | Mixed disposal is idempotent. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` and `DiagnosticsDrainLifecycleTests.Synchronous_disposal_settles_queued_acknowledgements_and_is_idempotent` — covered. |
| `EfCoreStructuredLogStoreResilienceTests.ExhaustedPersistRetriesLogTheDroppedBatchAndKeepTheDrainLoopAlive` | Retry exhaustion is observable and later work recovers. | `DiagnosticsDrainLoadTests.Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers` — covered (provider-neutral counters/loss reason replace EF logger text). |
| `.ChannelOverflowShedsOldestEntriesAndLogsAWarning` | Oldest queued item is shed and observable. | `DiagnosticsDrainLoadTests.Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement` — covered. |
| `EfCoreStructuredLogStorePruneRetryTests.SingleTransientPruneFailureDoesNotAbandonRetention` | Transient final-retention failure retries. | `DiagnosticsDrainShutdownTests.Transient_final_retention_failure_retries_and_recovers` — covered. |
| `.ExhaustedPruneRetriesKeepTheCounterArmedForTheNextBatch` | Retention failure does not kill later retention. | `DiagnosticsDrainShutdownTests.Exhausted_final_retention_failure_is_classified_without_invalidating_commits` — covered. |
| `SqliteStructuredLogsPersistenceFeatureTests.RegistersEfCoreStoreAsTheStructuredLogStore` | One selected durable replacement. | `DiagnosticsPersistenceFeatureTests.Enabled_Groundwork_persistence_features_replace_each_default_store_once` — covered. |
| `.PersistentStoreResolvesWhenStructuredLogCaptureIsEnabled` | Selected durable store is reachable through host composition. | `GroundworkV2StructuredLogStoreTests.Groundwork_feature_resolves_one_connection_backed_store_for_both_contracts` — covered. |
| `.RegistersTheDrainingStartupTask` | Durable drain enters host lifecycle. | `DiagnosticsPersistenceLifecycleTests.Registration_uses_one_hosted_coordinator_and_the_selected_singleton_drain_instances` — covered. |
| `.RegistersTheDrainingShellTerminator` | Shell termination shares the coordinator. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency` | EF factory lifetime avoids captive dependency. | Retired at the Groundwork boundary: the replacement owns provider connections/sessions directly and has no `IDbContextFactory` or captive-context lifetime to preserve. Current v2 connection/session lifecycle coverage is in `GroundworkV2StructuredLogStoreTests.Connection_constructor_applies_schema_and_publishes_actual_provider_capabilities`. |

## OpenTelemetry — 16 facts

| EF test identity | Exact objective | Provider-neutral/Groundwork evidence and intake state |
|---|---|---|
| `EfCoreOpenTelemetryStoreTests.WriteAsync_PopulatesSourceRegistrySynchronouslyAndPersistsAllSignalTypes` | Accepted batch marks source, persists every signal kind, filters resources and traces (including workflow instance), and returns trace detail, metrics, and logs. | `GroundworkV2OpenTelemetryTests.Accepted_capture_marks_the_source_synchronously_and_persists_every_signal_kind`, `GroundworkV2OpenTelemetryProviderMatrixTests.Ordinary_units_and_filters_round_trip_on_each_native_provider`, and `GroundworkV2OpenTelemetryTests.SQLite_round_trip_uses_ordinary_units_and_declared_trace_source_filter` — covered, including all four providers. |
| `.QueryTracesAsync_WhenTraceIdAppearsInMultipleBatches_ReturnsMergedSummary` | Repeat trace records merge earliest start, latest end, worst status, summed span count, and workflow ids. | `GroundworkV2OpenTelemetryTests.SQLite_repeated_trace_records_merge_earliest_latest_worst_count_and_workflows` — covered by current v2 executable evidence. |
| `.QueryMetricsAndLogs_FilterByServiceNameThroughDurableResources` | Metric/log service-name filtering follows durable resource metadata. | `GroundworkV2OpenTelemetryTests.SQLite_metric_and_log_service_filters_follow_durable_resource_values` plus `GroundworkV2OpenTelemetryProviderMatrixTests.Ordinary_units_and_filters_round_trip_on_each_native_provider` — covered by the adapter suite and all four providers. |
| `.QueryMetricsAsync_WhenInstrumentIdsDifferOnlyByCase_CollapsesLikeInMemoryStore` | Case-equivalent catalog ids coalesce but retain all points. | `GroundworkV2OpenTelemetryTests.SQLite_case_equivalent_instrument_ids_collapse_without_losing_points` — covered. |
| `.DrainPrunesHighVolumeSignalsToConfiguredCapacities` | Exact retention for all immutable signal streams and catalogs. | `GroundworkV2OpenTelemetryTests.SQLite_final_retention_keeps_exact_newest_signal_and_catalog_windows` — covered. |
| `.CompleteDrainingAsync_WhenDrainingWasNeverStarted_Throws` | Direct pre-start completion is invalid. | `DiagnosticsDrainLifecycleTests.Lifecycle_stop_before_start_is_terminal_without_provider_io` — covered by replacement lifecycle contract. |
| `.ShellTerminator_WhenDrainingWasNeverStarted_IsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsync_ClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `DiagnosticsDrainShutdownTests.Timeout_is_bounded_and_completes_the_inflight_acknowledgement` — covered. |
| `.DisposeAsync_PersistsBufferedBatchesBeforeCancelling` | Async shutdown drains accepted batches. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` — covered. |
| `EfCoreOpenTelemetryStoreResilienceTests.ExhaustedPersistRetriesDropTheBatchLogAndSurfaceDroppedCounts` | Retry exhaustion exposes per-signal loss and later work recovers. | `DiagnosticsDrainLoadTests.Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers` and `DiagnosticsPersistenceObservabilityTests.Every_loss_reason_is_counted_independently` — covered. |
| `.ChannelOverflowShedsOldestBatchesLogsAndSurfacesDroppedCounts` | Queue overflow sheds oldest batch and counts dropped signals. | `DiagnosticsDrainLoadTests.Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement` and `DiagnosticsPersistenceObservabilityTests.Every_loss_reason_is_counted_independently` — covered. |
| `SqliteOpenTelemetryPersistenceFeatureTests.RegistersEfCoreStoreAsReplacementAfterDefaultFeature` | Explicit durable replacement wins after default registration. | `DiagnosticsPersistenceFeatureTests.Enabled_Groundwork_persistence_features_replace_each_default_store_once` — covered. |
| `.PreventsDefaultStoreInterfaceRegistrationWhenPersistenceFeatureRunsFirst` | Selection is registration-order independent. | `DiagnosticsPersistenceArchitectureTests.Default_and_explicit_registration_have_order_independent_selection_semantics` — covered. |
| `.RegistersTheDrainingStartupTask` | Durable drain enters host lifecycle. | `DiagnosticsPersistenceLifecycleTests.Registration_uses_one_hosted_coordinator_and_the_selected_singleton_drain_instances` — covered. |
| `.RegistersTheDrainingShellTerminator` | Shell termination shares the coordinator. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency` | EF factory lifetime avoids captive dependency. | Retired at the Groundwork boundary: the replacement owns provider connections/sessions directly and has no `IDbContextFactory` or captive-context lifetime to preserve. Current v2 connection/session lifecycle coverage is in `GroundworkV2OpenTelemetryTests.SQLite_queued_capture_survives_restart_and_scope_isolation`. |

## Inventory and exit conditions

The two project trees contain exactly **46 facts**: 30 Structured Logs and 16 OpenTelemetry.
Support-only EF sources were opened as part of the reachability review: `StructuredLogsTestHost`,
`OpenTelemetryTestHost`, `OpenTelemetryPersistenceTestContext`, and the fault-injecting factories.
The added shared-host lifecycle test is the resulting non-token-scan coverage. The repeated-trace
Groundwork conformance row is covered on the current adapter head; the former Groundwork #130
blocker is historical and is not a reason to delete or retain a test by itself.

Before T053/T054 can be approved and performed:

1. The 43 covered rows must remain green in the exact current-family certification and preserved
   provider-neutral suites; the three EF-mechanism-only rows are explicitly retired at the
   Groundwork boundary, but their source tests remain until the separate deletion gates are met.
2. Satisfy the existing T050-T052 performance/remediation gates and the uncompleted T047/T057 final
   architecture and zero-EF checks. #646 owns the accepted diagnostics verdict and #647 owns the
   dependency-ordered deletion. This ledger does not authorize or mark either deletion task complete.
