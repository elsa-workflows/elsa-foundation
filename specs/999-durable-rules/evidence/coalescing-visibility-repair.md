# Coalescing visibility and command-budget repair

CI [39771539856](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39771539856) failed on parent `a5653d99fc9ba1062210f10293c410cdd44cece4`: the existing fork/join resume command counts were 269/256 for Durable and 211/183 for Balanced and Ephemeral. The full HTTP suite reported three failures and 91 passes. Downstream architecture and core-only jobs were skipped, not passed.

## Cause and correction

Local reproduction returned the same counts. SQL traces isolated the excess to the resumed phase: the start phase remained 169 commands for Durable and 111 for Balanced/Ephemeral. The resumed phase used 100 commands rather than the baseline allowances of 87/72. The unconditional pre-claim checkpoint boundary disabled coalescing around the ordinary resume path. A diagnostic removal restored budgets but omitted durable retry visibility; a diagnostic nested segment saved only one command. Neither experiment is the production repair.

`IWorkflowSchedulerWorkVisibilityReader` is an optional provider capability returning queued work and current claim/retry visibility in the same bounded page read. Both shipped providers implement it without a schema change. The original queue interface and public coalescing-session constructor remain compatible. Other providers retain the conservative durable pre-claim path and continuation handoff documented in [the earlier repair](coalescing-review-repair.md).

A visibility-aware overlay keeps ordinary successful resume coalesced, honors existing visibility deadlines, and persists a retry delay only when work actually defers. Its flush retains the source, reconciles consumed work by identity, then claims the actual durable head and releases that same identity with the requested deadline. An absent, competing or changed head cannot produce a successful durable Deferred result. A checkpoint that retains scheduler work must also retain it during coalesced reconciliation. A source already durably consumed cannot be recreated by a later release.

Visibility pages follow the provider's actual claim order. In-memory claim FIFO differs from its general-purpose item listing; its visibility cursor therefore uses stable insertion ordinals and a distinct cursor binding. Existing item listing behavior is unchanged. EF retains its existing `WorkOrderKey` order and one-query page read. The existing identity-reconciliation test now covers both the old-provider listing path and the visibility-aware FIFO path.

## Regression evidence

Three removal controls each failed an executed regression, and the source was restored after each:

- Ignoring the visibility deadline allowed an actively claimed source to be claimed by the overlay.
- Omitting durable release left the retry deadline null after source persistence.
- Ignoring `RetainSchedulerWork` failed recovery after an intermediate checkpoint that deactivated the overlay.

The new tests also cover restart plus an early real drain before the retry deadline, a retained source after deactivation, overlay-only sources, competing claims, changed heads, consumed-source refusal, scoped bounded provider pages, and continuation after head removal. The original fallback-provider regression coverage remains active. Provider implementation QA was delegated and reviewed by the root; root ran all validation.

[Command records and source/log hashes](coalescing-visibility-repair.json) retain the local results and removal evidence. The final local HTTP integration run passes all 94 cases with the original budget file unchanged. Native package evidence retains its own package and host source identities; no new Rules package metadata is required by this repair.

## Local verification

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Workflow Runtime | 2,559 | 0 | 0 |
| Runtime EF | 1,005 | 0 | 0 |
| HTTP Integration, including unchanged EF budgets | 94 | 0 | 0 |
| Rules | 278 | 0 | 0 |
| Architecture | 585 | 0 | 0 |
| Core-only filter (19 suites) | 4,227 | 0 | 2 |

The two core-only skips are the existing platform-sensitive `PayloadCatalogFileTests.ReadArray_IoFailure_WrapsOriginalIoException` and `EfSqliteSerialOpenStressTests.Concurrent_opens_of_one_file_never_share_a_pooled_connection`; they are not counted as passes.

Workbench rebuilt successfully; the owned-server restart/recovery and HTTP Flowchart journeys passed. Generated maps were refreshed, remained byte-identical, and passed freshness checking. The solution-filter check passed after correcting an invocation typo (`check-solution-filters` to `solution-filters-check`); the original command error remains visible in the receipt and is not a test failure. No whole-program or resulting-main completion is claimed.

## Renewal timeout correction after main integration

The normal merge of main `3625ca163b84059ef21e9d8a630baae8d125a184` into the repair produced `dab62015bf182039552aadbc432ccaffc21c3bd0` without conflicts. Main changed Studio and Elsa 3 import code; it did not alter the repaired Runtime or Rules inputs. Integrated architecture, generated maps, solution filters and diff checks passed.

A subsequent focused check found that deriving a new durable claim timeout from a renewed claim's `VisibleAfter - ClaimedAt` includes its elapsed lifetime. The new regression requested a three-minute renewal after two hours and observed an incorrect 02:03:00 timeout. The session now records the latest claim/renewal timeout explicitly and uses that duration when persisting a deferred release. The regression passed with restoration of 00:03:00 and the intended retry deadline.

Final focused validation: full Runtime 2,560, Rules 278, Architecture 585 and all 10 unchanged command-budget cases passed, with zero failures/skips. Map freshness and diff checks passed. [The renewal receipt](coalescing-visibility-renewal.json) preserves the red/green logs and source hashes. Earlier full HTTP, EF, core-only and server results above retain their pre-correction scope; this does not relabel them as reruns of the final head.

## Gate limits

The six formal self-review rounds remain exhausted. Focused diagnosis, repairs and regression validation are not a seventh formal round or a new clean final full review. Code Review reassessment of the new head, its required hosted checks and resulting-main qualification remain required. This evidence does not authorize merging a red or unapproved head or declare the program complete.

Historical receipt, HTTP and Workbench timeouts retain their prior causal uncertainty. Physical external effects remain at least once.
