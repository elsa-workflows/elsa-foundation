# Contract: recovery and operator actions

Splits today's conflated behavior (an operator retry gets a new id but copies the old snapshot) into distinct actions, as
ADR 0092 decision 8 states. Amends ADR 0045 §3. The API and Studio name the three operator actions distinctly so "retry"
never silently means fresh inputs.

## Recovery (not an operator action)

Resumes the same invocation from durable state; keeps `InvocationId` and the committed input snapshot and read set.
Covers crash redelivery before commit and resume after suspension. A durably completed invocation is never re-run
because downstream scheduling was interrupted.

## Retry (operator)

- Keeps `InvocationId`, operation keys derived from it, and the committed input snapshot.
- Starts a new attempt with no session reads.
- After a lost-update fault, it reads the same base and faults again while the base stays stale and changed, unless the
  node uses `LastWins`.
- Use: transient failure (e.g. HTTP 503) where an external service deduplicates on the same idempotency key.

## Re-execute / reschedule (operator)

- Creates a **new** invocation: new `InvocationId`, inputs materialized from current committed state, new input read set
  — as Set/Merge/Reduce successors already do.
- External effects repeat on purpose; a reschedule through the alterations API (ADR 0049), including mid-workflow, is a
  re-execution (D-2: "a re-run is a new execution with new values").
- Use: a variable corrected with `ModifyVariable` must reach the step; a lost-update fault must be recoverable without
  reading the same outdated base.

## Accept recorded completion (operator)

- For a step whose body succeeded but whose commit conflicted (a lost-update fault).
- Does **not** run the body. Revalidates current state, scope, pinned contracts and policies; applies the complete
  recorded transition (write intents, captures, output changes, outcomes) atomically and duplicate-safely, as a recorded
  override like `LastWins`.
- Refuses a faulted body. A returned result alone is not enough; the completion data is retained under the existing
  protection rules. The original conflict and resolution stay in the history.
- Repeated acceptance requests must not schedule downstream work twice. Does not silently refresh inputs.

## Successor-staging fix

- `ActivitySuccessorStaging.NewSuccessor` no longer gives an operator retry a new id with a copied snapshot.
- A successor that re-enters input materialization must not activate its owned frames a second time (guard).

## Test hooks (acceptance)

- `ModifyVariable` then re-execute → corrected value used.
- Retry → same `InvocationId` + snapshot kept; new attempt.
- Accept recorded completion → transition applied once, no body, revalidated, duplicate-safe, faulted body refused.
- Re-entered successor → owned frames not re-activated.
