# Delivery slices and verified integration

The current delivery boundary is the landed runtime stack below plus the Studio/recorded-playback implementation in [#4368](https://github.com/elsa-workflows/elsa-foundation/pull/4368). The [earlier slice ledger](evidence/2026-11-22-pre-closeout-slice-ledger.md) preserves preparation checkpoints and failed/unqualified attempts without presenting their old pending labels as current status. Closed prototype #3850 is donor history, not a delivery PR.

| Landed unit | Merge | Behavior |
| --- | --- | --- |
| [#4678](https://github.com/elsa-workflows/elsa-foundation/pull/4678) | `5cdb7343cc5f6b369262d63c79c53a67ae924bb4` | Publication capture and pinned policies |
| [#4984](https://github.com/elsa-workflows/elsa-foundation/pull/4984) | `4c3e9aaaf4e54c4ec19bf04c6a288178cc5e936e` | Retained semantic owners |
| [#4988](https://github.com/elsa-workflows/elsa-foundation/pull/4988) | `7577a6e89f2e9343123edecf8c230e1e778f9fb8` | Native memory timers |
| [#5275](https://github.com/elsa-workflows/elsa-foundation/pull/5275) | `62092572693fedb738316befbcd410106eaa3e97` | EF timer ownership and four official migrations |
| [#5315](https://github.com/elsa-workflows/elsa-foundation/pull/5315) | `51c8197a97e31f8d5e4602627953225bacc942fc` | Timer consumers, published Delay and process restart |
| [#5364](https://github.com/elsa-workflows/elsa-foundation/pull/5364) | `5479729406c068a3591193a69d7f492d76347d58` | Native child, compensation, control and retry history |

The native resulting-main receipts retain their actual source and provider/migration/restart counts. Latest native main `54797294` passed nine whole projects (10,389 cases, zero skips) plus the separately recorded17 distinct restart processes. The Studio contribution adds the Timeline workspace, exact activity journals/bookmark lifecycles, recorded playback and composed native diagnostic acceptance. The new optional Event deadline is staged through the existing durable bookmark command; no retained registration-family shape changes.

CI is skipped under the feature owner exception. Independent correctness, standards and actual visual acceptance, protected data handling, exact reviewed source and resulting-main verification remain required. No hosted check is represented as passed by this waiver. Implementation, immutable shared-artifact/download and final merge/main receipts belong to the owning PR.
