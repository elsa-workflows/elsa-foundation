# Contract: #645 to #646 Performance Handoff

#645 supplies correctness-proven workload definitions. #646 owns the benchmark harness, statistical method, physical-form comparison, raw artifacts, reports, and final Pass/Redesign/Blocked verdicts.

## Handoff bundle

Every workload supplies:

- stable workload identity and version;
- coverage-ledger row mappings;
- public Elsa operation(s), not a provider-internal shortcut;
- fixed seed, dataset cardinality, payload distribution, query selectivity, and concurrency;
- provider/topology/package versions and composition fingerprint;
- setup and cleanup rules excluded from timing;
- exact correctness invariants and deterministic result digest;
- scale-bearing route and native-plan evidence requirements;
- expected EF observable contract where one exists, with real EF execution explicitly owned by #646;
- relevant Groundwork physical forms to compare;
- restart/failure preconditions where relevant;
- provisional acceptance gate or a reviewed workload-specific replacement.

`requiredNativeRoutes` names exact `BoundedQueryDeclaration.Identity` values from the current physical manifest;
it is not a copy of the coverage ledger's provider-neutral `queryShapes`. A workload may leave that list empty
when its public operation is composed solely of writes, point reads, or other paths without a bounded-query plan.

Timing is invalid until the correctness digest and provider conformance scenario pass.

## Required workloads

| Workload ID | Public operation | Required correctness baseline | Primary rows |
|---|---|---|---|
| `checkpoint-commit` | Commit a representative execution bundle with state, bookmark/value/inspection changes, outbox entries, idempotency marker, and current fence | One atomic bundle, stale fence rejected, equivalent replay stable, no partial outcome after failure/restart | Checkpoint, activity/execution/value state, outbox |
| `bookmark-lookup` | Resolve bounded resumable bookmarks for a representative stimulus/execution scope | Exact result identities, scope isolation, deterministic order/continuation, native bound | Bookmark state |
| `trigger-binding-stimulus-lookup` | Resolve bindings by stimulus type/hash | Exact active bindings, no cross-publication/tenant leakage, storage-bound predicates/order/limit | Trigger binding, source reference |
| `recovery-scan` | Select bounded stale/recoverable executions/liveness/operational state | Exact candidates, stable due ordering, no full collection materialization | Execution state, liveness, incidents, scheduler/holds |
| `queue-drain` | Claim, process, retry, and acknowledge a bounded batch | One current owner, FIFO/due order, stale ack rejected, poison relationship durable | Scheduler queue, poison |
| `outbox-drain` | Claim and complete/retry a bounded post-commit batch | One current claim, deterministic due order, stale completion rejected | Post-commit outbox |
| `due-timer-selection` | Select and advance bounded due timers | Exact due set/order, conditional advancement, no duplicate successful ownership | Durable timers |
| `recurring-schedule-selection` | Select/advance bounded due recurring schedules | Exact due set/order, publication state consistent, conditional advancement | Recurring schedules, projection state |
| `iam-normalized-lookup-update` | Tenant-normalized lookup followed by revision-aware update | Five exact normalized/relationship lookups, current update accepted, stale update rejected, and real provider-native route evidence | IAM rows, #644 adapters |
| `secret-create-read-list` | Concurrent create, point read, and bounded deterministic list | Exactly one create winner, exact value/version, bounded page and continuation | Secrets repository |
| `placement-takeover` | Claim, renew, expire, and take over execution placement | One current lease, monotonic version, stale release rejected | Distributed placement |
| `command-send-lease-ack` | Concurrent send, bounded lease, visibility expiry/re-lease, acknowledgement | Unique identity/sequence, ordered lease, stale ack rejected, no loss after restart | Distributed transport |

