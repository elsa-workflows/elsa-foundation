# Joined request, settlement and background accounting

This T02/T04 handoff joins the accepted [checkpoint/queue operation packets](checkpoint-queue-accounting.md) within each capture and imports the separate response and historical evidence with its original boundaries. It establishes observed identity subgraphs and a bounded settlement account. It does not explain every historical untraced command or claim new query savings. The [compact derivation](evidence/joined-request-settlement-2026-10-07.json) pins the inputs and counts.

## Request and drain boundaries

Each row below contains one successful request trace. Commands are paired by `(commandOrdinal, occurrence)` and binned using that trace's monotonic `request_drain` start and stop. No pair straddles a drain boundary. These are EF command executions, not SQL statement counts, rows or provider round trips.

| Capture / trace | All commands | Exact method joins | No method ancestor | Before drain | Within drain interval | After drain stop | After-stop source-bound tail |
|---|---:|---:|---:|---:|---:|---:|---:|
| Coalesced HTTP | 277 | 129 | 148 | 13 | 218 | 46 | 40 |
| Coalesced REST | 101 | 39 | 62 | 11 | 77 | 13 | 8 |
| Immediate HTTP | 635 | 392 | 243 | 12 | 609 | 14 | 8 |
| Immediate REST | 556 | 345 | 211 | 10 | 533 | 13 | 8 |

Before, within and after sum to each row's total. The source-bound tail is a subset of after-stop work whose recorded source boundary remains `request_drain`; it is not an additional bucket. The remaining outside-boundary totals are 19/16 Coalesced HTTP/REST and 18/15 Immediate HTTP/REST. Closed command/save/transaction lifecycles and conserved tails do not establish that all inherited work was awaited.

The extended captures contain **no HTTP response-finished timestamp**. A drain stop is a method boundary; the harness completion time is not the response time. Consequently, the after-drain commands above cannot be labelled post-response from this evidence.

## Bounded settlement and separately observed sweeps

Both primary HTTP requests returned 200 with the expected `Alice Smith` body, reached Completed with zero incidents, and have a read-only terminal settlement snapshot. Coalesced has zero scheduler items and zero entries in all six outbox statuses. Immediate has zero scheduler items, 15 Delivered outbox entries and zero in the five other statuses. The REST companions independently returned 200 with expected output, Completed state and zero incidents; neither has a separate settlement snapshot.

All selected primary HTTP-trace command terminals precede their own settlement marker: last sequence 916 before marker 1251 for Coalesced, and 2498 before 4679 for Immediate. This accounts for 277/635 HTTP-trace commands through those markers; it is not an exclusive total of all work owned by either instance.

| Capture | All sweeps / commands | Sweeps before settlement / commands | Sweeps after settlement / commands |
|---|---:|---:|---:|
| Coalesced | 7 / 42 | 1 / 6 | 6 / 36 |
| Immediate | 6 / 36 | 1 / 6 | 5 / 30 |

Every sweep has four liveness SELECTs, one scheduler SELECT and one outbox SELECT. Thus the six post-settlement Coalesced sweeps contain 24 + 6 + 6 = 36 commands, and the five Immediate sweeps contain 20 + 5 + 5 = 30. The scheduler/outbox pairs have exact method ancestry; the liveness pairs have source-compatible mechanisms but no observed method ancestor. Global polls carry no target-execution identity. Their recorded returned-count metadata is zero, but unknown method outcomes, query return counts and SQL affected-row counts remain distinct. These sweeps are separately counted background observations, not proven instance-owned or awaited work.

## Identity joins beyond TraceId

Joins use typed `(domain, HMAC)` identities within individual operation records and explicit parent operation ordinals. They do not form Cartesian associations between flat execution/item sets, and identities never join across captures.

