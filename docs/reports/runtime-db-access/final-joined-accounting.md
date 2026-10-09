# Final request, protocol and settlement accounting

**Evidence scope: 9 October 2026.** Four separately pinned diagnostic cases passed root raw reconciliation and independent review with the attribution limits below. The [machine-readable projection](evidence/final-joined-accounting-2026-10-09.json) preserves exact source, artifact and review pins. The [clean-source command comparison](final-clean-ef-accounting.md), [low-logging timing](final-timing.md) and [final response](delivery-response.md) carry their own acceptance boundaries.

## Case-level results

| Case | Source | Cadence | EF command pairs (method joined / residual) | Global CommandComplete | Global query / Sync cycles | Settled snapshot |
|---|---|---:|---:|---:|---:|---|
| immediate-final-7357 | 7357b91e012c | Immediate | 1,221 (747 / 474) | 5,290 | 1,869 / 3,012 | Completed, terminal, queue 0, outbox 15 delivered / 15 rows |
| coalesced-final-7357 | 7357b91e012c | Coalesced | 243 (84 / 159) | 3,594 | 1,221 / 2,052 | Completed, terminal, queue 0, outbox 0 delivered / 0 rows |
| immediate-before-e6 | e6faa5689814 | Immediate | 1,227 (749 / 478) | 5,297 | 1,872 / 3,016 | Completed, terminal, queue 0, outbox 15 delivered / 15 rows |
| coalesced-before-e6 | e6faa5689814 | Coalesced | 414 (180 / 234) | 3,890 | 1,361 / 2,202 | Completed, terminal, queue 0, outbox 0 delivered / 0 rows |

The table keeps two units separate: EF command pairs are provider attempts; PostgreSQL CommandComplete messages are successful protocol statements. Query and Sync cycles are additional global protocol counts. All PostgreSQL totals include capture setup, HTTP/REST requests and background activity, so they are not request-only SQL totals.

## Method attribution and residual query nature

Exact operation-ancestor joins, checkpoint operations and scheduler/dispatch operations are grouped by trace and safe operation family in the JSON. The EF residuals have no exact operation-method ancestor. Their parsed public table identifiers are grouped by prefix into telemetry (`elsa_otel_*`), runtime state (`elsa_runtime_*`), placement (`elsa_distributed_*`), identity (`identity_*`) and other/unmapped families. All 19 distinct parsed identifiers were found in C# source at both pinned revisions (7357b91e and e6faa568). This source-name check supports the identifiers and family labels; it does not identify a caller or method for any residual command. The JSON preserves per-trace table identifier, provider family and leading-verb counts without SQL text.

| Case | Trace | Residual pairs | Telemetry | Runtime state | Placement | Identity | Other/unmapped |
|---|---|---:|---:|---:|---:|---:|---:|
| immediate-final-7357 | HTTP | 243 | 128 | 113 | 2 | 0 | 0 |
| immediate-final-7357 | REST | 211 | 112 | 96 | 2 | 1 | 0 |
| immediate-final-7357 | background_or_unassigned | 20 | 0 | 20 | 0 | 0 | 0 |
| coalesced-final-7357 | HTTP | 87 | 48 | 37 | 2 | 0 | 0 |
| coalesced-final-7357 | REST | 48 | 16 | 29 | 2 | 1 | 0 |
| coalesced-final-7357 | background_or_unassigned | 24 | 0 | 21 | 3 | 0 | 0 |
| immediate-before-e6 | HTTP | 243 | 128 | 113 | 2 | 0 | 0 |
| immediate-before-e6 | REST | 211 | 112 | 96 | 2 | 1 | 0 |
| immediate-before-e6 | background_or_unassigned | 24 | 0 | 24 | 0 | 0 | 0 |
| coalesced-before-e6 | HTTP | 148 | 80 | 66 | 2 | 0 | 0 |
| coalesced-before-e6 | REST | 62 | 16 | 43 | 2 | 1 | 0 |
| coalesced-before-e6 | background_or_unassigned | 24 | 0 | 24 | 0 | 0 | 0 |

These are EF provider-command residuals, not PostgreSQL protocol statements or affected rows. Exact method ancestry is reported separately; a family label inferred from the SQL parser table token does not supply missing caller attribution. Background/unassigned residuals remain in the JSON and are not allocated to HTTP or REST.

## Observed checkpoint, queue and dispatch operations

The counts below are paired safe operation metadata observations from the same run. They are not provider-command pairs, successful writes, durable checkpoint records, queue-row counts or completed dispatches. Exact EF command joins by operation remain separately identified in the JSON.

