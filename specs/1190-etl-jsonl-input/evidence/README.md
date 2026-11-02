# Evidence: Finite JSON Lines ETL input

Every receipt names the source it ran on. Limits are stated with each one.

## Actual Workbench journey (SC-006, US5)

The driver is [`jsonl_workbench_journey.py`](jsonl_workbench_journey.py). It ran at source `9b7c6df4d` (clean tree), on a Workbench built from that commit (Debug, .NET SDK 10.0.402), with Python 3.14.

The driver:

- copies the Workbench settings into a private root;
- approves two JSON Lines source files and per-case output folders;
- starts its own Workbench on a loopback port (ephemeral for run 1; owned port 5391 for run 2, so a Studio could attach);
- publishes two pipelines through the design API;
- runs each from a native Flowchart whose `RunEtl` connects `Done` and `BusinessError` to different `WriteLine` nodes.

It stops only the process it started. Run 2's derived summary is [`workbench-journey-summary.json`](workbench-journey-summary.json); the host URL and process id are removed from it.

| Case | Input | Native outcome | Run | Partitions |
|---|---|---|---|---|
| clean | BOM, a blank line, CRLF, unterminated last line, 4 records | `Completed` through `Done` only | `succeeded`, 4 accepted, outputs Ada/Bram/Cleo/Dee | `[1,3)` and `[3,5)`, both at their end |
| dirty | 7 lines: 2 valid, then one each of invalid JSON, non-object, wrong type, missing required, duplicate property | `Completed` through `BusinessError` only | `businessError` / `etl.rows-rejected`, 2 accepted, 5 rejected | `[1,5)` and `[5,8)`, both at their end |

Further results from both runs:

- **Dirty-case error records:** each carries its file ordinal and code: `etl.jsonl.record-invalid` at ordinals 2 and 6, `etl.jsonl.record-not-object` at 3, `etl.invalid-value` (`id`) at 4, and `etl.required-value-missing` (`id`) at 5. No source text appears.
- **Inspection:** reports source kind `jsonl` and the safe source input (revision, pinned digest, snapshot digest and length). It contains no host path.
- **Accounting:** zero outstanding records and bytes for both runs.
- **Binding catalog:** the source file approvals are still listed under kind `csv` (research R2).

**Limits:**

- This journey does not kill the host; kill-point recovery is proved in worker processes (below).
- It covers the SQLite runtime and design stores only.

## Real browser against the owned hosts (SC-005, US4)

During run 2, an owned `Elsa.Studio.Web` (built from the same commit) ran on 127.0.0.1:5392 against the owned Workbench. The built-in browser signed in with the local test account.

- [`studio/live-jsonl-draft-source.jpg`](studio/live-jsonl-draft-source.jpg): the saved definition opens with the **JSON Lines file** source kind, the source file binding and its two JSON Pointer fields.
- [`studio/live-jsonl-runs.jpg`](studio/live-jsonl-runs.jpg) and [`studio/live-jsonl-run-inspection.jpg`](studio/live-jsonl-run-inspection.jpg): the runs list, and the dirty run's inspection. The inspection shows **Source type: JSON Lines**, 7 source records, 2 accepted and 5 rejected, the retained source identity, and both file partitions (`File range 1–5 (next 5)`, `File range 5–8 (next 8)`).

Both owned processes were stopped afterwards. The owner's own Workbench on 5095 was not touched.

The fixture-backed Playwright spec (`Studio/tests/browser/etl-connector-authoring.spec.ts`) adds JSON Lines authoring at 320px and 1024px with no axe violations. Its screenshots are [`studio/1190-studio-jsonl-source-320.png`](studio/1190-studio-jsonl-source-320.png) and [`studio/1190-studio-jsonl-source-1024.png`](studio/1190-studio-jsonl-source-1024.png).

## Worker-process recovery (SC-003, US3)

`EtlRuntimeJourneyTests.Two_jsonl_worker_processes_recover_an_owed_partition_without_duplicate_effects` runs real child worker processes (`Elsa.Etl.WorkerHost`) over one SQLite runtime store. For each of `AfterReservation` and `BeforeAcceptance` (after physical main and error publication), it:

1. kills the predecessor;
2. waits for its lease to expire;
3. starts a replacement process.

The replacement:

- takes the same partition under a higher fence;
- rebuilds the identical batch descriptor and digest;
- keeps the retained output;
- leaves the published main file set unchanged (no second physical file);
- finishes with 2 accepted, 1 rejected and 1 filtered, and zero outstanding.

`Cancelling_a_jsonl_run_with_owed_work_settles_once_and_stops_progress` cancels with an owed batch. It verifies that cancellation settles once (one terminal row and one outbox row), that the stale lease cannot accept, and that no partition can be claimed or file published.

## Bite-proofs (SC-002, SC-004)

[`bite-proofs.json`](bite-proofs.json) records six mutations. For each, the focused tests failed with the mutation in place and passed again once the file was restored from git:

| Mutation | Tests failed while mutated |
|---|---|
| Indexer emits blank lines as records | 3 of 11 framing cases |
| Bad lines fail the batch instead of routing | 1 of 1 |
| Zero-record settlement removed (FR-011) | 5 of 5 (store CSV and JSON Lines, worker empty, blank-only and header-only CSV) |
| R10 span check disabled | 2 of 5 forged-index cases |
| R10 gap check disabled | 1 of 5 |
| R10 line-start check disabled | 1 of 5 |

Disabling the span check leaves its third case red: the gap check still refuses that forged index.

## Not covered here

- CSV delivery.
- Continuous runs.
- SQL and RabbitMQ connectors.
- BPMN invocation of a JSON Lines pipeline.
- ETL store qualification on providers other than SQLite.
- Repair of v2 CSV runs that are already stuck in `Running` from before FR-011.

These stay with #2908.
