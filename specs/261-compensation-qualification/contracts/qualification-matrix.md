# Contract: qualification matrix

Each row is one support claim. A row marked **supported** is claimed only once its proof passes on the candidate revision. The record (`docs/reports/native-compensation-qualification.md`) carries, for each row: the revision, the job or command, and the count. A row that cannot pass loses its claim; it is moved to **not qualified**, never silently downgraded (spec 250 FR-013).

"Existing" means the proof is on `main` at `7fb745f86`, or in #3140 for Studio. "M5" means this spec adds it. CI jobs: **B&T** is `CI / Build & test` (every PR and push), **EF** is `CI / EF container suites` (nightly and dispatch), **Studio** is `Studio CI` (every PR), **Arch** is `CI / Architecture guards`, **Maps** is `CI / Generated maps fresh`, **Live** is a run by hand against a recorded Studio and Workbench pair.

## Matrix

| Row | Claim | Proof | Where | CI | Today | M5 work |
|---|---|---|---|---|---|---|
| Q1 | Every journey of M1–M4 in Durable, Balanced and Ephemeral on SQLite | P1–P4, J1–J6, N1–S3, O1–O8, J1 (journal) with their restart points | `Essentials/tests/Activities/Compensation/Tests` | B&T | **Existing** | Re-run on the candidate; add BK1–BK6 |
| Q2 | Restart injection, every documented point | Spec 250 P1–P4, spec 251 FR-216 1–4, spec 258 points 1–4, spec 260 O8 1–3 | as Q1 | B&T | **Existing** (SQLite) | BK restart points; the provider subset repeats one point per family |
| Q3 | Real process kill through REST | `WorkbenchCompensationRecoveryTests` (4 journeys) | `Essentials/tests/Workbench/Tests` | B&T | **Existing** | Add a backoff journey with a kill while waiting; add the sample test (OD19 A) |
| Q4 | PostgreSQL in each mode | Provider subset | new `Essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests` | EF (`compensation`) | Runtime stores only (`runtime` suite) | **M5** |
| Q5 | SQL Server in each mode | Provider subset | as Q4 | EF | Runtime stores only | **M5** |
| Q6 | MySQL in each mode | Provider subset | as Q4 | EF | Runtime stores only | **M5** |
| Q7 | In-memory stores: development and tests | Smoke without restart: J1, N1, O2h | `Activities.Compensation.Tests`, in-memory host | B&T | Seam only (`Activities.Runtime.Tests` harness) | **M5** smoke; claim is "no restart guarantee" |
| Q8 | Scope state upcast v1→v5 | Payload upcasts | `CompensationScopeUncertaintyTests`, `CompensationScopeDecisionTests`, nesting tests | B&T | **Existing** v1→v4 | v4→v5 unit; **M5** resume-from-older-version integration (FR-518) |
| Q9 | Serialization of wire contracts | `compensation.journal/1`, `RetryFaultedActivity/1`, `DecideIntervention/1`, outcome metadata keys | projector tests, runtime alteration tests | B&T | **Existing** | Journal `notBefore` field; private-state round trip on each server provider (subset S6) |
| Q10 | Executable pinning | Republish mid-recovery keeps the pinned handlers | `CompensationScopeRestartTests.A_republish_mid_recovery_keeps_the_handlers_of_the_pinned_executable` | B&T | **Existing**, Durable only | Run it in all three modes; BK5 (republish while a retry waits) |
| Q11 | Retention | Completed scope's journal not served (`StateCleared`); compensated run deleted with its incidents; running run refused; executable kept for a pending delayed item; test-run window behaviour | runtime API tests; new compensation API tests | B&T | **Existing**: `StateCleared` (spec 260 T413) | **M5**: the other four (FR-519, research R10) |
| Q12 | Studio authoring, publish, run, inspect (live) | `compensation-journey.spec.ts` | `Studio/tests/live` | Live | **Broken** (#3142) | Repair on shared helpers; asserts the FR-014 warnings |
| Q13 | Studio nesting and Confirm view (live) | `nesting-journey.spec.ts`: outer scope with an inner scope step and a Confirm, rejected last step | `Studio/tests/live` | Live | Mocked only (`compensation-scope.spec.ts`) | **M5** |
| Q14 | Studio operator recovery (live) | `operator-actions-journey.spec.ts` | `Studio/tests/live` | Live | **Existing** in #3140 | Re-run on the candidate; add the backoff step (Retry scheduled, then Retrying) |
| Q15 | Studio states, dialogs and axe (mocked) | Vitest, `compensation-scope.spec.ts` at 390/1200 px, light and dark | `Studio/` | Studio | **Existing** (#3140: 74/74) | Retry scheduled state; warnings in publication review |
| Q16 | Negative controls | M1 (4), M2 (4), M3 (6), M4 (8) by hand | quickstarts of specs 250–260 | by hand | **Existing**, recorded per spec | **M5** controls N501–N506 ([quickstart](../quickstart.md#negative-controls)) |
| Q17 | Architecture, maps, current head | Architecture guards, generated-maps check, CI green on the exact head; post-merge `main` CI and a dispatched EF run on the merge commit | — | Arch, Maps, B&T, EF | Per PR | Record per PR and post-merge (#3048 comment) |
| Q18 | Oracle | — | — | — | No provider in CI | **Not qualified** |
| Q19 | Multi-node placement (`runtime-distributed`) | — | — | — | No compensation journey | **Not qualified** |
| Q20 | Compensation inside a Studio test run beyond 30 minutes | Research R10 | — | — | Unknown | Settled and documented in PR 2 |

## Provider subset

Run in each of PostgreSQL, SQL Server and MySQL, in Durable, Balanced and Ephemeral (Q4–Q6). Each row names its source journey; the assertions are the source's, on the participant's file and the scope state.

| ID | Source | Restart point | Why it is in the subset |
|---|---|---|---|
| S1 | Spec 250 P2: handler killed after its effect | after the effect, before the completion commits | Checkpoint ordering and the pre-effect claim on the provider |
| S2 | Spec 251 J4 → J5: wait, then retry twice with one key | while waiting | Incident outcome write-once rule, alteration idempotency |
| S3 | Spec 258 N1: nested hand-over | after the hand-over commits, before the outer scope journals it | Ancestor notification ordering |
| S4 | Spec 260 O2h + O8 point 2: resolve with a kill before delivery | after the alteration checkpoint | Decision delivery work item |
| S5 | Spec 260 O4: abandon | — | Fault propagation and the run's terminal state |
| S6 | Spec 258 L1 with 50 iterations | — | Private-state JSON size and round trip |
| S7 | BK2: backoff with a kill while waiting | while the retry waits | `AvailableAt` on the provider's outbox |

## Recorded result format

```
| Row | Revision | Job / command | Result | Notes |
| Q4 | <sha> | CI run <id>, EF container suites (compensation) | 21/21, 0 skipped | ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1 |
| Q12 | Studio <sha>, Workbench <sha> | npx playwright test --config tests/live/playwright.live.config.ts compensation-journey.spec.ts | 1/1 | axe clean at 390/1200 |
```
