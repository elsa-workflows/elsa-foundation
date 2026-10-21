# Quickstart: proving M5

The backend journeys use the shared reservation participant from spec 251 (operation-key deduplication, tombstones, `FailCancel(key, times)`) and the fake clock of `CompensationHost`. They assert the participant's persisted file and the scope state. Unless a row says otherwise, each runs in Durable, Balanced and Ephemeral.

## Shape

The order workflow from specs 251 and 260:

```
Scope "Order": [ Reserve inventory ↔ Release inventory,
                 Reserve delivery  ↔ Cancel delivery,
                 Confirm order (fails) ]
handlerMaxAttempts 4, handlerRetryDelay 30 s, handlerRetryBackoffMultiplier 2, handlerRetryMaxDelay 90 s
```

So *Cancel delivery*'s waits are 30 s, 60 s and 90 s (capped) before attempts 2, 3 and 4.

## Backoff journeys

| ID | Setup | Clock and host | Expected participant | Expected scope |
|---|---|---|---|---|
| BK1 | `FailCancel(delivery, 1)` | Sweep at +29 s: no attempt 2. Sweep at +30 s: attempt 2 | One cancel effect; no reservations | Attempt 2 `notBefore` = fault time + 30 s; `Compensated`; 2 attempts |
| BK2 | `FailCancel(delivery, 2)` | Kill the host while attempt 3 waits; restart at +200 s | One cancel effect; no reservations | Attempt 3 ran once at the first sweep after restart; `Compensated`; 3 attempts |
| BK3 | `FailCancel(delivery, 99)` | Advance through every wait | Delivery and inventory still reserved | 4 attempts with waits 30 / 60 / 90 s; `AwaitingIntervention`; an operator retry runs at once (attempt 5, no `notBefore`) |
| BK4 | `FailCancel(delivery, 99)`; cancel the run while attempt 2 waits | Advance past the due time | Delivery and inventory still reserved | No attempt 2 handler execution; delayed item acknowledged; journal readable; run `Cancelled` |
| BK5 | `FailCancel(delivery, 1)`; republish the workflow with a different *Cancel delivery* while attempt 2 waits | Advance past the due time | The original handler's effect, once | Attempt 2 ran the pinned handler node |
| BK6 | Nested: outer [P ↔ undo P, inner (= Shape), X (fails)]; inner's attempt 2 waits when a hand-over from a Parallel sibling arrives at the outer scope | Advance | All undone once | The hand-over is journaled while the inner scope waits; nothing is held back |
| BK0 | `handlerRetryDelay` 0 (default) | — | as spec 251 J3 | Identical commits and attempt records to M4 (SC-501) |

Restart injection points for backoff (each mode):

1. After the failed attempt's commit, before its due time (BK2).
2. After the outbox sweep enqueued the delayed schedule, before the handler starts (BK1 variant).
3. After the due time passed while the host was down (BK2).

## Replay-safe warning checks (OD20 A)

| ID | Draft | Expected validations |
|---|---|---|
| W1 | A step with a compensation, forward activity Write Line | `Warning` `CompensationScope/ReplaySafeStep` at `{scope}/steps/0` |
| W2 | A handler that is Write Line | `Warning` `CompensationScope/ReplaySafeHandler` at `{scope}/steps/0` |
| W3 | A Write Line step without a compensation | none |
| W4 | A Sequence step with a compensation; a Sequence handler | none |
| W5 | The participant (`External`) as step and handler | none |
| W6 | Only warnings | Promotion succeeds; Studio's review lists them under **Warnings** and **Publish** is enabled |

## Live browser journeys (real backend)

Against a Studio and a Workbench built from the candidate revision, with `ELSA_STUDIO_URL` set. All four share `Studio/tests/live/support/journey.ts`.

| Journey | Steps |
|---|---|
| `compensation-journey.spec.ts` (M1, repaired) | Author the three-step scope with Write Line steps and handlers; open **Review & publish**; see two warnings per Write Line pair; publish; **Test Run**; inspect `Compensated`, the cause, the order and the "Compensates" link; axe |
| `nesting-journey.spec.ts` (new) | Author outer [Charge ↔ Refund, Confirm, inner scope (Reserve A ↔ Release A, Reserve B ↔ Release B), Ship (Fault)], all Write Line but Ship; publish; test run; inspect: Charge `Confirmed`, inner `Compensable by …`, **Handed over by inner scopes** with B then A `Compensated`; the inner occurrence link; axe |
| `operator-actions-journey.spec.ts` (M4) | As in spec 260, plus: with `handlerRetryDelay` 5 s, the row reads **Retry scheduled · Attempt 2 of 3** before **Retrying**, and no action is offered during the wait |
| reader step (M4) | Unchanged; runs when `ELSA_STUDIO_READONLY_USER` is set |

Any wait longer than the 30-minute test-run window uses a published run (research R10).

```bash
cd Studio && ELSA_STUDIO_URL=http://localhost:5089 npx playwright test --config tests/live/playwright.live.config.ts
```

## Provider subset

```bash
# One provider at a time, with its required-provider switch, as CI does (needs Docker)
ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1 dotnet test \
  Essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Compensation.Persistence.EntityFrameworkCore.ProviderTests.csproj
```

