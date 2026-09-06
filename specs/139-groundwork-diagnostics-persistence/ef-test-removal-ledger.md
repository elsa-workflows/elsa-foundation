# Diagnostics EF test-removal ledger

**Work unit:** `139-groundwork-diagnostics-persistence`
**Scope:** historical T053/T054 inventory plus the reviewed 2026-09-06 deletion disposition below.
**Captured:** 2026-09-01 on `codex/642-diagnostics-03-recertification`, against Elsa `main` `8b92d34b7`.
**Groundwork baseline:** exact `0.4.0-preview.3` (tag `v0.4.0-preview.3`, release SHA `9de7aa0e6271c311536d2a78a1e6c1f0260e1fda`).

## Current pre-deletion audit — 2026-09-05

The active implementation consumes Groundwork `0.4.0-preview.16`. The September 2 checkpoint below
is historical evidence, not the current package or deletion authority. Re-opening the original EF
methods and their named replacements found gaps in append-failure/high-water, retention retry/final
drain, shutdown-option, and bounded-read proofs. The `codex/642-diagnostics-retention-proofs` slice
repairs those mappings with direct adapter tests before any deletion. A matching method name alone
does not establish behavioral coverage.

Four old contracts are explicitly superseded below rather than claimed unchanged. Their named
replacement tests document the current Groundwork lifecycle/schema behavior. This distinction is
not test-deletion approval: #646 must first accept the four-provider verdict, and #642/#1472 must
record the reviewed deletion disposition under framework §2.21.1. #647 is a superseded umbrella.

## Historical v3 closeout checkpoint — 2026-09-02

The historical inventory below remains the deletion ledger, but its preview.3 header is no longer the
active implementation boundary. Diagnostics #642 now consumes Groundwork `0.4.0-preview.9` and the
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

**Disposition: 39 covered; 4 superseded contracts; 3 EF-mechanism-only facts retired at the Groundwork boundary.** Every one
of the 46 original EF facts has an explicit row and named outcome; the ledger is mechanically checked by
`DiagnosticsPersistenceArchitectureTests`.

## Structured Logs — 30 facts

