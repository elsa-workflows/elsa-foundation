# Evidence: Finite JSON Lines ETL input

Every receipt names the source it ran on. Limits are stated with each one.

## Actual Workbench journey (SC-006, US5)

The driver is [`jsonl_workbench_journey.py`](jsonl_workbench_journey.py). It ran twice, with Python 3.14 and .NET SDK 10.0.402, each time on a clean tree and a Debug Workbench built from that commit:

- Run 1 at `9b7c6df4d`, on an ephemeral port.
- Run 2 at `724bb5c00`, which only adds the driver's `--port` and `--studio-origin` options, on owned port 5391 so a Studio could attach. Its summary records that revision.

Both runs passed every check; only run 2 kept its summary. The journey was not re-run after the main merge (`0ee760f35`) or the review fixes (`c51023ed8`: record-buffer bound, exception rename; `657b5cd9f` and later: tests, a comment and a Studio component rename), which the unit, worker and process tests cover at head.

The driver:

- copies the Workbench settings into a private root;
- approves two JSON Lines source files and per-case output folders;
- starts its own Workbench on a loopback port;
- publishes two pipelines through the design API;
- runs each from a native Flowchart whose `RunEtl` connects `Done` and `BusinessError` to different `WriteLine` nodes.

It stops only the process it started. Run 2's derived summary is [`workbench-journey-summary.json`](workbench-journey-summary.json); the host URL and process id are removed from it.

| Case | Input | Native outcome | Run | Partitions |
|---|---|---|---|---|
| clean | BOM, a blank line, CRLF, unterminated last line, 4 records | `Completed` through `Done` only | `succeeded`, 4 accepted, outputs Ada/Bram/Cleo/Dee | `[1,3)` and `[3,5)`, both at their end |
| dirty | 7 lines: Ada first and Eve last, with one each of invalid JSON, non-object, wrong type, missing required and duplicate property between them | `Completed` through `BusinessError` only | `businessError` / `etl.rows-rejected`, 2 accepted, 5 rejected | `[1,5)` and `[5,8)`, both at their end |

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
- [`studio/live-jsonl-runs.jpg`](studio/live-jsonl-runs.jpg) and [`studio/live-jsonl-run-inspection.jpg`](studio/live-jsonl-run-inspection.jpg): the runs list, and the top of the dirty run's inspection (7 source records, 2 accepted, 5 rejected, nothing outstanding).
- [`studio/live-jsonl-run-inspection.txt`](studio/live-jsonl-run-inspection.txt): the same inspection's full page text, captured from the browser. It shows **Source type: JSON Lines**, the retained source identity and both file partitions (`File range 1–5 (next 5)`, `File range 5–8 (next 8)`), which lie below the screenshot's fold.

Both owned processes were stopped afterwards. The owner's own Workbench on 5095 was not touched.

The fixture-backed Playwright spec (`Studio/tests/browser/etl-connector-authoring.spec.ts`) adds JSON Lines authoring at 320px and 1024px with no axe violations. Its screenshots are [`studio/1190-studio-jsonl-source-320.png`](studio/1190-studio-jsonl-source-320.png) and [`studio/1190-studio-jsonl-source-1024.png`](studio/1190-studio-jsonl-source-1024.png).

## Worker-process recovery (SC-003, US3)

`EtlRuntimeJourneyTests.Two_jsonl_worker_processes_recover_an_owed_partition_without_duplicate_effects` runs real child worker processes (`Elsa.Etl.WorkerHost`) over one SQLite runtime store. For each of `AfterReservation` and `BeforeAcceptance` (after physical publication of every route the owed batch produced, asserted per route), it:

1. kills the predecessor;
2. waits for its lease to expire;
3. starts a replacement process.

The replacement:

- takes the same partition under a higher fence, after which the killed owner's lease is refused with `LeaseLost`;
- rebuilds the identical batch descriptor and digest;
- keeps the retained output;
- leaves the published main file set unchanged (no second physical file);
- finishes with 2 accepted, 1 rejected and 1 filtered, and zero outstanding.

