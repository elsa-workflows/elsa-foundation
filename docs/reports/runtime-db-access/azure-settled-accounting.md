# Bounded Azure settlement accounting

**Accepted scope:** one successful Coalesced HttpEndpoint request, one valid REST-start control, a durable settlement snapshot for the primary execution, and six subsequent natural resumption sweeps. Root reviewed the retained artifacts and an independent reconstruction of their lifecycle ledger. T02 remains In Progress/Running; T04 remains Blocked/Pending because exact caller, checkpoint-participant and work-item/outbox attribution are not complete. This is diagnostic accounting on the preserved before source, not a timing comparison or final integrated acceptance.

The [sanitized evidence](evidence/azure-settled-accounting-2026-10-06.json) contains counts, identity hashes, source pins, finite verification results and the acceptance boundary. Private raw files include disposable database credentials and are retained outside the repository.

## Scenario and source

The run began on 6 October 2026 at 22:12:22.547 UTC on the owner-approved isolated Skywalker ISP Azure runner: Ubuntu 24.04 x64, eight vCPUs, 32 GiB RAM, .NET 10.0.12 and PostgreSQL 16.15. It used the [reviewed deterministic fixture and companion](workload-reproduction-plan.md), with Alice Smith input and expected output. Each was invoked once, without an in-run retry. Both returned HTTP 200, expected body/output, Completed state, zero incidents, and effective Coalesced/50/boundary-level settings.

The runtime baseline is `e6faa5689814c33353009b13f5b9e44d0e9982a4`; diagnostic revision `b3f55bef32576011d39b93d664949a0cb705fb3b` plus the three pinned private source overlays supplies request-drain and resumption-sweep boundaries. Overlay manifest SHA-256 is `eb6d628f8baff10cac49ebe06aa20c37efa4392b96660a983ed306dcca9d217e`. Fixtures come from `6f3a07e24df4d2baf73dec7e52f501db55e755d9`. These pins identify a preserved before-source observation, not current main or an exact reproduction of the historical transform.

The observation cutoff was 90 seconds after harness start, followed by a two-second terminal-only close window. The inner harness completed in 95.827 seconds and the outer guard in 95.922 seconds, including setup and cleanup. These durations are harness execution bounds, not workflow latency samples. Both receipts passed; all 589 pre/post binary pins, source overlays and fixture pins matched. Root independently verified that the 24 owned process groups and exact disposable PostgreSQL container were absent after cleanup. No forced host kill occurred.

## What the database counts mean

An EF command start and matching terminal form one command execution. A command can contain multiple SQL statements or touch several tables. The counts below therefore do not establish statement totals, network round trips, rows read, checkpoints or exclusive execution ownership. See the [existing source/query map](source-query-map.md) for source-confirmed mechanisms and the [EF identity ledger](ef-identity-accounting.md) for the shared taxonomy.

| Trace | Runtime PostgreSQL | Diagnostics SQLite | Placement SQLite | IAM SQLite | Total EF commands |
|---|---:|---:|---:|---:|---:|
| HttpEndpoint | 195 | 80 | 2 | 0 | 277 |
| REST companion | 82 | 16 | 2 | 1 | 101 |

The collector observed 420 command executions in its selected request and sweep scopes: 277 HTTP + 101 REST + 42 across seven resumption sweeps. This is not a census of every host command. Independent reconstruction matched all 420 starts and terminals by command identity, occurrence and execution method, with no duplicate, unmatched, failed or metadata-mismatched pairs. It also matched 54 SaveChanges pairs and 16 transaction generations. The 96 transaction logger callbacks include commit and savepoint events; they are not 96 transactions.

| Source slice | Commands | SaveChanges pairs | Transaction generations |
|---|---:|---:|---:|
| HTTP drain at method stop | 218 | 27 | 8 |
| HTTP descendants after method stop | 40 | 10 | 5 |
| HTTP trace outside that source boundary | 19 | 4 | 0 |
| REST drain at method stop | 69 | 5 | 1 |
| REST descendants after method stop | 16 | 4 | 2 |
| REST trace outside that source boundary | 16 | 4 | 0 |

All 40 HTTP and 16 REST descendant-tail commands used the diagnostics SQLite context. The HTTP drain at method stop contained 178 runtime PostgreSQL and 40 diagnostics SQLite commands; the REST drain contained 69 runtime PostgreSQL commands. The outside-boundary groups contain the remaining runtime reads/writes and placement/IAM commands. Their exact repository callers remain unresolved.

