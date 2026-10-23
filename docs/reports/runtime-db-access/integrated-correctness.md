# Integrated runtime correctness

**Accepted by the program lead, 9 October 2026.** [T17 / #2412](https://github.com/elsa-workflows/elsa-foundation/issues/2412) verifies the delivered runtime changes. Final measurements and findings remain [T18 / #2413](https://github.com/elsa-workflows/elsa-foundation/issues/2413).

The exact resulting-main source `7357b91e012c56cb34f1259ef7a60ef56e144afe` passed the rebuilt normal-host correctness matrix under both Immediate and Coalesced50 checkpoint persistence. The matrix covered nine capture cases per mode and independently joined 10 persisted PostgreSQL execution rows per mode to the API identities, for 18 capture cases and 20 persisted rows total. There are nine semantic scenarios per mode; the parent/child scenario produces two execution rows, so the JSON projection lists ten execution outcomes per mode.

## Exact-head gates

The Workbench build was source-conserved at the exact head with a 520-file closure. The accepted build receipt SHA-256 is `b628b4698d9932f7e921e0f4cf3adad19168a9916e7e1d5946250ec16b749b50`; the closure SHA-256 is `d2be60bf27d44f4b4098f4d2b7281aecc9581606f279de9782c359de600fc342`; the Workbench assembly SHA-256 is `b85e5ffa51614aacadc0c1398a703db32f136e510fd398c6d64e9f9c0245fbbb`.

Resulting-main [CI run37856735788](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37856735788) completed successfully. The selected affected suites recorded zero failures and zero skips: Runtime 2,130, Runtime EF 923, Design API 145, Publisher API 723, HTTP activity 204, HTTP integration 43, Resumption 21, native runtime providers 185, and Architecture 635. Maps `37856735125`, Quality `37856734716`, Docker `37856928358`, Packages `37856735178`, and filters `37856735180` also completed successfully.

The production/test delta from response integration `9d8a4b7ed96de043924a85a51764dc8d6f956951` is the five-file Design API correction in [PR #2372](https://github.com/elsa-workflows/elsa-foundation/pull/2372); five unrelated modular-hosting documents also changed. Runtime, HTTP, and process-loss proof surfaces are unchanged for this acceptance; the correction is therefore adopted as the exact source identity for this final matrix rather than presented as a new runtime persistence implementation.

## PostgreSQL normal-host matrix

Both modes used PostgreSQL 16.15 from the pinned image digest `sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d` on `linux/amd64`. Each mode passed all nine capture cases. API execution identities, definition/version/artifact/source references, status, cadence and cap match persisted PostgreSQL state. Incident counts and inspection granularity were separately verified through authenticated API readbacks. The two configurations differ only in cadence; their table and migration sets are identical.

| Mode | Primary and REST | Other controls | Persisted inspection | Evidence |
|---|---|---|---|---|
| Immediate | HTTP 200 with the expected `Alice Smith` body; valid REST 200 with matched output | Workflow flow, SetOutput, HTTP workflow, value capture, parent/child input, fault, and faulting-input controls passed | `Immediate`, activity-level | receipt `bbf65b75e04922786b572680d7c1190dee0efd261fb5f02adb3f2fbf2e3af40d`; provider audit `917b27a9680f06218529156ce03507aad143a58aa0273b5edbc96e375e9457b4`; schema `5d940d01cdad29370168e8e9941c3f49d96f765719d185bf575ee6f5570e89e7` |
| Coalesced50 | HTTP 200 with the expected `Alice Smith` body; valid REST 200 with matched output | Workflow flow, SetOutput, HTTP workflow, value capture, parent/child input, fault, and faulting-input controls passed | `Coalesced`, cap 50, boundary-level | receipt `24e6eceed39e3fbec1d93affd80723a10d77e0a01af6eea2f5705a6ede09dace`; provider audit `86dca25e93d88b62c844ed34495eafcae55f2d2a05703c8f67e35f522a0e1523`; schema `29ac2992e599ba4dd18439475c74537c7791ac53435b5f2be080020dc51f4a86` |

The SetOutput result was persisted and matched its authored value. The HTTP workflow trigger returned its asynchronous 202 admission and the persisted execution completed with the same identity. Value capture joined the writer input, HTTP status/body evidence, and activity inspection to one execution. Parent and child executions completed with the expected input echo and parent link.

The explicit fault activity produced one blocking activity fault, ran the before branch, did not run the after branch, and persisted the `FaultWorkflow` resolution. The malformed/faulting-input control returned the observed 202 admission, persisted one blocking `SchedulerWorkPoisoned` incident, remained Running, and produced no downstream response. Its identity, transport, admission, and durable-incident predicates all passed. This is an observed negative-control result; it does not prescribe a universal HTTP status policy or repair the historical unexpected valid-input 202 attribution.

## Provider and preservation boundary

The PostgreSQL schema audit found 75 tables and four migration histories (`ElsaRuntime`, `ElsaWorkflowsDesign`, `ElsaActivitiesDesign`, and `ElsaPublishingSnapshotReview`). The audit queries were read-only. A separate source-aware SQLite audit passed for ancillary stores, with no forbidden runtime, design, or publication-review tables; diagnostics remained SQLite by source configuration. This is a mixed provider composition with PostgreSQL covering runtime/design persisted state, not an all-PostgreSQL composition claim.

For each mode, the read-only persisted-row audit restarted the retained PostgreSQL container, performed no workflow rerun or repair, preserved the original receipt and artifacts, and retained the volume, container, content root, and logs. The Workbench host and PostgreSQL container were stopped after capture.

Previously accepted process-loss, replay, fencing, nested ownership and partition proofs are adopted because the affected runtime/HTTP production and proof sources are unchanged. Current-main affected suites also passed; no older result is relabelled as a new execution.

| Adopted contract | Evidence and scope |
|---|---|
| Inline response recovery | [T009 hard process loss](../../../specs/198-response-replay-safety/evidence/crash-recovery.md): SQLite, Coalesced cap50, fused buffered completion, kill137 and fresh-process recovery to the same committed response. The socket is not claimed to survive. |
| External input replay | [T019 external body restart](../../../specs/198-response-replay-safety/evidence/external-input-restart.md): SQLite, cap2, fusion disabled, a distinct provider in a fresh process rereads the same external body after lease expiry. |
| Cache ownership, access partition and stale generations | [Spec197 integration](../../../specs/197-bounded-durable-value-page-reuse/evidence/integration-verification.md): full access/codec/scope memo key, nested independent ownership, and write/failure fencing. |
| Persisted interruption and stale fence | [Spec197 qualification](../../../specs/197-bounded-durable-value-page-reuse/evidence/final-live-verification.md): persisted interruption snapshot, fresh provider recovery and stale-fence rejection. The snapshot is not an OS process kill. |
| Mandatory boundaries and current provider contracts | Current-main Runtime, Runtime EF and native runtime provider suites above; [response proof](../../../specs/198-response-replay-safety/evidence/integrated-verification.md) retains claim/lease and mandatory-boundary assertions. |
| Contingent coordination optimizations | Reviewed [checkpoint](checkpoint-access-spike.md) and [scheduler/outbox](scheduler-outbox-access-spike.md) no-change dispositions remain accepted. Their rejected trials are not shipped or counted as gains. |

The SQLite process-loss proofs remain SQLite evidence; this PostgreSQL matrix does not extend them to PostgreSQL process loss or exactly-once transport.

This acceptance covers correctness and provider/state identity. It makes no timing, query-saving, comparative-gain, or performance claim. #2413 remains the separate timing and accounting work item. Raw database rows and request logs remain private; the committed projection excludes credentials, connection strings and private host paths.

The [sanitized evidence projection](evidence/integrated-correctness-2026-10-09.json) records exact run, build, schema, original receipt and provider-audit hashes. The root review projection has SHA-256 `9673cadef8bed39a8cf411761d23f6a9bb316a66c46ef7512f1d98635453fa42`.

Earlier failed wrapper attempts and historical product failures remain retained. The final clean runs do not turn those attempts into passes. In particular, #2293/#2185 and the historical valid-input202/C4 failures retain their separate attribution boundaries. PR #2372 corrects the independently reproduced authored-enum HTTP400; it does not explain those historical symptoms.
