# K1b evidence, 21 November 2026

Part of [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229).
The node lease is the prototype's ownership path. A node heartbeat renews one node record. Each partition retains
its own epoch and ordered journal position. See the [acceptance cases](../../architecture/ownership-and-fencing.md#acceptance-cases-node-lease)
and [condition NL-C1](../../program-goals/clean-slate-kernel.md#gate-2-conditions).

## Revisions and gates

| Work | Reviewed head and result |
|---|---|
| Watchdog, [#4922](https://github.com/elsa-workflows/elsa-foundation/pull/4922) | `db7345c1a` passed the Kernel prototype gate and Build & test. Main `0bef4889a`. |
| Mutation gate, [#4924](https://github.com/elsa-workflows/elsa-foundation/pull/4924) | `94fe53a2d` passed the exact-head [PR checks](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44894945448). Main `cfc9285e0`. |
| Node lease switch, [#4927](https://github.com/elsa-workflows/elsa-foundation/pull/4927) | `94ebb8096` passed [PR checks](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44905755455) and the [4,096 nightly](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44905756510/job/136850850729). Main `0d408860e`, with [per-file proof](https://github.com/elsa-workflows/elsa-foundation/pull/4927#issuecomment-7063110975). |
| Full-size nightly | [2aed0d254](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44899460969/job/136832117263) and [d0d51e183](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44902809023/job/136842091383) passed the full kernel and Native sweeps, mutation gates, store and runtime checks, and graph corpus. The subsequent split edit was confined to runtime conformance. |
| NL-9 in CI, [#4881](https://github.com/elsa-workflows/elsa-foundation/pull/4881) | The Kernel prototype gate passed on `7cb469467e`. The tree-identical replay `ed6452f47f` cleared the history scan and its [hosted NL-9 job](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44909063745/job/136860788418) passed 17 probes. Main `185648e635`, with [per-file proof](https://github.com/elsa-workflows/elsa-foundation/pull/4881#issuecomment-7063386431). |
| NL-C1 and WAL | 6c code `133f158882` adds both engine mutations and enables the two cases in the existing nightly. The 6c PR links its hosted exact-head result. The local readings are below. |

The 6b gate exercised 10,000 kernel and 10,000 Native seeds at 1,024 with the lease, landed and placement checks.
The revised 4,096 nightly passed 20,000 seeds in each model with zero failures and 17 of 17 runtime cases.
The short gate matched the 48 kernel, 15 Native and seven node-lease mutation verdicts. Its explicit exceptions remain
`FenceWithoutPosition`, `DeleteWithoutCount` and `FlatRetryGeneration` in their respective sweeps. The full
Native sweep caught `FreshTurnSwapsLoopKeys` at seed 14396 without the short-gate seed pin.

[6b provider evidence](https://github.com/elsa-workflows/elsa-foundation/pull/4927) covers locked builds, lease,
store and runtime conformance. SQLite passed 106 required cases in each owned fence mode and 98 on EF SaveChanges.
MongoDB 8.0 passed 108 in each fence mode and its broken-fence run caught 15. The killed-connection run ended with
all 2,000 messages accounted for. The MongoDB lease-renewal probe caught the stale snapshot variant, then observed a
refused take and a landed fence for the live owner after the primary-read fix. The same pooled sessions read back
MySQL's 50-second lock wait and SQL Server's −1 setting after a 9-second take.

## NL-1 idle heartbeat WAL

The `x setidle postgres` probe ran for 120 seconds on PostgreSQL 17.12 in a tmpfs-backed local Docker container
on arm64. Four logical RuntimeNodes in the runner held 256 partitions each, with the default 10-second lease
and one-second balance and poll loops. The sample started after all four joins settled.

```sh
dotnet ef/Kernel.Ef.Run/bin/Release/net10.0/Kernel.Ef.Run.dll x setidle postgres "<isolated connection>" seconds=120 count=1024 writes=1
```

| Metric | Node lease |
|---|---:|
| WAL over the 120-second sample | 133,808 bytes |
| WAL per hour | 4.0 MB (3.8 MiB) |
| Rate | 23.9 statements and 3.99 commits per second |
| Node tuple updates | 481, all HOT |
| Partition tuple updates | 0 |
| Runtime loop errors | 0 |

The lease write touches one node tuple per heartbeat. WAL inspection counted 479 node HOT-update records and
479 row locks in its LSN range. The independently sampled tuple statistics counted 481 HOT updates. Partition
WAL in the sample was page maintenance. The [K1 baseline](k1-evidence-2026-11-19.md#costs-criterion-6-slice-8) measured
161 MB per hour at the same 1,024-partition, four-node idle shape. This sample is about 40 times lower.

## NL-2 through NL-10

| Case | Evidence on the prototype |
|---|---|
| NL-2 | The store conformance case refuses the paused owner's whole flush, observes its empty ownership answer and counts one death per previous-epoch trial. The in-memory gate catches `TakeIgnoresNodeLease`. |
| NL-3 | The Worker consumes the heartbeat's ownership answer, stands the moved partition down and retains the other epochs. The seven-mutation gate catches `HeartbeatIgnoresAnswer`. |
| NL-4 | Store conformance holds a take over the expired-owner read and renews the old owner before release. The old owner keeps and can commit only the partition the take left. |
| NL-5 | Store and deterministic witnesses refuse the whole seen-only batch from the superseded epoch. `PlainReadEmptyFence` lands the held batch and is caught by both gates. |
| NL-6 | Store conformance refuses a restart with a fresh name until the predecessor's lease expires, then counts the trial deaths. The gate catches `StableNodeName`. |
| NL-7 | Store and Worker witnesses check the take's own node renewal, a late take beside a newer heartbeat, and a late take with no live deadline. |
| NL-8 | Store and Worker witnesses cover lost releases and retakes, two Workers on a fixed host and answers across reset. `KeepUnheldRows` and `ReleaseDuringTake` are caught. The two generation guards have separate witnesses. Removing the recorded-answer guard alone fails its witness with the take counter held constant. |
| NL-9 | See the [ordered and locking takes](#nl-9-ordered-and-locking-takes). |
| NL-10 | The delayed heartbeat stands down and retakes pending partitions, with a death counted. The wedged Worker stops renewing and taking. Passing both host mutation flags into the witness catches `RenewRegardlessOfPasses` and `TakeRenewsWedgedHost`. The 5b watchdog and 6b runtime conformance exercise hung owner calls and the host's stop policy. |

### NL-9 ordered and locking takes

The native hosted job forces both sides of the mutual take to wait on the held transaction. Both ordered takes
complete on all four setups. The local 6c probes observed the same ordered outcomes on PostgreSQL and MySQL.

| Engine and case | Ordered control | Locking mutation |
|---|---|---|
| SQL Server with RCSI | No deadlock | Repeatable-read take deadlocked with victim 1205 |
| SQL Server without RCSI | No deadlock | Repeatable-read take deadlocked with victim 1205 |
| MySQL 8.4 | No deadlock with the take at READ COMMITTED, on a REPEATABLE READ server default | REPEATABLE READ plus the node-record subquery in the update deadlocked with victim 1213 |
| PostgreSQL 17 | No deadlock | `FOR SHARE` on the other owners' records, including expired records, deadlocked with victim 40P01 |

The SQL Server runs also catch the locking READ COMMITTED subquery in the heartbeat and mutual cases, and a
membership read inside the heartbeat's transaction. The current job runs 12 ordered controls and seven negatives.
The 6c mutations live behind explicit probe options. The ordered take uses its existing plain lease read.

## The 4,096 split witness

[Historical run 44518408624](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44518408624)
sampled 18 of 20 tokens at the deadline. Two 6b runs sampled 19 of 20. The original logs stop there.
The [loaded trace](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44904668944/job/136847573071)
reproduced six misses and showed every remaining token in the durable inbox. The six schedules reached all 20 tokens
once and in order, with overlapping activations and stale fence refusal. A separate run missed the original
schedule's admitted-write precondition.

6b now admits and holds the old commit before starting the successor. The successor drains within a budget of 84
completed active scope passes, with hang guards. The witness checks overlap, exact token order and refusal of the
old commit. Both local sizes and the revised hosted nightly passed, nine additional hosted split runs passed under
competing load, and the local broken-fence control still failed both required fence cases. These results attribute
the reproduced misses to the witness deadline and scheduling. No delivery loss was observed.
[Revised hosted nightly](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44905756510/job/136850850729) ·
[Loaded repaired witness](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44907241707/job/136855338012)

## Post-acceptance corrections to K1

[Slice 4b, #4870](https://github.com/elsa-workflows/elsa-foundation/pull/4870), main `ed23b07a0`, corrected three
behaviours present before the node-lease switch:

- **Hand-over.** At seed 57839, a cancel for a partition gained after batch construction could enter memory and
  overtake the start. Hand-over now uses the partition set fenced by that batch and catches up by polling.
- **Loss during a retake.** At seeds 6448 and 4765, the runtime could lose the activation while its retake recorded
  ownership. The fix records the in-flight loss and reconciles the retake with the actual host state. A later harness
  scheduling change moved the Native witness from 4765 to 1102.
- **Process death.** The simulation stops a dead process from sending further calls. A live stale process and already
  admitted traffic can still race the fence.

K4 retains the provider qualification for NL-C1 and the provider-side bounds on a stuck owner call. The MongoDB
take-read wording and the owner-path coverage questions are recorded for Claude's 23 November follow-up.