`Cancelling_a_jsonl_run_with_owed_work_settles_once_and_stops_progress` cancels with an owed batch. It verifies that cancellation settles once (one terminal row and one outbox row), that the stale lease cannot accept, and that no partition can be claimed or file published.

## Bite-proofs (SC-002, SC-004)

[`bite-proofs.json`](bite-proofs.json) records eight mutations at `c51023ed8` and two more (marked round 3) on the round-3 test change, with each failing test named. For each, the focused tests failed with the mutation in place and passed again once the file was restored from git:

| Mutation | Tests failed while mutated |
|---|---|
| Indexer emits blank lines as records | 3 of 11 indexer tests |
| Bad lines fail the batch instead of routing | 1 of 1 |
| Zero-record settlement removed (FR-011) | 5 of 5 (store CSV and JSON Lines, worker empty, blank-only and header-only CSV) |
| R10 span check disabled | 2 of 5 forged-index cases |
| R10 gap check disabled | 1 of 5 |
| R10 line-start check disabled | 1 of 5 |
| Record limit off by one (`> max + 1`) | 1 of 1 (the max+1 line parses instead of being too large) |
| Record ceiling off by one (`>=`) | 1 of 1 (exactly-at-ceiling preparation refused) |
| JSON Lines parse allows trailing commas | 1 of 1 |
| JSON Lines parse skips comments | 1 of 1 |

Disabling the span check fails only 2 of its 3 targeted cases: the third forged index is still refused, by the gap check, so that test stays green.

## Whole-suite checks

Run locally; CI covers the exact head:

- `Elsa.Etl.Tests` on `f1b85fa0c`'s code (rerun during review round 4): 1060/1060 passed, 0 skipped; `--list-tests` gives 1060; 189 s wall time on a loaded shared machine; architecture guard 609/609, maps check and catalog links pass on the same code. Earlier: 1060/1060 at `657b5cd9f`, 1058/1058 at `c51023ed8`, 1056/1056 at `7ecec65ad`.
- The golden-hash theory `EtlV2ArtifactIdentityTests.Persisted_v2_artifacts_keep_their_canonical_hash_per_source_kind`, copied unchanged onto an untouched `origin/main` checkout (`eb2df8548`), passes there (3/3 in the class), so the pinned CSV and HTTP JSON hashes are main's, not values captured on this branch.
- Architecture guard at `7ecec65ad`: 609/609.
- `Elsa.Maps.Generator -- check` and `test-tiers-check`: pass.
- Studio ETL client: typecheck clean; Vitest 70/70.
- Playwright `etl-connector-authoring`: 6/6.

The project's manifest entry is PR tier, `child-process`, median 1.83 min, from CI timing. The new child-process journeys add roughly 30 s of local wall time. If CI's median for the project reaches 2 minutes, the §E7 tier rule moves it to the integration tier; that is decided from CI timing, not from this machine.

## Artifact reviews

Independent reviews ran before implementation. Each finding was adopted, and none was discarded.

- **Spec, against the code at `3adffdfe1`:** one blocker and ten other findings. The blocker was that header-only v2 CSV runs never settle (FR-011).
- **Plan and tasks:** ten should-fix findings and three nits, among them the preparer gates, the shared reader's size check and ordinals, the settlement placement, replacement ordering, forged-hash index tests and the R4 exception.

PR review rounds 1–3 used three independent lenses (correctness, standards and spec conformance) and stopped at their cap unconverged. Round 4 re-reviewed the round-3 fixes: correctness found nothing; standards and spec found no code issues but four documentation inaccuracies. The fixes to this file and `tasks.md` landed through the stacked PR #3668, because #3657 was already in auto-merge; the gate-comment inaccuracy is corrected in #3657's round-4 comment. Findings and outcomes are recorded in the PR comments. The round-3 effect-count assertion (`(AcceptedMainEffects, AcceptedErrorEffects) == (2, 1)`) is tested but not bite-proofed.

## Not covered here

- CSV delivery.
- Continuous runs.
- SQL and RabbitMQ connectors.
- BPMN invocation of a JSON Lines pipeline.
- ETL store qualification on providers other than SQLite.
- Repair of v2 CSV runs that are already stuck in `Running` from before FR-011.

These stay with #2908.
