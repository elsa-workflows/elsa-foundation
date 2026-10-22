# Contract: guides and samples

## Audit

Audit of `website/docs/essentials/compensation.md` on `main` at `7fb745f86`, plus the "In Studio" section and states the M4 Studio branch adds (#3140). Each row is resolved in PR 3 by the action given, and the proof column names the test that backs the statement afterwards. "Keep" means the statement matches shipped behaviour.

| # | Section | Finding | Action | Proof |
|---|---|---|---|---|
| A1 | Intro, Author a scope | Matches M1 authoring | Keep | M1 live journey (Q12) |
| A2 | What runs, and when | "runs it again straight away" becomes untrue only when an author sets a delay | Add the three backoff inputs and the formula; say the delay is a minimum (one outbox sweep, 10 s by default) | BK1–BK6 |
| A3 | Crashes and repeated calls | Correct, but silent on `ReplaySafe` steps and handlers | Add the FR-014 warning and why to heed it | W1–W6, Q12 |
| A4 | What a participant must do | Matches spec 251 FR-220 | Keep; link the sample participant as the reference implementation | Sample test |
| A5 | Pass data from a step to its compensation | Teaches only a workflow variable; shipped behaviour also binds a handler input to the paired step's result through causal lineage (spec 250 FR-010, FR-016; `WorkbenchCompensationRecoveryTests` uses `ActivityResultArgument`). In a loop the variable is overwritten by later iterations | Lead with the step-result reference that the expression editor offers first; keep the variable as an alternative with the loop warning | Q3, sample S2 |
| A6 | Uncertain outcomes | The code sample is not compiled anywhere | Take it from the sample participant so it compiles in CI | Sample build |
| A7 | Act on a waiting recovery | Matches M4 API and (after #3140) Studio | Keep; mention that waits between automatic attempts offer no action | Q14 |
| A8 | Nest scopes and run them in loops | Matches M3 | Keep | Q1 (N1–L2, B1–B2) |
| A9 | Confirm, Compensate, Cancellation | Matches M3 | Keep; move the definitions into the four-way table and link it | Q1 (C1–C4, E1–E3, X1) |
| A10 | Which steps report failures | Matches the composition matrix and its known limit | Keep | Q1 (S1–S3) |
| A11 | Inspect a run | Missing the new state | Add **Retry scheduled** | Q15 |
| A12 | Current limits | Lists two limits M5 removes (delay between retries, replay-safe warning) and none of the qualification limits | Remove the two; add the not-qualified rows (Oracle, multi-node), in-memory = no restart guarantee, the test-run window (R10), the due-time precision | Qualification record |
| A13 | — | No place says what is *not* compensation | Add the four-way table below | — |
| A14 | Glossary | "Reconciliation" means catalog reconciliation (`docs/glossary/elsa.md:84`) | Add "Effect reconciliation (compensation)" and a "not to be confused with" pointer on both rows; add "Scheduled retry (compensation)" and "Replay-safe warning" | Glossary link check |
| A15 | Persistence modes guide | Says `External` work is committed before it runs; does not link compensation | Link the FR-014 warning from the `ReplaySafe` paragraph | Docs link check |

## Compensation, cancellation, reconciliation and confirmation

The guide's new section "Undo, stop, settle or keep", one table, linked from each concept's own section:

| | Compensation | Cancellation | Reconciliation | Confirmation |
|---|---|---|---|---|
| **What it does** | Undoes completed steps, newest first, by running their compensations | Stops forward work | Settles a step whose effect may or may not exist | Marks completed work as never to be undone |
| **Started by** | A step that fails; a **Compensate** activity; a later failure in an enclosing scope that received this scope's work | Cancelling the run (API or Studio) | A step that reports `outcome-unknown` | A **Confirm** activity; `confirmOnComplete` |
| **Runs compensations?** | Yes, one at a time, with retries and the optional wait between them | Never; a compensation already running may finish | The step's compensation, which must work whether or not the effect exists; with none, an operator decides (**Resolve**) | No; recovery skips confirmed steps later |
| **How it ends** | `Compensated`, or the step's failure re-raised (`faultAfterCompensation`), or **Needs action** for an operator | `Cancelled`; the journal stays readable | The entry reads `Compensated` (marked uncertain) or `Resolved: effect absent` / `Resolved: effect kept` | The entry reads `Confirmed` |
| **Elsa does not** | Undo across a reusable activity's boundary or across workflows | Undo anything | Provide a reconciliation framework or query the participant; the participant contract and operation keys are the tools | Confirm across a reusable activity's boundary |

## Samples (OD19 A)

### Package

`samples/Elsa.Samples.Compensation/`, a Nuplane package built like `samples/Elsa.Samples.Nuplane.Activities`:

| Item | Content |
|---|---|
| Feature | `SampleCompensation` (depends on `ActivitiesCompensation`), with a `Directory` setting for the participant's ledger |
| `Reserve` activity | Inputs `Resource`, `Reject`, `LoseResponse`. Applies a reservation at most once per `OperationKey`, returns the original result on a repeat, refuses a key that has a tombstone, faults `outcome-unknown` when `LoseResponse` is set (after applying). `External` (the default profile). |
| `Release` activity | Inputs `FailTimes`. Undoes the reservation for `TargetOperationKey` when it exists; otherwise writes a tombstone for it; idempotent per `OperationKey`; fails `FailTimes` times first. `External`. |
| Ledger | One JSON file per resource under `Directory`, written atomically; readable by the README's walkthrough and by the test. |
| `workflows/` | `reservation-saga.json`, `nested-confirm.json`, `operator-recovery.json` (below), each a body for `POST design/workflows/definitions/submit`. They name activities by type key; the import step resolves each to its catalog version id through `GET design/activities/definitions?search=`, as the Workbench tests do, because version ids are catalog-specific. |
| `README.md` | Build and drop (as the Nuplane sample), enable the feature, import, run, what to expect in Studio and in the ledger. |

The Workbench test witness `Essentials/tests/Workbench/Fixtures/Reservations` keeps its test-only hooks (holds, kill points); the sample copies its participant logic without them.

### Workflows

| ID | Workflow | Run | Expected ledger | Expected Studio |
|---|---|---|---|---|
| S1 | **Reservation saga**: scope [Reserve inventory ↔ Release inventory, Reserve delivery ↔ Release delivery, Confirm order (Reject)] | Run once | No reservation left; two releases | Scope `Compensated`; cause "Confirm order failed"; recovery order 1st delivery, 2nd inventory |
| S2 | **Nested scope with Confirm**: outer "Order" [Charge ↔ Refund, Confirm, inner scope (Reserve A ↔ Release A, Reserve B ↔ Release B), Ship (Reject)]; the handler inputs bind to the step results. Confirm comes before the inner scope because it confirms everything the scope could undo so far, handed-over entries included | Run once | A and B released; the charge kept | Charge `Confirmed`; inner `Compensable by Order`, then its handed-over entries undone B then A; outer `Compensated` |
| S3 | **Operator recovery**: S1 with Release delivery `FailTimes` 99, `handlerMaxAttempts` 3, `handlerRetryDelay` 5 s | Run, wait, then Retry, Resolve or Abandon in Studio | Depends on the action, as in spec 260 O1, O2h, O4 | **Retry scheduled** twice, then **Needs action**; the action dialogs |

### Proof

`Essentials/tests/Workbench/Tests/WorkbenchCompensationSampleTests.cs` packs the sample into the owned feed the Reservations witness uses, enables `SampleCompensation`, imports the three Drafts, publishes, runs S1 and S2 to the end, runs S3 to **Needs action** and resolves it `effect-absent` over REST, and asserts each expected ledger and scope outcome. It runs in **B&T** on every PR, so the sample cannot drift from shipped behaviour. The README's Studio steps are walked once by hand for the record (Q12–Q14).

## Guide and glossary changes, by PR

| PR | Change |
|---|---|
| 1 | Backoff inputs and formula (A2); FR-014 warning (A3, A15); **Retry scheduled** (A11); remove the two limits (A12, part); glossary "Scheduled retry", "Replay-safe warning"; `EXTENSION_POINTS.md` for R-7 and the reader contract |
| 2 | Not-qualified rows, the in-memory claim and the test-run window in current limits (A12, rest) |
| 3 | A5, A6, A13 (the table), A14 (reconciliation rows), sample links, the "Samples" section |
