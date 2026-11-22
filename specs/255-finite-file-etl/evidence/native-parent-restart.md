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
| `A_result_claimed_for_delivery_before_native_consumption_survives_a_host_rebuild_and_is_consumed_once` | Run terminal, delivery lease claimed by the first host, no dispatch, no acknowledgement | The rebuilt host's worker polls under a test clock: while the clock is inside the dead owner's lease the outbox stays unacknowledged and the child suspended; once the clock passes the expiry it delivers exactly once |
| `A_terminal_outcome_reached_after_a_host_rebuild_surfaces_once` (BusinessError, Faulted) | Parent suspended; the outcome is produced after the rebuild | BusinessError completes the child with outcome `BusinessError` and one rejected record; the write failure faults the child with `etl.output-write-failed`, and the parent follows the composite's rule |
| `A_parent_cancelled_before_a_host_rebuild_settles_the_admitted_run_after_it_and_refuses_a_late_result` | Parent cancelled with the child in flight; the cancel intent is still in the native outbox and the run still admitted when the host dies | The rebuilt host's sweep delivers the intent: run tombstoned `Cancelled`, bookmark gone, no manifest; a worker started afterwards settles the delivery as `ProvenParentTerminal` instead of resurrecting the run |

The hold in the second case is a test `IEtlResultDelivery` registered after the feature's `TryAdd`, so the worker claims
the real delivery lease and then neither dispatches nor acknowledges, which is what a crash between claim and dispatch
leaves behind. The rebuilt host in that case reads time from a test `TimeProvider` (the ETL store and worker both take
it from DI), which is how the witness proves the lease is honoured and then expires without sleeping through the
five-second minimum. Every other step is production code.

## How the restart is simulated

`NativeEtlJourney.RebuildAsync` stops the ETL worker, disposes the DI container and the Generic Host lifetime (closing
every SQLite connection), keeps the directory, and builds the next host generation over the same `workflow.db`,
`etl-runtime.db`, `etl-design.db`, snapshots and output folder with the same execution id. The new generation uses
activity ids its predecessor cannot have persisted.

## Limits

- In-process rebuild, not a process kill. The process-kill Workbench witness for ETL is the following leaf, once the
  Workbench harness used by the BPMN hosting tests is shared.
- SQLite only; the composite parents of the existing journey (Sequence, Flowchart, CompensationScope), no HTTP source
  rows. The leaf plan named a Root parent for the ETL wait case; the in-process journey has no Root shape, so that
  row is deferred to the process-kill leaf and the deviation is recorded on #2892.
- "Late result refused" is proved through the cancellation case only; a duplicate delivery after a consumed completion
  is covered by the adapter-level `EtlNativeResultDeliveryTests`, not re-proved across a restart here.
- Local run on 9 November 2026 (macOS, .NET SDK 10.0.402): 15 rows passed; the whole project passed 1,343 tests.

## Process-kill witness (production Workbench)

`RunEtlPendingParentResultCrashTests` in `Extensions/Etl/tests/Hosting/Tests` (tier `integration`, trait `child-process`,
shard `workbench-extensions-hosting`) is the process-kill counterpart, mirroring the #3998 BPMN witness. It starts the production
`Elsa.Workbench` as a child process through the shared `WorkbenchProcess` harness with the eight ETL features enabled in
its own copied `shells.json` over SQLite files, publishes the finite file pipeline and a native parent over the REST API
(Root `RunEtl`, Sequence and Flowchart), and lets the Workbench's own ETL worker run the pipeline.

Cut: the ETL run is terminal and its delivery lease is claimed by the running process, which is frozen just before the
parent resume by the `ELSA_HOLD_NATIVE_RESULT_DELIVERY` test hold in `EtlNativeResultExecutor` (the same seam shape #3998
added to `ParentResumeExecutor` and `BpmnEmbeddingDelivery`; production hosts never set the variable, and the seam writes
an `.engaged` marker so the test observes the frozen claim instead of guessing). The process is then killed with
`SIGKILL` (`Process.Kill(entireProcessTree: true)` on Windows) and a fresh process is started over the same content root.
Proof: the parent was `Running` with `RunEtl` `Suspended` and no incidents before the kill; the killed process exited
non-zero and the restarted process has a different id; after the restart the parent is `Completed`, exactly one `RunEtl`
activity is `Completed`, the following `WriteLine` ran once (Sequence, Flowchart), the single ETL run is `succeeded`, and
no second run exists. The hold is armed before the parent starts, so the worker cannot deliver ahead of the freeze.

Limits: completion only (fault and cancel across a process kill remain covered by the in-process witnesses above);
SQLite only; the restarted process recovers the claimed delivery when the dead owner's lease lapses, so each case takes
the delivery lease plus two Workbench starts. The fixture sets `DeliveryLeaseSeconds` to its floor of 5 (production default
30; the lease has no heartbeat while a delivery is in flight), against a 90-second post-restart deadline; each row takes
about 13 seconds locally.
