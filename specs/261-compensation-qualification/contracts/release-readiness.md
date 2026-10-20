# Contract: the qualification and release-readiness record

PR 3 writes `docs/reports/native-compensation-qualification.md` from this outline and lists it in `docs/reports/README.md`. The lists below are its draft content, collected from specs 250, 251, 258 and 260 and from this packet's research. PR 2 and PR 3 update them with what qualification finds. Every claim names its proof, or it is not made.

## Outline

1. **Scope and candidate.** The program, milestones M1–M5 with their specs and merged PRs, and the candidate revision (Foundation `main` commit; Studio is in the same repository). A plain statement: *"This record qualifies the integrated candidate. No package was released, no image was published and nothing was deployed; releases and deployment are outside this program by owner decision."*
2. **Qualification matrix.** The rows of [qualification-matrix.md](qualification-matrix.md) with their recorded results (revision, job or command, count, notes).
3. **Supported combinations.** The list below, as a table.
4. **Not qualified.** The list below; each with why and what qualifying it would take.
5. **Known limits.** The list below; each with its source and whether a follow-up exists.
6. **Open follow-ups.** Issues, each with its state at the time of writing.
7. **Review findings.** What the M5 review of the M1–M4 runtime and Studio code found and how each was resolved or why it was filed instead (FR-521).
8. **Evidence.** Negative controls run (N501–N506 and the earlier specs' records), live journey runs with both revisions, the post-merge `main` CI run and the dispatched nightly EF run on the merge commit (linked to the #3048 comment).

## Supported (claimed once its row passes)

| Combination | Rows |
|---|---|
| SQLite, Durable / Balanced / Ephemeral | Q1–Q3, Q8–Q11 |
| PostgreSQL, SQL Server, MySQL, Durable / Balanced / Ephemeral (OD18 A) | Q4–Q6 (provider subset), plus Q1 semantics |
| Studio authoring, publish, run, inspect, nested view, operator recovery | Q12–Q15 |
| Single-node hosting (one Workbench process, or several processes over one store with the existing claims and fences) | Q3 |

## Not qualified

| Combination | Why | To qualify |
|---|---|---|
| In-memory stores | No durability: a restart loses the run | Not applicable; development and tests only (Q7 smoke) |
| Oracle | No provider in CI (`docs/reports/ef-core-oracle-scoping-2026-08.md`) | A provider suite plus the subset |
| Multi-node placement (`runtime-distributed`) | No compensation journey with placement and handover | The subset under the distributed placement fixture |
| Studio test runs that wait longer than 30 minutes | Settled in PR 2 (research R10) | — |
| Live browser journeys in CI | Opt-in by design (default in spec 261) | A CI job that starts a Workbench and a Studio |

## Known limits (draft)

| Limit | Source |
|---|---|
| A failure inside a Sequence inside a Flowchart step never reaches the scope; the run faults and nothing is undone | Spec 258 R-3 known limit, S2 |
| A compensation that can be handed over and reads a container's variable from a *script* expression is not checked at publication; it fails when it runs and waits for an operator | Spec 258 R18 (T324 checks authored references only) |
| Steps run one at a time; there are no parallel steps inside one scope (branch scopes inside a Parallel are supported) | Spec 250 FR-002 |
| Nothing crosses a reusable activity's boundary: no hand-over, Confirm or Compensate | Spec 258 defaults |
| Only the nearest enclosing scope can undo a completed scope; there is no workflow-wide or cross-workflow registry | Spec 258 OD8 |
| Cancellation never compensates; a compensation already running may finish | Spec 258 OD10, R20 |
| Completing the workflow confirms nothing; unconfirmed, uncompensated entries end as kept | Spec 258 defaults |
| A completed scope keeps no journal; Studio derives its view from run data | Spec 258 R1, R22; spec 260 FR-417 |
| An expression `scope` input on Confirm or Compensate is not resolved in the Studio run view | Spec 258 R22 |
| Only leaf handlers are key-stable across retries; activities inside a composite handler get their own default keys | Spec 251 edge cases |
| No exactly-once effects and no reconciliation framework; participants must follow the participant contract | Spec 251 FR-220, program non-goals |
| Operator reasons are visible to every reader of the run | Spec 260 R17 |
| Bulk retry, cancel-run and variable edits from Studio stay parked | Spec 260 OD12, ADR 0080 D0 |
| Retrying a terminal (faulted) run is out of scope | Spec 251 FR-211 |
| A handler retry delay is a minimum: a due retry runs at the next outbox sweep (10 s by default) | Spec 261 research R3 |
| No jitter, no per-handler backoff, no "retry now" during a wait | Spec 261 out of scope |
| The replay-safe warning covers leaves only; a `ReplaySafe` activity inside a composite step does not warn | Spec 261 OD20 A |
| Live-instance migration of a running scope to a new workflow version | Program non-goal (runs keep their pinned executable) |

## Open follow-ups (draft)

| Issue | State at writing | Note |
|---|---|---|
| [#3142](https://github.com/elsa-workflows/elsa-foundation/issues/3142) | Open | M1 live journey; fixed in PR 2 (T518) |
| [#3130](https://github.com/elsa-workflows/elsa-foundation/issues/3130) | Open | Diagnostics `canManage`; fixed in PR 2 (T525) |
| [#3140](https://github.com/elsa-workflows/elsa-foundation/pull/3140) | Open PR | M4 Studio; a prerequisite of PR 2 |
| [#2747](https://github.com/elsa-workflows/elsa-foundation/issues/2747) | Studio slice closes with #3140 | Operator retry |
| [elsa-core#4015](https://github.com/elsa-workflows/elsa-core/issues/4015), [elsa-core#8884](https://github.com/elsa-workflows/elsa-core/issues/8884) | External | Elsa 3 follow-on; see the assessment |
| New issues from PR 2's review (FR-521) and R10 | — | Filed with `needs-triage` and listed here |