| EF test identity | Exact objective | Provider-neutral/Groundwork evidence and intake state |
|---|---|---|
| `EfCoreStructuredLogStoreTests.GetHighWaterMarkReturnsMaxSequence` | Highest committed logical sequence. | `GroundworkV2StructuredLogStoreTests.Provider_sequence_is_the_public_sequence_and_replays_authoritative_cursor` — covered. |
| `.GetHighWaterMarkReturnsZeroWhenTableMissing` | Missing table looked like never-written history. | Superseded contract: startup applies the declaration; missing-table read failures do not silently become empty history. Replacement evidence: `GroundworkV2StructuredLogStoreTests.Connection_constructor_applies_schema_and_publishes_actual_provider_capabilities` and `DiagnosticsPersistenceLifecycleTests.Startup_failure_releases_prior_leases_preserves_the_exact_failure_and_starts_no_drains`. |
| `.GetHighWaterMarkPropagatesProviderFailureInsteadOfReportingNeverWritten` | Operational failure remains visible. | `GroundworkV2StructuredLogStoreTests.High_water_mark_propagates_a_direct_inspection_failure` — covered with the identical provider exception propagated from the direct inspection seam. |
| `.GetHighWaterMarkDoesNotMisclassifyCorruptTableShapeAsNeverWritten` | Schema corruption is not empty history. | `DiagnosticsPersistenceLifecycleTests.Startup_failure_releases_prior_leases_preserves_the_exact_failure_and_starts_no_drains` — covered by the shared fail-closed startup contract. |
| `.GetRecentIsNewestLastAndClampedToRequestedCount` | Bounded newest window, ascending result. | `GroundworkV2StructuredLogStoreTests.Recent_query_clamps_to_requested_count_after_filtering`, `GroundworkV2StructuredLogStoreTests.Recent_query_clamps_a_large_requested_count_to_configured_maximum`, and `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered by greater-than-limit durable rows, configured maximum, ascending output, and provider conformance. |
| `.GetRecentAppliesLevelFilter` | Minimum-level filter. | `GroundworkV2StructuredLogStoreTests.Recent_query_applies_the_minimum_level_to_durable_rows` — covered with lower, equal, and higher severities around the requested threshold. |
| `.ReplayPagesThroughTheWholeSnapshotBeyondThePerQueryLimit` | Paged cursor replay beyond one query. | `GroundworkV2StructuredLogStoreTests.Read_after_scans_filtered_positions_and_restart_continues_provider_sequence` and `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered. |
| `.ReadAfterRejectsDefaultCursorAtTheEfStoreBoundary` | Default cursor is non-disclosing unavailable. | `GroundworkV2StructuredLogStoreTests.Malformed_or_foreign_cursors_have_one_non_disclosing_outcome` — covered. |
| `.TrimToZeroAndRestartPreserveLifetimeLogicalHighWater` | Trimmed anchor expires, high-water survives restart. | `GroundworkV2StructuredLogStoreTests.Scope_isolation_and_zero_retention_preserve_lifetime_high_water_without_reserved_data_rows` and `GroundworkV2ProviderMatrixTests.Structured_logs_preserve_public_behavior_across_native_providers` — covered. |
| `.AppendRejectsTheExactReservedHighWaterRepresentation` | EF state-row sentinel cannot collide with data. | Retired at the Groundwork boundary: Groundwork has no reserved high-water data row; provider sequence/high-water metadata is separate from visible records. Current v2 coverage is `GroundworkV2StructuredLogStoreTests.Scope_isolation_and_zero_retention_preserve_lifetime_high_water_without_reserved_data_rows`, including the post-trim empty data set and preserved lifetime high-water. |
| `.ConcurrentFirstInitializationPreservesTheMaximumWithoutExposingStateRows` | Concurrent writers retain distinct commits and maximum high-water. | `GroundworkV2StructuredLogStoreTests.Tied_writers_replay_in_provider_sequence_order` — covered. |
| `.FailedBatchCannotAdvanceLifetimeHighWaterBeforeCommit` | Failed append has no visible durable/high-water mutation. | `GroundworkV2StructuredLogStoreTests.Failed_append_rolls_back_durable_row_and_lifetime_high_water_before_commit` — covered with a real SQLite BEFORE INSERT failure after acceptance, a failed acknowledgement, no durable row, and unchanged high-water. |
| `.DrainPersistsAppendedEntriesRoundTrippingComplexFields` | Properties, scopes, and exception survive durable round trip. | `GroundworkV2StructuredLogStoreTests.Complex_structured_log_fields_round_trip_through_the_v2_payload` — covered. |
| `.DrainPrunesOldestBeyondRetentionCap` | Exact newest retention after regular draining. | `GroundworkV2StructuredLogStoreTests.Exact_retention_acknowledgement_loss_retries_one_operation_and_keeps_newest_rows` — covered. |
| `.CompleteDrainingPrunesTailBelowPruneInterval` | Final drain applies retention even below interval. | `GroundworkV2StructuredLogStoreTests.Final_retention_prunes_rows_below_the_periodic_interval` — covered with actual durable rows, fewer appends than the interval, and exact newest rows after stop. |
| `.CompleteDrainingAsyncThrowsWhenDrainingWasNeverStarted` | Direct pre-start completion is invalid. | Superseded contract: the replacement exposes terminal pre-start stop, not the old EF completion exception. Replacement evidence: `DiagnosticsDrainLifecycleTests.Lifecycle_stop_before_start_is_terminal_without_provider_io` and `GroundworkV2StructuredLogStoreTests.Lifecycle_refuses_prestart_and_stops_without_accepting_new_work`. |
| `.ShellTerminatorWhenDrainingWasNeverStartedIsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsyncPersistsBufferedEntriesBeforeCancelling` | Async disposal drains accepted entries. | `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent` — covered. |
| `.DisposeAsyncClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `GroundworkV2StructuredLogStoreTests.Negative_shutdown_timeout_is_clamped_and_disposal_settles_an_accepted_append` — covered at the adapter boundary with a negative option and an accepted uncooperative append. |
| `.HardStopCompletesAcceptedAppendWithFailure` | Uncooperative provider cannot strand an acknowledgement. | `GroundworkV2StructuredLogStoreTests.Hard_stop_settles_accepted_append_when_provider_ignores_cancellation` — covered. |
| `.DisposeIsIdempotentAcrossSyncAndAsyncPaths` | Mixed disposal is idempotent. | Superseded contract: the Groundwork adapter exposes async disposal, not the old mixed sync/async EF interface. Replacement evidence: `DiagnosticsDrainLifecycleTests.Async_disposal_gracefully_drains_and_is_idempotent`; shared synchronous drain disposal is independently exercised by `DiagnosticsDrainLifecycleTests.Synchronous_disposal_settles_queued_acknowledgements_and_is_idempotent`. |
| `EfCoreStructuredLogStoreResilienceTests.ExhaustedPersistRetriesLogTheDroppedBatchAndKeepTheDrainLoopAlive` | Retry exhaustion is observable and later work recovers. | `DiagnosticsDrainLoadTests.Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers` — covered (provider-neutral counters/loss reason replace EF logger text). |
| `.ChannelOverflowShedsOldestEntriesAndLogsAWarning` | Oldest queued item is shed and observable. | `DiagnosticsDrainLoadTests.Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement` — covered. |
| `EfCoreStructuredLogStorePruneRetryTests.SingleTransientPruneFailureDoesNotAbandonRetention` | Transient periodic-retention failure retries. | `GroundworkV2StructuredLogStoreTests.Periodic_retention_retries_a_transient_failure_and_keeps_the_counter_healthy` — covered by periodic provider failure/retry with one stable operation identity and exact newest durable rows. |
| `.ExhaustedPruneRetriesKeepTheCounterArmedForTheNextBatch` | Retention failure does not kill later retention. | `GroundworkV2StructuredLogStoreTests.Exhausted_periodic_retention_retries_remain_armed_for_the_next_batch` — covered by exhausted periodic retries followed by a later append that retries retention and preserves the exact newest rows. |
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
| `.CompleteDrainingAsync_WhenDrainingWasNeverStarted_Throws` | Direct pre-start completion is invalid. | Superseded contract: the Groundwork adapter delegates completion to terminal pre-start stop rather than preserving the EF exception. Replacement evidence: `DiagnosticsDrainLifecycleTests.Lifecycle_stop_before_start_is_terminal_without_provider_io`. |
| `.ShellTerminator_WhenDrainingWasNeverStarted_IsNoOp` | Host terminator is safe before start. | `DiagnosticsPersistenceLifecycleTests.Cshell_lifecycle_uses_the_same_coordinator_after_provider_prepare` — covered. |
| `.DisposeAsync_ClampsNegativeShutdownDrainTimeout` | Invalid negative timeout cannot break shutdown. | `GroundworkV2OpenTelemetryTests.SQLite_negative_shutdown_timeout_settles_an_accepted_capture_at_the_adapter_boundary` — covered with the real Groundwork adapter, a negative configured timeout, an accepted/dequeued capture blocked before commit, bounded disposal reporting exactly one shutdown-timeout loss, and awaited unit-of-work cleanup before the lease is released. Physical queue depth is not used as proof of acknowledgement settlement. |
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

Historical pre-deletion exit conditions (superseded by the current disposition below):

1. The 39 covered rows and four superseded-contract replacement proofs must remain green in the exact
   current-family certification and preserved provider-neutral suites; the three EF-mechanism-only rows are explicitly retired at the
   Groundwork boundary, but their source tests remain until the separate deletion gates are met.
2. Satisfy the existing T050-T052 performance/remediation gates and the uncompleted T047/T057 final
   architecture and zero-EF checks. #646 owns the accepted diagnostics verdict and #642/#1472 own the
   dependency-ordered diagnostics deletion. This ledger does not authorize or mark either deletion task complete.

## Current deletion disposition — 2026-09-06

The pre-deletion checkpoints and the 46 row-level dispositions above are retained as historical evidence;
they are not rewritten as new test receipts. On the owner-approved #642 deletion branch, T053 and T054
are now staged: the first-party Structured Logs and OpenTelemetry EF implementation projects, SQLite
variants, migrations, and their obsolete EF test projects have been removed from the active source and
solution composition. The provider-neutral Groundwork replacements named by the ledger remain intact.

The ledger still contains all 46 original objectives: 39 covered rows, four superseded contracts, and
three EF-mechanism-only facts retired at the Groundwork boundary. The 43 non-retired rows remain the
behavior-retention disposition for this deletion; this entry does not claim a new provider, performance,
native-plan, or zero-EF certification. The shared EF kernel, EF Secret comparator, vendor OpenIddict EF
integration, and unrelated database support remain intentionally outside this slice. Remaining benchmark
harness contract cleanup is deferred to its owning follow-up rather than represented as deleted evidence here.
