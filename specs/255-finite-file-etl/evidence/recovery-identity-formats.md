# Recovery, identity and saved-format evidence

This evidence map records the completed T025 checklist item. It maps the current tests to the requested failure boundaries; it does not claim that every Cartesian combination has been exercised.

## Cancellation versus final acceptance

`EtlRuntimeStoreTests.Final_batch_acceptance_and_cancellation_settle_once_in_either_store_order` checks both sequential operation orders against two real SQLite contexts. `Concurrent_final_batch_acceptance_and_cancellation_serialize_real_transactions` uses separate contexts and transaction-interceptor barriers to hold the first real transaction open while observing the second operation enter EF transaction creation, then verifies the single durable winner and its terminal result. These are store/SQLite tests; they do not claim that a worker process was forcibly stopped by cancellation.

## Frozen native identity and registration lifecycle

`EtlNativeCheckpointTests.Suspension_alone_does_not_admit_until_the_real_bookmark_is_committed`, `A_changed_bookmark_address_cannot_create_an_admission_intent`, `Cancellation_before_bookmark_creation_freezes_the_actual_registration_address`, and `Cancellation_uses_the_checkpoint_snapshot_even_if_the_store_still_has_an_older_attempt` cover admission and cancellation intents derived from committed checkpoint state. `Native_private_state_preserves_the_explicit_null_tenant_opaque_partition_and_frozen_target` covers the serialized private request and explicit null tenant.

`A_placed_wait_rejects_a_registration_that_does_not_match_its_frozen_target` and `Bookmark_checkpoint_rejects_a_registration_with_changed_typed_delivery_identity` exercise placed target and typed registration mismatches, including registration ID, invocation, payload alias/schema, stimulus type/hash, and deduplication policy. `A_missing_registration_without_a_consumed_receipt_cannot_be_reconstructed` rejects an unexpectedly absent registration. By contrast, `Cancellation_after_typed_claim_uses_the_consumed_receipt_even_after_payload_retirement` permits recovery from a genuine consumed typed receipt when its payload has been retired. `RunEtlActivityTests.Activation_refuses_an_absent_or_incorrectly_owned_resume_target` and `Resume_refuses_changed_or_unsupported_result_and_saved_state_before_returning_an_outcome` cover activation and resumed-result validation.

These focused checkpoint/activity tests are not themselves a durable workflow journey. `EtlNativeCompositeJourneyTests` exercises the real SQLite native scheduler, ETL store and worker across Sequence, Flowchart and CompensationScope, including terminal success/cancellation, parent termination, and result acknowledgment. `EtlNativeResultDeliveryTests` separately checks consumed-receipt proof, duplicate delivery, mismatched receipts/bookmarks and terminal-parent proof. The native journey evidence is in [native subtree cancellation](native-subtree-cancellation.md) and [terminal outcomes](native-terminal-outcomes.md).

## Changed and corrupt input/output

`EtlFilePreparationTests.Snapshots_the_admitted_revision_after_the_current_alias_moves_forward`, `Refuses_a_removed_admitted_revision_instead_of_falling_back_to_current`, and `Rejects_changed_host_bytes_before_publishing_a_snapshot` verify that admission pins are retained and changed or unavailable host input cannot be substituted by the current alias. `Preparation_reopens_retained_snapshots_and_verifies_them_again` and `Preparation_rejects_a_corrupted_lookup_snapshot_before_index_publication` verify source and lookup snapshot hashes after reopening.

`EtlBatchBuilderTests.Refuses_a_corrupted_source_or_index_before_returning_rows` rejects modified source/index content before returning rows. `EtlRuntimeJourneyTests.Actual_worker_interrupted_mid_staged_write_recovers_without_accepting_partial_content` runs real child workers for both snapshot-copy and output-chunk staged-write interruption; `Two_real_worker_processes_recover_a_published_unaccepted_effect_after_predecessor_exit` covers physical publication followed by process exit before acceptance. The [T024 interruption matrix](interruption-matrix.md) records those process boundaries.

`EtlRuntimeJourneyTests.Missing_or_corrupt_accepted_chunk_refuses_materialization_without_publishing_a_route` checks missing and same-length modified accepted chunks. `Removed_pinned_output_revision_refuses_materialization_instead_of_using_current_alias` prevents output-binding retargeting. `A_changed_materialized_file_is_refused_and_never_overwritten_on_replay` protects an already materialized route, while `Terminal_materialization_replays_both_routes_in_manifest_order_and_ignores_unaccepted_orphans` checks manifest order and exclusion of unaccepted physical files. These are materializer/runtime journey assertions, not a claim that every possible filesystem failure has a dedicated test.

## Unknown persisted formats

`EtlRuntimeStoreTests.Reopened_runtime_refuses_a_saved_artifact_with_an_unknown_format` and `Reopened_runtime_refuses_a_prepared_descriptor_with_an_unknown_format` mutate durable SQLite rows, reopen through the production store, and verify `FindRunAsync` refuses them without changing durable rows. Those tests prove codec/read-path refusal after reopen; they do not execute a claimed partition.

`EtlRuntimeJourneyTests.Claimed_partition_execution_refuses_an_unknown_saved_format_without_progress` adds that entry-path check for both artifact and prepared descriptor. It uses the real SQLite/file fixture to admit and prepare, claims an actual partition, persists the unsupported format, then calls `EtlExecution.ExecutePartitionAsync`. After refusal it reads through a fresh scope and checks that the run is nonterminal, counts and outstanding quota are unchanged, every partition cursor remains at its start, and no reservation, accepted manifest, terminal row, or outbox row was added. The actual output and error directories also remain absent. Claim ownership/fence metadata can change during claim and cleanup, so that metadata is not compared as business progress.

The first four-class focused run passed 85 cases before the registration-ID and claimed-execution additions. The strengthened run then passed **88 tests, zero failures/skips**, including both actual execution-entry cases. Root review added nonempty-partition and absent physical output/error assertions before that run. Command:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter 'FullyQualifiedName~EtlRuntimeStoreTests|FullyQualifiedName~EtlExecutableReaderTests|FullyQualifiedName~EtlNativeCheckpointTests|FullyQualifiedName~RunEtlActivityTests|FullyQualifiedName~Claimed_partition_execution_refuses_an_unknown_saved_format_without_progress'
```

The retained local log is `/tmp/elsa-program-20261020-reprioritization/etl-t025-focused-3.log`. Earlier compiler failures were corrected before the passing runs; they are not test passes. The full integrated Release ETL suite subsequently passed **367 tests, zero failures/skips** using `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release --no-build --no-restore -p:WarningsNotAsErrors=NU1603`; retained log: `/tmp/elsa-program-20261020-reprioritization/etl-integrated-full.log`. Historical proofs above retain their original evidence receipts.

Root review reconciled the named T025 boundaries against the current source and full passing suite. T025 is checked; Studio/browser and final integration/delivery gates remain open. Provider validation is separately limited to the executed SQLite migration/reopen evidence in [provider-reopen.md](provider-reopen.md); this map makes no claim for unexecuted providers.
