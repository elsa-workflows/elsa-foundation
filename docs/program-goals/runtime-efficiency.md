# Runtime Efficiency

- **Status:** Completed 2026-11-16. Active from 2026-10-10; end-to-end continuation authorized 2026-11-01. The
  [closeout](#closeout-16-november-2026) below records each milestone and child. Remaining qualification belongs to
  the kernel ([#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229)).
- **Area:** Execution overhead, immutable preparation, persistence, admission and concurrent recovery.
- **Steward:** Sipke. **Program:** [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531).
- **Authorization:** [owner instruction and delivery scope](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6554699791),
  [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) and
  [D2](../adr/0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default).

This program absorbs [Runtime Database Access](runtime-db-access.md) and [Runtime Throughput](runtime-throughput.md).
The program issue is the queue and progress record. Keep one active implementation leaf; reuse matching issues.
The ready queue already exceeds its cap, so additional work stays on the parent until existing leaves are reconciled.

## Outcome and evidence

Reduce work per execution while preserving the selected persistence mode, current input values, authorization,
ownership fences, checkpoint/outbox atomicity and recovery. Use deterministic command/read budgets for regression
coverage. Owner-requested timing comparisons are bounded qualification evidence, with their actual hardware,
payloads, cache state, failures and exclusions retained.

The [native assessment](../reports/native-runtime-performance-2026-10-31.md) and
[first implementation](../reports/native-runtime-performance-implementation-2026-10-31.md) establish the starting
point. [#3542](https://github.com/elsa-workflows/elsa-foundation/issues/3542) / PR #3545 delivered the zero-input
fast path, immutable input preparation, indexed coalescing views and weak EF provider factories. Its timing cohort
predates final main integration; later correctness qualification does not change that provenance.

## Delivery milestones

| Milestone | Completion evidence | Existing records |
|---|---|---|
| M0: reconcile the queue | Each older objective is mapped to current code, delivered work or remaining acceptance; original failed/skipped/superseded states remain explicit. | [Persistence epic #2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564), #2582–#2586, #2828, #2831 |
| M1: complete accepted native starts | Explain the intermittent incomplete in-memory start with a causal control; prove completion/recovery and identifier behavior under concurrency. Passing later runs alone do not establish a fix. | #3542 assessment and parent #2531 |
| M2: demand-driven inputs | Literal-only and narrowly scoped inputs avoid unnecessary history/value/frame reads; custom materializers, dynamic bindings, lazy structure inputs and closed scopes retain their semantics. | Parent #2531; first slice #3542 delivered |
| M3: immutable preparation | Reuse hydration/nullability, conversion and prepared-JavaScript metadata with bounded or weak ownership; prove fresh values, concurrent isolation, validation and collectible-module release. | [Hydration #2657](https://github.com/elsa-workflows/elsa-foundation/issues/2657); parent #2531 |
| M4: storage and scheduling cost | Attribute the fixed command floor, lower justified budgets, qualify pooling and retained history, and resolve applicable suspension/cancellation, pump and diagnostics objectives. Preserve replay and stale-writer rejection. | #2564; [pumps #1307](https://github.com/elsa-workflows/elsa-foundation/issues/1307), [diagnostics #2613](https://github.com/elsa-workflows/elsa-foundation/issues/2613) |
| M5: integrated qualification | Representative expressions/payloads, sustained arrivals, owner crash/takeover, duplicate commands and database interruption have reproducible outcomes; native/BPMN/ETL measurements name the actual model, provider and machine topology. | Parent #2531; model capabilities remain on [Unified Execution #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) |

The first continuation slice, #2657, reuses weak type-owned hydration metadata. JavaScript and conversion
preparation remain separate M3 work. M1–M5 acceptance remains open until its evidence is recorded; a merged
optimization does not complete this program. Available ETL already uses bounded partitions/batches: qualify and
improve that implementation instead of assuming it must be introduced.

## Boundaries and sequencing

- Run beside the active programs and coordinate shared runtime/model changes through their existing issues and PRs.
  This program owns efficiency and qualification; Unified Execution retains ownership of model/hosting features.
- Balanced remains the default. Preserve each mode's documented checkpoint and external-effect boundaries.
- Keep artifact caching bounded and lifecycle-aware. Mutable execution values and scoped services remain invocation-
  or ownership-scoped; arbitrary process-wide durable-state caching is outside this plan.
- Keep the [proposed commit journal](../adr/0084-runtime-commit-journal.md) low priority under its existing triggers.
  No journal rewrite, timing CI gate, general benchmark service, production SLO, release or production deployment
  is introduced by this continuation.
- Choose the independent-host qualification fixture when implementation is ready to run. Record actual host and
  database placement; multiple processes or containers on one host are not independent-machine capacity evidence.

## Completion

Close #2531 only after M0–M5 and every still-applicable older child acceptance have demonstrated results on main,
or a separately recorded owner disposition. Each delivered change carries reviewed-head checks, behavioral
controls and affected-suite accounting; final integration verifies a containing main commit. Reports preserve
remaining failures and evidence limits. The program and registry are marked completed only at that point.

## Closeout (16 November 2026)

On 11 November the owner replaced the runtime core with the clean-slate kernel
([#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229)). The owner then decided on 16 November
([record](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6917554545)):

- old-core-only objectives close as superseded by the kernel;
- layer 3 work that carries over stays delivered;
- current-core defects are fixed only where needed, and the ones the kernel makes irrelevant are closed;
- M5 becomes part of kernel release 1 qualification.

The current core has shipped only as the contributor preview `contributor-preview-2026-10-07.1`. It is deleted when the
old core is removed (#4229 gate 5). Kernel figures below are prototype counts from `prototypes/kernel-model` at main `baed25d04`.
They are not product budgets.

| Milestone | Outcome | Evidence |
|---|---|---|
| M0 | Delivered: 27 objectives mapped to code, delivery or disposition | [Reconciliation](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6615399245) |
| M1 | Closed by the owner on 1 November on two causal fixes, UUIDv7 runtime IDs and rollback-safe immediate continuation (#3601). The historical incident's cause stays unconfirmed. The kernel removes both mechanisms: its clock is monotonic and anchored to database time (the `WallClockTimers` mutation is caught), and ids inside an instance are counters in its state | [Decision](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6563805234), [report](../reports/native-runtime-performance-program-2026-11-01.md#original-incomplete-start-investigation) |
| M2 | Delivered in #3601: literal, secret, request and closed-scope inputs read no history, and custom binding resolvers get the full context | `ActivityInputSnapshotCheckpointTests.Demand` |
| M3 | Delivered: weak hydration metadata (#3598), prepared JavaScript and conversion metadata (#3601), with fresh values, concurrency and collectible-module controls. Hydration and JavaScript live in layer 3 libraries and carry over. Conversion lives in `Elsa.Workflows.Runtime`, so the kernel's native-model slice moves or replaces it | `ActivityInputHydrationReuseTests`, `PreparedJavaScriptReuseTests`, `RunJavaScriptPreparationTests`, `ValueConversionPreparationTests` |
| M4 | Delivered on the current core where it applied (#1307, #2586, #2828, #2831, #2584, #3647, #3275, #2564 R1 and R2a). The rest of epic #2564 closed as superseded: a turn ends in one fenced batch with no savepoints, pumps, drains or separate queue. Ten WriteLines under Balanced: 64 SQLite commands and 48 PostgreSQL round trips on main, 3 commands and 2 or 3 round trips on the prototype | [#2564 disposition](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6917321380), [NATIVE.md section 5](../../prototypes/kernel-model/NATIVE.md#5-what-it-costs) |
| M5 | Handed to #4229 gate 3 with its acceptance, the 32-command and 16-round-trip budgets, the M2/M3 tests to keep, the M1 controls, graceful stop and uncertain commits | [Hand-off](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6917342686), [additional rows](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6917418143) |

Fresh receipt on main `6f275e16c`, Release, run locally on 16 November: the M2 and M3 tests named above and the ID burst
control all pass (48 passed, 0 failed, 0 skipped). It is a correctness receipt and measures no timing.

The open children #3698, #3700, #3703 and #3939, and #4043 (filed as part of this program, not a sub-issue), were
closed as superseded by the kernel. Each closing comment
names the kernel mechanism that removes the defect. Codex's drafts #3841, #3848, #3868, #3905 and #3915, and its ready PR #3941,
were closed unmerged, with their branches and evidence kept. The sections above remain the charter as it stood while the
program ran.
