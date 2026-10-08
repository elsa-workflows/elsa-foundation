# Checkpoint, scheduler queue, and outbox accounting

This accepted Coalesced capture adds exact repository-method attribution to the preserved-before workload. The command total remains 420: 277 HTTP, 101 REST, and 42 separately labelled resumption-sweep command pairs. Of those same 420 pairs, 182 now join to an exact operation span; 238 still have no method ancestor. This is additional accounting evidence, not another database-access reduction or a measured saving.

The retained operation ledger has 146 complete operation pairs. It contains 38 checkpoint operations joined to 66 command pairs, 17 scheduler queue operations joined to 25 pairs, 11 post-commit outbox operations joined to 11 pairs, and 80 durable-value page reads joined to 80 pairs. The 66 checkpoint, queue, and outbox operations carry explicit valid method metadata. The 80 durable-value operations omit that metadata by design. Every method outcome remains unobserved: ending an instrumented scope does not establish that its method returned successfully.

| Operation family | Operation pairs | Exact command pairs | Explicit metadata |
|---|---:|---:|---:|
| Checkpoint: commit, marker lookup, participant stage, participant flush, marker flush | 38 | 66 | 38 |
| Scheduler queue: enqueue, dequeue, list, list claimable executions | 17 | 25 | 17 |
| Post-commit outbox: claim, list claimed | 11 | 11 | 11 |
| Durable-value page reads | 80 | 80 | absent by design |
| **Total admitted** | **146** | **182** | **66** |

| Checkpoint method | Operation pairs | Exact command pairs |
|---|---:|---:|
| Commit | 4 | 28 |
| Marker lookup | 4 | 4 |
| Participant stage | 22 | 26 |
| Participant flush | 4 | 4 |
| Marker flush | 4 | 4 |
| **Total checkpoint** | **38** | **66** |

The checkpoint scopes expose marker lookup, typed staging, participant flush, and marker flush separately. The participant input vectors are requested inputs; their counts are not written-row counts. A combined participant flush may cover several staged participants, so its SQL cannot be assigned to one participant or treated as a per-participant row count. Four marker lookups report missing with a returned count of zero. All method outcomes remain unobserved.

| Command group | Pairs | Exact method joins | Remaining without method ancestor |
|---|---:|---:|---:|
| HTTP request trace | 277 | 129 | 148 |
| REST control trace | 101 | 39 | 62 |
| Background resumption sweeps | 42 | 14 | 28 |
| **Total** | **420** | **182** | **238** |

The 42 background pairs come from seven resumption sweeps. Those global queue/outbox polls remain attributed to their sweep trace; the capture does not establish that they belong to or were awaited by the primary execution. Queue request and return identities are exported as separate typed HMAC sets. No item-to-execution mapping is inferred between those flat sets. Returned list identities are primary item identities, while participant input counts do not represent persisted rows.

The primary HTTP request returned 200, matched the expected body, completed with zero incidents, and has a terminal final read-only snapshot with zero queue items and zero outbox entries across all inspected statuses. The REST control independently returned 200, matched its expected output, completed with zero incidents, and has no separate settlement snapshot. The final HTTP snapshot is not REST settlement evidence.

The private raw probe is 2,561,423 bytes, below the existing 8 MiB collector cap. It has 1,361 records, contiguous sequence numbers, conserved tails, and no observer faults, overflow, or unmatched operations. The source candidate uses the preserved runtime baseline e6faa5689814c33353009b13f5b9e44d0e9982a4 and source revision b3f55bef32576011d39b93d664949a0cb705fb3b. All eight source overlay pins, 585 binary pins, and four fixture pins match their before/after manifests. The exact source, build, descriptor, probe, archive, and pin-manifest hashes are in the [allowlisted evidence packet](evidence/checkpoint-queue-accounting-2026-10-07.json).

The initial prelaunch attempt was rejected before a host or database started because its wrapper still expected five source overlays while the reviewed candidate contained eight. A one-line wrapper correction updated that count; the accepted source-candidate capture then launched the host once, made one HTTP request and one REST invocation, and had no retries. The wrapper correction did not repair product source. The failed prelaunch and accepted run retain their separate outcomes.