The #644 Identity workload definition is versioned in
[`../workloads/iam-secrets.json`](../workloads/iam-secrets.json) as
`iam-normalized-lookup-update` v1.1.0. Its provider-neutral correctness handoff executes one canonical user,
16 noise users, one role, and one user-role link through physical Groundwork Identity storage. It uses input
fingerprint `5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and public result digest
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`. These hashes define the
provider-neutral contract; they are not accepted provider evidence. Each provider entry point must capture native
evidence for the five lookup routes against exactly 100,000 physical records and require one materialized
candidate. The current Identity shape uses Groundwork physical entity tables. SQLite is mandatory; SQL Server,
PostgreSQL, and MongoDB use the opt-in external-provider matrix. Spec 095 retains accepted exact-candidate
Groundwork `preview.60` / Identity manifest v1.0.4 evidence for all four topologies as immutable historical
provenance. Current execution uses the repository-pinned `preview.80` family and current Identity manifest; no
historical `preview.60`/`preview.76`/`preview.77` artifact is linked as an active exact-head pass.
The committed EF artifact is a non-executed contract baseline only. #646 owns real same-provider EF execution,
equality, and all timing.

Groundwork PR #88 supplies the generic version-aware codec contract consumed by the current package family.
Groundwork PR #95 extends the certified provider-neutral keyset continuation introduced in `preview.62` with
residual predicates over bounded physical routes. Groundwork PR #96 adds the portable substring search keys
consumed by `preview.63`; Groundwork PR #97 adds provider-native latest-per-key execution; Groundwork PR #101
admits sort-only index fields as residual predicates; and Groundwork PR #108 adds bounded linked hydration,
all consumed by `preview.80`. Elsa-specific per-kind version policies, legacy-stamp parsing, JSON
options, and concrete upcasters remain behind Elsa's provider marker and provider packages so
core modules remain Groundwork-free. Any codec or manifest change invalidates prior composition fingerprints and
requires fresh exact-head provider evidence before the workload can feed #646.

FR-030 lists secret create/read and bounded list as one lane; the workload may report point and list operations separately but must return one verdict covering both. Checkpoint bundle sub-operations may also report separately, but the atomic bundle gate cannot be replaced by isolated document timings.

## Dataset requirements

- Use fixed seeds and content hashes committed with the workload.
- Include enough records that every requested page is smaller than the candidate population.
- Include equal values, null/missing values, long/Unicode values where supported, ordering ties, expired/not-due records, cross-tenant collisions, and terminal/nonterminal lifecycle states.
- Concurrency workloads use independent clients and record contender counts/winner distributions.
- MongoDB uses the topology required by the public atomicity claim.
- EF and Groundwork compare identical logical inputs and observable results; provider-native setup differences are recorded, not normalized away invisibly.

## Provisional verdict gates

Unless #646 records a reviewed workload-specific replacement:

- runtime hot path: median measured p95 no worse than 1.10x same-provider EF and throughput at least 90%;
- ordinary store: median measured p95 no worse than 1.25x same-provider EF and throughput at least 80%;
- Groundwork p99 no worse than 2x same-provider EF;
- providers without an EF oracle still require absolute baseline, correctness, bounded-plan, and Groundwork-form comparison evidence.

#646 determines run count, steady-state duration, confidence method, measurement validity, and physical-form selection criteria. #645 does not reinterpret its output.

## Verdict consumption

| Verdict | #645 action |
|---|---|
| `Pass` | Link evidence and accepted shape in every mapped ledger row; advance performance state. |
| `Redesign` | Return mapped rows to implementation, record the recommended shape/route/primitive, remediate, and resubmit the same workload version or an explicitly versioned successor. |
| `Blocked` | Keep mapped rows incomplete and link the missing prerequisite/evidence. |

No lane is ready for EF deletion while its mapped verdict is missing, non-reproducible, Redesign, or Blocked.

## Artifact retention

Durable reports/raw summaries retain workload version, commit SHAs, package/provider versions, composition/manifest fingerprints, machine/topology metadata, fixed seed and input hash, result digest, native plan identity, allocation/DB work/round trips/storage/write amplification where measured, and the final verdict. Connection values, credentials, and secret payloads are never retained.