- **Checkpoint:** all 34 Coalesced child spans match their four commit parents' execution/checkpoint/commit triples; all 293 Immediate child spans match their 40 parents. Each primary drain's execution identity matches its settlement marker.
- **Coalesced scheduler:** four enqueued work-item IDs match four dequeued IDs and the execution input of each corresponding invocation. Two list-return IDs match two of those enqueues and their operation-local execution inputs.
- **Immediate scheduler:** 42 enqueued IDs match 42 returned IDs across 70 claim calls, including each invocation's execution input. Twenty `consumed_scheduler_work` stage records match enqueue/claim identities and their parent commit execution. Separately, 22 `complete_claim` inputs match enqueue identities and execution. The 20 staged-consumed and 22 complete-claim sets are disjoint. These are distinct observed consumption paths; they are not a single inferred claim-to-checkpoint-to-completion chain or evidence of a defect.
- **Immediate outbox:** 28 stage records contain distinct outbox IDs plus intent and parent-commit identities. Those outbox IDs match 28 returned IDs across 30 `get_deliverable` calls and 28 inputs to `record_delivery_result`. Each returned set belongs to its own invocation's execution query input; the [captured source filters by that execution](https://github.com/elsa-workflows/elsa-foundation/blob/b3f55bef32576011d39b93d664949a0cb705fb3b/src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L487-L530). This is an observed typed-ID chain with source-confirmed query filtering. Coalesced has no staged or returned outbox item IDs in this packet.

All method return outcomes remain unobserved. Participant inputs are requested inputs, not written rows, and a combined flush cannot be allocated to individual participants from its command count. The terminal snapshot contains status counts, not the 28 outbox row identities above. These joins therefore do not prove that each staging/delivery operation persisted its requested result. They also do not assign the 716 commands without method ancestry to specific items or global polls to an execution.

## Response latency, older boundaries and historical uncertainty

The [before timing report](before-timing.md) retains separate 60-response sequential HTTP windows: Coalesced median/p95 130.4095/237.7586 ms and Immediate 437.5354/760.9183 ms, with source/configuration/load qualifications. These are before-only observations, not timings of the extended diagnostic captures. REST timing covers admission rather than full HTTP response delivery. The failed Coalesced concurrency-four window remains failed correctness evidence.

The separate [EF identity ledger](ef-identity-accounting.md) records command callbacks relative to its client `IWR finally` response boundary: Coalesced HTTP 277 before-or-at / 0 after; Coalesced REST 85 / 16; Immediate HTTP 635 / 0; Immediate REST 556 / 0. The earlier server `RequestFinished` boundary is different. Neither boundary can be transferred to the extended captures through process-local HMACs or matching command totals.

The [supplied findings](findings.md) report 60 idle statements in 40 seconds and 25–695 untraced commands per 60 default calls, versus 1–25 per 60 Coalesced calls. Those historical populations are not the current one-request-per-control captures. The current 716 commands without method ancestry and the observed post-settlement sweeps do not explain that range. No claim that all historical untraced work was polling is supported.

## Accepted handoff and remaining ownership

Root accepts this finite T02 input and qualified T04 accounting handoff under the program's explicit-residual-uncertainty rule. This accepts the observed joins, reconciled counts, bounded primary settlement and honest historical disposition. It does not mark literal all-command caller/row attribution or same-run post-response placement as proven. The [45-group residual account](residual-accounting.md) retains each measured family and its owner/revisit condition.

T04 retains the historical untraced-range uncertainty; the program lead carries it into T18's final response. Revisit exact ownership if a selected queue/outbox reduction relies on it or final comparable evidence shows an unexplained delta. T18's own final capture must contain its own response boundary and relevant identity evidence before claiming same-run post-response ordering. It must also report total observed commands, primary settlement and independent polling separately. No second generic baseline harness is selected to fill an otherwise unused historical gap.

T07 receives the post-M2 state-read counts and the relevant residual families as inputs to its normal-path materialization experiment. Absence of repeated-read proof is that spike's question, not an external prerequisite on starting it. Any subsequent cache implementation still requires selected-design, ownership, freshness and correctness proof. T02/T04 remain open through PR review and the recorded qualified acceptance transition; remaining program correctness and final measurement gates are unchanged.