The evidence packet projects all 420 command pairs with exact operation linkage where observed, plus all 146 paired operations and their observed method metadata. It exposes only bounded category fields and typed HMACs; raw IDs, SQL text, parameters, payloads, bearer or credential tokens, and machine-local paths are excluded. The numeric monotonic claim-fence counter is included as non-secret lifecycle metadata. The raw probe SHA-256 is 3261099441a6ca5adcbf5e1c92965e1d7bccdf59d85c757e34050f1ccd7d4b88; the exported archive SHA-256 is 2a9d0434209f8694f194ce0efe33ab2236d6d9f07def068a625936f3f5a5e3f5. The JSON projection was derived from that retained capture and its reviewed receipts; producing this packet did not launch a host.

For a later candidate comparison, this packet supplies the checkpoint, queue, and outbox phase baseline within the same 420-pair Coalesced workload. It does not contain an after run or show a query reduction. The 238 commands without method ancestors remain at trace/boundary level, and two additional durable-value page-read operations remain excluded outside the target request/boundaries. T02 remains In Progress / Verification Running pending the other caller, comparison, and correctness gates.

## Immediate capture (7 October 2026)

The accepted bounded Immediate source-candidate capture adds a separate phase ledger for its HTTP request and REST control. It is preserved-before-source evidence: it does not measure a query reduction, elapsed-time improvement, or production behavior. The allowlisted [Immediate evidence packet](evidence/checkpoint-queue-immediate-2026-10-07.json) keeps this run separate from the Coalesced capture above.

| Command group | Command pairs | Exact method joins | Without method ancestor |
|---|---:|---:|---:|
| HTTP request | 635 | 392 | 243 |
| REST control | 556 | 345 | 211 |
| Six resumption sweeps | 36 | 12 | 24 |
| **Total** | **1,227** | **749** | **478** |

| Operation family | Operation pairs | Joined command pairs | Metadata observed |
|---|---:|---:|---:|
| Checkpoint | 333 | 397 | 333 |
| Scheduler queue | 140 | 246 | 140 |
| Post-commit outbox | 66 | 94 | 66 |
| Durable-value page reads | 12 | 12 | absent by design |
| **Total** | **551** | **749** | **539** |

All 293 checkpoint child spans identity-match their 40 commit parents. The ledger excludes two durable-value reads outside the target request and boundary, one queue poll, and one outbox claim that began after admission closed. The six global `scheduler_queue.list_claimable_executions` polls have no target-execution identity, so they remain assigned only to their sweep traces. Every method outcome is unknown; a disposed instrumentation scope does not show that its method returned successfully.

The HTTP request returned 200 with the expected body, completed with zero incidents, and ended with a terminal read-only snapshot showing zero queue items and 15 outbox rows in `Delivered` status, with zero in every other inspected status. The REST control returned 200 with the expected output and completed with zero incidents; it has no separate settlement snapshot. The HTTP snapshot is not evidence of REST settlement.

The probe is 8,963,801 bytes and 4,773 records under the prepared 16 MiB artifact cap. The host exited normally and the owned database container was removed. The temporary Azure resource group was deleted and confirmed absent after all captures and compiled host/tool artifacts were exported and hash-verified locally. Preparation included six matching guard cases and 42 portable synthetic controls; those controls did not exercise a total-artifact-byte boundary. The evidence packet records source, binary, fixture, descriptor, archive, and review pins, and exports typed HMAC identities without raw IDs, SQL text, parameters, credentials, or machine-local paths. Participant input counts remain requested inputs, not persisted-row counts; combined flush commands are not divided among participants.

A prior Immediate capture had 1,243 command pairs (635 HTTP, 559 REST, 49 sweep pairs); this capture has 1,227 (635, 556, and 36). Separate-capture variation has no causal or savings attribution. The existing M2 reduction of 83 HTTP SELECTs (277 to 194) is separate and is not counted here. The public packet SHA-256 is `02b23c01d38d0e14a4e4217f11085d93d26b5b7b751ee940678a0a2cb5dc24a4`.

## Residual source review

The [joined request/settlement report](joined-request-settlement-accounting.md) imports both packets into the shared T02/T04 ledger. It adds operation-local typed identity joins, reconciles drain tails and natural sweeps, and states the missing response-finished boundary and unresolved historical range explicitly.

The [remaining-command account](residual-accounting.md) conserves all 238 Coalesced and 478 Immediate commands without method ancestry across 45 disjoint groups and 13 source-supported families. It records exact-caller limits, the root lead's bounded dispositions, and the existing T04/T07/T18 ownership and revisit conditions. Those source candidates do not add observed method joins, turn participant inputs into rows, or claim new savings. The full program's reduction selection and integrated correctness/final measurement gates remain open.