Method stop is not the HTTP response-finished boundary. Exact Activity-object ancestry identifies observed descendants, but does not prove that the caller awaited them or exclusively owned their work. The original method-stop receipt remains immutable; later callbacks are accounted for separately through the cutoff and terminal close window. All nine observed method boundaries had conserved, closed lifecycles at finalization. Earlier packets show different diagnostic splits around method stop despite equal trace totals; those splits are not a performance comparison.

## What settlement and background work establish

The read-only repeatable-read PostgreSQL snapshot identifies the primary execution as Completed and terminal, with zero scheduler items and no outbox items. All six known outbox-status counts are zero. Snapshot, primary execution HMAC, request-drain receipt, live-context receipt and settlement marker match. This snapshot concerns the primary execution at that instant; REST terminal readback is a separate control and does not constitute a second durable-settlement snapshot.

One resumption sweep preceded the settlement marker. Six subsequent sweeps began and completed after it. Every sweep performed six PostgreSQL SELECT command executions:

| Table token | Commands per sweep |
|---|---:|
| `elsa_runtime_execution_liveness_state` | 4 |
| `elsa_runtime_scheduler_work_item` | 1 |
| `elsa_runtime_post_commit_outbox` | 1 |

Each sweep had no observed SaveChanges, transaction generation, descendant tail or outstanding lifecycle at finalization. No resumption-candidate event was observed. This directly shows recurring reads after the primary workflow has reached the defined settled condition. It does not identify returned rows as belonging to that workflow or explain the historical 25–695 untraced commands. Source-bound sweep activity and diagnostics tails are separate observed categories; the historical remainder stays unresolved under T04.

## Failure history and acceptance limits

The first twelve normal Azure captures remain failed and excluded. The twelfth reached the full observation window and produced valid collector/snapshot evidence, but its final stage receipt exceeded the harness's 16 KiB cap: the validated wrapper was 20,239 bytes. Root and independent review confirmed that exact source failure. A prospective correction allows up to 64 KiB only for the collector-stage receipt, retaining the 16 KiB default for other stages and all overwrite/size rejection checks. Nine finite controls passed, including the old method rejecting the retained shape and the new method accepting it. The thirteenth fresh run is the accepted packet; no earlier receipt was rewritten or promoted.

Other preparatory fixes and their failed attempts remain in the [T02 issue history](https://github.com/elsa-workflows/elsa-foundation/issues/2386). Successful harness corrections are not runtime query reductions, latency gains or causal repairs of errors whose original evidence was unavailable.

The accepted packet narrows the accounting gap; it does not close these remaining outcomes:

- The equivalent finite settled observation for Immediate and final candidate accounting remain to be completed.
- Exact repository-caller, checkpoint-participant/marker and work-item/outbox identity joins remain distinct from command/context/Activity ancestry.
- The historical untraced range, original transform cost and isolated valid-input 202 are not reproduced or explained by this packet.
- Comparable low-logging before/after measurements, concurrent correctness and changed-contract interruption/replay verification remain with T17/T18. The [seven earlier before timing cases](before-timing.md) retain their own qualifications.

The already delivered M2 pagination correction has separate, qualified 277-to-194 command evidence. The repeated 277 here identifies the preserved before scenario; it supplies no new gain. Remaining reduction choices continue through their reviewed materialization, cadence, checkpoint and scheduler/outbox spikes. No global default, durability rule or program acceptance criterion changes.

## Retained evidence fingerprints

| Artifact | SHA-256 |
|---|---|
| Private archive, 125 files | `2f8edcce315bd379002b4ac1c2db4fcdddd8ba126d7202e038341de8804f1c12` |
| Raw collector | `5be673111f244f731e2b68aeccac58f2992825d428a4adfa9ec13e0defbb162d` |
| Inner final receipt | `f2c998b455a321cddbfda9e97e39bf4223daef4ed3b19528351731e3d9f6a8ba` |
| Outer guard receipt | `102e969e708034678044962a77f58501548fbfabcbd2099a9483db0e4b64276c` |
| Primary settlement snapshot | `a76fc9928ab54e023f16f161e416ef310c393148f6fa0bd9bf6cbd72621c1226` |
| Source/byte preflight receipt | `72eec935dd21f706d7567dddc690a87255ee14ed9f1c4c13991830ad353cfd2e` |

The raw machine receipt retains its `passed_source_candidate` label and pending-publication boilerplate. Root acceptance is a separate, bounded review recorded in the sanitized evidence; it does not alter the raw receipt or imply completion of T02, T04 or the program.
