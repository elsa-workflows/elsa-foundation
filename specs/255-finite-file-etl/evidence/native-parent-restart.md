# Native parent survives a host restart while waiting on RunEtl

Part of [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892); the leaf is
[planned on the program issue](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6738102053).

## What is proved

A native Sequence, Flowchart or CompensationScope parent that is durably suspended on `RunEtl` over SQLite survives a
host restart at each hand-off point of the ETL integration and then reaches exactly one native outcome. The tests are
`EtlNativeCompositeJourneyTests` in `Extensions/Etl/tests/Tests/EtlNativeCompositeJourneyTests.HostRestart.cs`
(tier `pr`, shard `pr-1`):

| Case | Cut | Proof |
|---|---|---|
| `A_suspended_RunEtl_survives_a_host_rebuild_before_any_processing_and_completes_once` | Parent suspended, run admitted, worker never ran | Rebuilt host's worker runs the pipeline; one consumed receipt, `after-etl` runs once, run `Succeeded`, outbox acknowledged |
| `A_result_claimed_for_delivery_before_native_consumption_survives_a_host_rebuild_and_is_consumed_once` | Run terminal, delivery lease claimed by the first host, no dispatch, no acknowledgement | The dead owner's lease is honoured until it expires (five-second minimum); the rebuilt host then delivers exactly once |
| `A_terminal_outcome_reached_after_a_host_rebuild_surfaces_once` (BusinessError, Faulted) | Parent suspended; the outcome is produced after the rebuild | BusinessError completes the child with outcome `BusinessError` and one rejected record; the write failure faults the child with `etl.output-write-failed`, and the parent follows the composite's rule |
| `A_parent_cancelled_after_a_host_rebuild_settles_the_admitted_run_and_refuses_a_late_result` | Parent suspended; cancel issued after the rebuild | Run tombstoned `Cancelled`, bookmark gone, no manifest; a worker started afterwards settles the delivery as `ProvenParentTerminal` instead of resurrecting the run |

The hold in the second case is a test `IEtlResultDelivery` registered after the feature's `TryAdd`, so the worker claims
the real delivery lease and then neither dispatches nor acknowledges, which is what a crash between claim and dispatch
leaves behind. Every other step is production code.

## How the restart is simulated

`NativeEtlJourney.RebuildAsync` stops the ETL worker, disposes the DI container and the Generic Host lifetime (closing
every SQLite connection), keeps the directory, and builds the next host generation over the same `workflow.db`,
`etl-runtime.db`, `etl-design.db`, snapshots and output folder with the same execution id. The new generation uses
activity ids its predecessor cannot have persisted.

## Limits

- In-process rebuild, not a process kill. The process-kill Workbench witness for ETL is the following leaf, once the
  Workbench harness used by the BPMN hosting tests is shared.
- SQLite only; the composite parents of the existing journey (no Root parent, no HTTP source rows).
- "Late result refused" is proved through the cancellation case only; a duplicate delivery after a consumed completion
  is covered by the adapter-level `EtlNativeResultDeliveryTests`, not re-proved across a restart here.
- Local run on 9 November 2026 (macOS, .NET SDK 10.0.402): 15 rows passed, the lease-expiry rows in about six seconds
  each because of the five-second minimum delivery lease.