| Case | Trace | Checkpoint operation pairs | Scheduler queue/dispatch operation pairs |
|---|---|---|---|
| immediate-final-7357 | HTTP | 180 (checkpoint.commit=22; checkpoint.marker_flush=22; checkpoint.marker_lookup=22; checkpoint.participant_flush=22; checkpoint.participant_stage=92) | 71 (scheduler_queue.claim=37; scheduler_queue.complete_claim=12; scheduler_queue.enqueue=22) |
| immediate-final-7357 | REST | 153 (checkpoint.commit=18; checkpoint.marker_flush=18; checkpoint.marker_lookup=18; checkpoint.participant_flush=18; checkpoint.participant_stage=81) | 63 (scheduler_queue.claim=33; scheduler_queue.complete_claim=10; scheduler_queue.enqueue=20) |
| coalesced-final-7357 | HTTP | 19 (checkpoint.commit=2; checkpoint.marker_flush=2; checkpoint.marker_lookup=2; checkpoint.participant_flush=2; checkpoint.participant_stage=11) | 6 (scheduler_queue.delete=2; scheduler_queue.enqueue=2; scheduler_queue.list=2) |
| coalesced-final-7357 | REST | 10 (checkpoint.commit=1; checkpoint.marker_flush=1; checkpoint.marker_lookup=1; checkpoint.participant_flush=1; checkpoint.participant_stage=6) | 3 (scheduler_queue.delete=1; scheduler_queue.enqueue=1; scheduler_queue.list=1) |
| immediate-before-e6 | HTTP | 180 (checkpoint.commit=22; checkpoint.marker_flush=22; checkpoint.marker_lookup=22; checkpoint.participant_flush=22; checkpoint.participant_stage=92) | 71 (scheduler_queue.claim=37; scheduler_queue.complete_claim=12; scheduler_queue.enqueue=22) |
| immediate-before-e6 | REST | 153 (checkpoint.commit=18; checkpoint.marker_flush=18; checkpoint.marker_lookup=18; checkpoint.participant_flush=18; checkpoint.participant_stage=81) | 63 (scheduler_queue.claim=33; scheduler_queue.complete_claim=10; scheduler_queue.enqueue=20) |
| coalesced-before-e6 | HTTP | 28 (checkpoint.commit=3; checkpoint.marker_flush=3; checkpoint.marker_lookup=3; checkpoint.participant_flush=3; checkpoint.participant_stage=16) | 7 (scheduler_queue.dequeue=3; scheduler_queue.enqueue=3; scheduler_queue.list=1) |
| coalesced-before-e6 | REST | 10 (checkpoint.commit=1; checkpoint.marker_flush=1; checkpoint.marker_lookup=1; checkpoint.participant_flush=1; checkpoint.participant_stage=6) | 3 (scheduler_queue.dequeue=1; scheduler_queue.enqueue=1; scheduler_queue.list=1) |

Any background or unassigned scheduler/dispatch operation pairs stay separately visible in the JSON; they are not assigned to either request trace.

## Direct retained PostgreSQL checkpoint proof

After the captures, root reopened only the four retained PostgreSQL containers and ran bounded repeatable-read, read-only queries with the workflow hosts stopped. Each PostgreSQL system identity matched its original capture. Root and independent review reconciled all eight execution identities and **87 immutable checkpoint marker rows**. Every execution has one Completed state row; every marker passed schema/revision, execution-identity and commit-presence checks. The query checks do not reimplement the complete store fingerprint/content validation contract. [Sanitized readback, query/wrapper/result hashes and limitations](evidence/final-retained-postgres-2026-10-09.json).

| Cadence | Request | Before markers | After markers | Current Delivered outbox before / after |
|---|---|---:|---:|---:|
| Immediate | HTTP | 22 | 22 | 15 / 15 |
| Immediate | REST | 18 | 18 | 13 / 13 |
| Coalesced | HTTP | 3 | 2 | 0 / 0 |
| Coalesced | REST | 1 | 1 | 0 / 0 |

