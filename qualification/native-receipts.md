# Landed runtime qualification

## Landed runtime and migration receipts

| Unit | Merge | Actual resulting-main qualification |
| --- | --- | --- |
| Publication history #4678 | `5cdb7343cc5f6b369262d63c79c53a67ae924bb4` | Runtime4,409 / Activities Runtime891, zero skips |
| Retained semantic owners #4984 | `4c3e9aaaf4e54c4ec19bf04c6a288178cc5e936e` | Runtime4,553 / Activities Runtime895, zero skips |
| Native memory timers #4988 | `7577a6e89f2e9343123edecf8c230e1e778f9fb8` | Runtime4,678 / Activities Runtime895, zero skips |
| EF timers and four migrations #5275 | `62092572693fedb738316befbcd410106eaa3e97` | EF2,937 / native providers360; all81 history controls and six fresh/retained paths, zero skips |
| Timer consumers, Delay and restart #5315 | `51c8197a97e31f8d5e4602627953225bacc942fc` | Scheduling141 / Delay54 / Activities Runtime895 / Runtime4,680 / API641 / EF2,943;17 distinct restart processes, zero skips |
| Native child/compensation/control/retry owners #5364 | `5479729406c068a3591193a69d7f492d76347d58` | Nine whole projects10,389 passes,17 distinct restart processes, zero skips |

The latest nine-project result is Dispatch405, Compensation453, Runtime4,690, Activities Runtime895, API645, EF2,943, Scheduling141, Delay54 and Graph163. Independent source review and architecture624, locked Release/isolated Debug restores and generated-map checks passed on their recorded premerge sources; these are not restamped as later executions. See the owning PRs' exact source-bound receipts, especially [#5275](https://github.com/elsa-workflows/elsa-foundation/pull/5275#issuecomment-7071954763), [#5315](https://github.com/elsa-workflows/elsa-foundation/pull/5315#issuecomment-7072627149) and [#5364](https://github.com/elsa-workflows/elsa-foundation/pull/5364#issuecomment-7074061199).

All four TimerHistoryOwnership migrations were generated through the official incremental tool and landed in #5275 with Designer/snapshot metadata and predecessor history preserved. Operational format1.2 and history schema1.0 are distinct. Fresh and frozen-store upgrades use disposable provider stores; no user database was reset or shared.

Native restart controls preserve actual committed identities, ordering, capture coverage, replay markers, timer responsibility, bookmark consumption and completion across distinct processes. Logical test-clock advancement is recorded separately from the composed Studio Delay, which uses a real published45-second Delay and its normal pump.

