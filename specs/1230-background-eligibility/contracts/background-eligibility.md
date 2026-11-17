# Contract: background eligibility

Applies ADR 0093. Replaces the eligibility paragraphs of spec 241's `contracts/background-activity.md`, which points
here once this spec is implemented.

## Who decides

| Party | Decides |
|---|---|
| Architects | the criteria (ADR 0093) |
| Activity author | that the type meets them: `[BackgroundExecutionSafe]` |
| Workflow author | per node: `ExecutionPlacement = Background` |
| Host | that background execution is on: the `BackgroundActivities` feature |

## The component

`BackgroundEligibility` (`Elsa.Workflows.Runtime.Core`):

- `CheckPublication(subject)` — C1–C5, C9, C10. Called by `ExecutableNodeCompiler` for every node placed in the
  background.
- `CheckRunTime(subject)` — C1–C5, C9, C10, and C6 when the subject carries staged effects. Called at admission
  (`BackgroundActivityCoordinator`), before activation and after the body on the worker
  (`BackgroundActivityWorker`).

Both return the first refusal in criterion order, or none. Callers throw; no other code decides eligibility.

## Refusal

`VF-BG-001 (C<n>): Background execution is not eligible for activity node '<id>': <reason>.`

- At publication: the publish fails, as today.
- At run time: the step faults with an incident carrying the same text (Q5).

## The declaration

`[BackgroundExecutionSafe]` documents:

- **C7** — the body may run more than once after a lost worker; its external effects are idempotent or keyed on
  `InvocationId`.
- **C8** — the body observes its cancellation token and stops.
- The engine checks C1–C6, C9 and C10; C7 and C8 are the author's promise.
- A subclass inherits the declaration and the promise (Q6).