These are persisted checkpoint markers, independently corroborating the observed commit-operation counts. The [pinned EF writer](https://github.com/elsa-workflows/elsa-foundation/blob/7357b91e012c56cb34f1259ef7a60ef56e144afe/src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs) flushes participants, adds the immutable marker and commits their shared transaction. The source-defined marker therefore supplies durable commit evidence; SaveChanges or transaction event counts alone do not.

All eight targets have zero residual scheduler rows and zero surviving workflow-dispatch rows. The latter does not prove zero historical dispatches; retained state cannot reconstruct deleted history. Marker arrays contain 56 pending-work references and 40 consumed-work references across the four cases, but the query returns array lengths rather than logical IDs, so these sums are not deduplicated historical totals. All containers were stopped afterward and the Azure VM was confirmed deallocated. Existing databases and disks remain preserved. No new workflow invocation was made.

## EF transaction lifecycle observations

EF transaction and savepoint log-event occurrences are shown by trace. Start/commit and savepoint phase event counts are separate telemetry dimensions and are not deduplicated into a transaction count. The method-stop counters are an independent boundary readback.

| Case | Trace | Transaction log events (Starting / Started / Committing / Committed) | Savepoint log events (Creating / Created / Releasing / Released) |
|---|---|---:|---:|
| immediate-final-7357 | HTTP | 38 / 38 / 38 / 38 | 44 / 44 / 44 / 44 |
| immediate-final-7357 | REST | 32 / 32 / 32 / 32 | 36 / 36 / 36 / 36 |
| coalesced-final-7357 | HTTP | 8 / 8 / 8 / 8 | 4 / 4 / 4 / 4 |
| coalesced-final-7357 | REST | 3 / 3 / 3 / 3 | 2 / 2 / 2 / 2 |
| immediate-before-e6 | HTTP | 38 / 38 / 38 / 38 | 44 / 44 / 44 / 44 |
| immediate-before-e6 | REST | 32 / 32 / 32 / 32 | 36 / 36 / 36 / 36 |
| coalesced-before-e6 | HTTP | 13 / 13 / 13 / 13 | 6 / 6 / 6 / 6 |
| coalesced-before-e6 | REST | 3 / 3 / 3 / 3 | 2 / 2 / 2 / 2 |

| Case | Method-stop transactions started / completed / in flight |
|---|---:|
| immediate-final-7357 | 37 / 37 / 0 |
| coalesced-final-7357 | 8 / 8 / 0 |
| immediate-before-e6 | 38 / 37 / 1 |
| coalesced-before-e6 | 9 / 9 / 0 |

Immediate-before-e6 has one transaction still in flight at method stop (38 started, 37 completed); final-tail accounting later conserves the captured tails. The method-stop counts and per-trace EF log-event occurrences are distinct measures.

## Client response and settlement boundaries

The response boundary is when the fixture client received the response, not when the server finished writing it. HTTP-trace EF command pairs relative to that client boundary were:

| Case | Completed before receipt | Started after receipt | Calibration-overlap |
|---|---:|---:|---:|
| immediate-final-7357 | 635 | 0 | 0 |
| coalesced-final-7357 | 134 | 0 | 0 |
| immediate-before-e6 | 635 | 0 | 0 |
| coalesced-before-e6 | 263 | 8 | 6 |

At the later snapshot and settlement-marker boundaries, all HTTP-trace EF pairs were before the boundary: 635 in Immediate-final, 134 in Coalesced-final, 635 in Immediate-before-e6, and 277 in Coalesced-before-e6. The Coalesced-before case has six response-boundary pairs inside the doubled calibration uncertainty band; they remain ambiguous. Every case preserves before/after/overlap buckets for all traces and for capture-wide protocol statements and query/Sync cycles in the JSON.

The separate root protocol-error readback reports four ErrorResponse frames per case, each definitely before its primary request began (minimum lead 11.510–11.785 seconds). This is temporal classification only; no SQL cause or workflow failure is inferred. The bounded analyzer leaves ErrorResponse phase/time unclassified; the separate readback is pinned in the JSON.

## Settled state, observation, and ownership limits

Each HTTP and REST control ran once, returned 200, observed Completed and zero incidents, and matched its expected output. Each v9 observation window lasted at least 12 seconds with zero retries, no extra requests, and `sweepCompletionClaimed=false`. All point snapshots are Completed/terminal with queue count zero and the expected outbox state: 15 Delivered rows for Immediate, zero rows for Coalesced. Marker-to-snapshot and request-drain identity/tick joins passed.

Global sweep boundaries and their tails remain unowned. No target candidate/action rows were linked by the conservative boundary/span and execution-hash join. A sweep-start marker is not a queue read; a marker absent at an earlier point does not prove the queue was nonzero. No packet gives a direct residual-row purge count, so no purge causality or performance-reduction claim is supported. Validated HTTP/REST trace associations permit scoped EF provider-attempt counts, but those counts are not cost measurements. Capture-wide PostgreSQL protocol totals and global sweeps remain unowned and cannot be assigned as per-request costs.

At request-drain method stop, command and SaveChanges pairs balance with zero in-flight pairs in all four cases. The Immediate-before-e6 packet has one transaction generation still in flight at stop (38 started, 37 completed); final tail accounting later reports complete, conserved tails. Tail ownership is `unknown_not_awaited` where recorded, with post-stop callbacks visible in the JSON.

## Provenance and historical boundary

The JSON pins each case archive, descriptor, guard and final receipt, raw probe, analyzer readback, helper sources/binaries, workbench binary/closure, source revision/delta, source registry and root/independent review receipts. The four v9 evidence sets remain separate.

Earlier T02 extended Immediate and Coalesced captures are listed as historical evidence with their own archive/probe/review digests and EF totals; they are not merged into the v9 totals. T04 typed-source and finite-control reviews are also listed separately: they qualify source/control checks, while live-capture readiness was not established by those synthetic controls.

**Disposition:** accepted as a finite diagnostic account with explicit residual ownership in the [final response](delivery-response.md#remaining-cost-and-owned-uncertainty). This evidence does not establish exact terminal-purge causality, the server response-finished boundary, exclusive request-only PostgreSQL statement totals or physical network round trips. Timing is measured in the separate low-logging series. Request-associated EF attempts remain counts at their validated trace scope.