The subset S1–S7 is in [contracts/qualification-matrix.md](contracts/qualification-matrix.md#provider-subset). CI runs it nightly and on dispatch as EF suite `compensation`; dispatch it on the PR 2 head before merge and on the merge commit after.

## Upcast and retention

| ID | Case | Expected |
|---|---|---|
| U1–U4 | A run waiting for intervention whose committed scope state is replaced by the equivalent version 1, 2, 3 or 4 payload; restart; operator retry | Recovery finishes; the state is written back as version 5 |
| R1 | Delete a compensated (completed) run through the API | 204; its activity executions and incidents are gone, and with them its operator history |
| R2 | Delete a run that waits for intervention | Refused; nothing deleted |
| R3 | A delayed schedule pending; run the executable garbage collector | The pinned executable is kept |
| R4 | A Studio test run waits for intervention past its 30-minute window | As research R10 settles it; the guide says the same |

## Negative controls

Run once by hand on the full suites, each reverted; record the failing counts in the PR and here.

| ID | Remove | Expected failure |
|---|---|---|
| N501 | The `NotBefore` → `AvailableAt` mapping, so delayed items are available at once | D2, BK1, BK2: attempt runs before its due time |
| N502 | The hop and immediate-dispatch skip | The skip's own unit test (D2 and BK1 still pass: the in-process hop only caches payloads) |
| N503 | The parent-not-running check on delivery | D5: the handler starts under a finished parent (BK4 still passes: the terminal-run check refuses first) |
| N504 | The mandatory checkpoint for a delayed item | Its own unit test (D7 and BK2 still pass: a coalesced item keeps its due time and a crash replays the same intent) |
| N505 | The delay formula's exponent (use a fixed delay) | BK3's recorded waits |
| N506 | The warning validator, and separately Studio's severity filter | W1/W2; W6 and the M1 live journey's publish step |

### Results (PR 1, run once by hand, each reverted)

Suites: Workflows.Runtime (2,508 tests), Activities.Runtime (653) and Activities.Compensation (375, including the SQLite D2/D4 and the BK journeys); for N506 also the Workbench W6 test, the Studio Workflows Vitest suite (2,281) and the publication-review browser spec (10). Every suite passed before and after each control.

| ID | What was removed | Failing tests | Notes |
|---|---|---|---|
| N501 | `RuntimePostCommitOutboxItems.AvailableAt` returns the commit time | 50: Runtime 5 (the T501 immediate-dispatch, Balanced/Ephemeral and recovery checks, and the D9 replay), Activities.Runtime 21 (six `DelayedScheduleExecutionTests` cases in every mode, the terminal-run case for a completed and a faulted run: first-sweep delivery, other work not held back, terminal run, parent not running, a run that only waits, the child-completion path), Compensation 24 (BK1 with both restart points, BK2, BK3, BK4, BK6; SQLite D2 and D4) | As expected. |
| N502 | The committer's hop skip for a delayed intent | 1: `DelayedSchedule_TheInProcessHopDoesNotPublishANotYetDueIntent` | D2 and BK1 do **not** fail. The hop carrier is only a payload cache (R2 outcome): the drain delivers an item only once the outbox selects it, and the outbox honours `AvailableAt`. The skip is hygiene, so only its unit test guards it. The immediate dispatch has no separate skip to remove; it is guarded by `AvailableAt` itself, which N501 covers. |
| N503 | The parent-not-running check on delivery | 3: D5 (`DelayedSchedule_AParentThatIsNoLongerRunningGetsNoChild`) in every mode | BK4 does **not** fail: cancelling the run makes the run terminal, and the terminal-run check (D6) refuses the retry first. The parent check covers a parent that ended while its run still runs, which only D5 exercises. |
| N504 | The mandatory, `Immediate` checkpoint for a delayed intent | 2: `DelayedSchedule_ABalancedOrEphemeralDrainFlushesItDurablyWithItsDueTime` (Balanced, Ephemeral) | D7 and BK2 do **not** fail. Without the override the item lives in the coalescing overlay until the end-of-drain flush, which keeps its `AvailableAt`; a stop before that flush loses the whole buffered segment, and replay from the last durable checkpoint recreates the same intent, so no retry is lost. A failed handler also opens an incident, whose checkpoint is already mandatory. The override is defence in depth: the item is durable at its own commit and does not depend on the replay. |
| N505 | The exponent (a fixed delay) | 13: BK2 and BK3 in every mode (6), the formula and cap unit tests (7) | As expected. |
| N506a | The warning validator (it reports nothing when a reader is present) | Compensation 6 (W1, W2, both-at-once, nested, read-once; the no-reader case inverts), Workbench W6 1 | As expected. |
| N506b | Studio's severity filter (`isWarningSeverity` never true) | Vitest 9 (seven in `publicationReviewWarnings.test.tsx`, two in `workflowPublicationOperations.test.tsx`) | The browser spec does **not** fail: its fixture builds the review with its warnings already split, so it guards the rendering and accessibility, not the filter. The M1 live journey was not run here; it needs a live host and is broken today (#3142, R7, PR 2). |

## Run locally

```bash
dotnet test Essentials/tests/Workflows/Runtime/Tests --filter DelayedSchedule
dotnet test Essentials/tests/Activities/Runtime/Tests --filter DelayedSchedule
dotnet test Essentials/tests/Activities/Compensation/Tests
dotnet test Essentials/tests/Workbench/Tests --filter Compensation
cd Studio/src/essentials/Elsa.Studio.Workflows/Client && npx vitest run compensation publicationReview
cd Studio && pnpm test:browser
```
