# Bookmark expiration

> **Audience:** activity authors, workflow engineers, and runtime integrators.
> **Purpose:** explain what `BookmarkState.ExpiresAt` controls, what it deliberately does not control,
> and how to model time-bounded waits without confusing handle validity with workflow behavior.
> **Knowledge role:** worked reference. Canonical short definitions live in the
> [Elsa glossary](glossary/elsa.md); runtime extension-point contracts live in
> [`Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).

## Contract

A bookmark is a durable resume handle. Its nullable `ExpiresAt` value controls whether runtime may
match a stimulus to that handle:

- `ExpiresAt = null`: the bookmark remains matchable indefinitely.
- `ExpiresAt > evaluatedAt`: the bookmark is still matchable.
- `ExpiresAt <= evaluatedAt`: the bookmark is expired and stimulus lookup ignores it.

Equality is expired. The cutoff is evaluated when a stimulus is matched; it is not an event scheduled
for future delivery.

By itself, expiration does **not**:

- resume the workflow;
- select a timeout or expiry outcome;
- cancel the waiting activity, a branch, or the workflow;
- delete the bookmark from persistence; or
- notify the workflow that the cutoff was reached.

The default per-execution and cross-execution lookup services apply the cutoff. Raw bookmark-store
queries deliberately retain and return expired records, and bookmark persistence has no expiry index
or expiry pump. A finite expiration on an otherwise unhandled, lone bookmark can therefore leave its
activity suspended with no remaining admissible stimulus.

## Why the field exists

`ExpiresAt` is useful when the *right to resume through this particular handle* is time-bounded. It
prevents a stale or delayed external stimulus from continuing an execution after the stimulus has
ceased to be valid. That is a capability-validity rule, separate from the process engineer's decision
about what the workflow should do when time passes.

This separation keeps a bookmark passive. Time-driven workflow behavior belongs to an explicit timer
or another explicit stimulus source, whose delivery travels through the normal resume path.

## Use cases

The following finite-expiration cases are appropriate only when the workflow also has an explicit way
to progress, cancel, or be administered after the handle becomes invalid.

| Use case | What `ExpiresAt` protects | What must drive the workflow |
|---|---|---|
| Signed approval or rejection link | Rejects a click after the signed action link's validity window. | A parallel `Delay` or other explicit timeout path escalates, defaults, or cancels. |
| Partner webhook response window | Rejects a response delivered after the partner's contractual response window. | A durable timer follows the missed-response or compensation path. |
| Quote or offer acceptance | Prevents a late acceptance from reviving an offer whose terms are no longer valid. | An explicit timer closes the offer and selects the expiry outcome. |
| Temporary HTTP callback route | Stops an expired mid-flow callback bookmark from contributing a routable endpoint or matching a request. | A timer, operator action, or another bookmark moves or terminates the instance. |
| Device or browser authorization session | Rejects a callback after the short-lived authorization session expires. | The authorization subsystem signals failure, or a workflow timer chooses the fallback path. |
| One-time external correlation nonce | Bounds the lifetime during which a nonce may correlate an external message to this execution. | A separate lifecycle action retires, retries, or compensates for the unanswered request. |

Some common scenarios should not use finite bookmark expiration:

| Scenario | Correct model |
|---|---|
| Human approval that may legitimately take months or years | Use `ExpiresAt = null`. |
| "Wait for approval, otherwise escalate after two days" | Race the approval wait against a durable `Delay`; the winning path selects the business outcome. |
| A `Delay` activity's own timer bookmark | Use `ExpiresAt = null`; the durable timer owns the deadline and must still be able to match the bookmark when it fires. |
| Retention or garbage collection | Use an explicit retention/cleanup policy. `ExpiresAt` does not delete records. |
| Administrative or business cancellation | Send an explicit cancellation command and persist the resulting lifecycle transition. |

## Modeling a workflow timeout

A business timeout is active control flow, not bookmark metadata:

1. Register the external wait, usually with `ExpiresAt = null`. If late delivery must be rejected,
   give it the same business cutoff while retaining the remaining steps below.
2. Schedule a durable `Delay` for the cutoff.
3. Use a race or interrupting construct that chooses exactly one continuation.
4. When one side wins, durably cancel, consume, or otherwise invalidate the losing wait and its
   external capability.
5. Route the winning result to the authored approval, rejection, escalation, compensation, or
   cancellation outcome.

An ordinary fork followed by an ordinary merge is not sufficient unless that construct guarantees a
single winner and losing-branch cleanup. Without those semantics, a late external stimulus can still
continue the other branch or leave an orphaned bookmark.

## Durable timers

A durable timer and its bookmark are complementary:

- the timer owns *when* a stimulus is emitted;
- the bookmark owns *where* that stimulus resumes execution.

The timer bookmark must remain matchable when the timer fires. Setting its `ExpiresAt` equal to the
timer's due time makes it expired exactly when dispatch evaluates it, producing `NotFound` instead of
resumption. See [Durable timers and the `Delay` activity](runtime-durable-timers.md) for the complete
suspend/resume cycle.

## Runtime evidence

- [`BookmarkStimulusLookup`](../src/Elsa/Workflows/Runtime/Services/BookmarkStimulusLookup.cs) applies
  the per-execution cutoff.
- [`GlobalBookmarkStimulusLookup`](../src/Elsa/Workflows/Runtime/Services/GlobalBookmarkStimulusLookup.cs)
  applies the same cutoff to cross-execution and type-scoped lookup.
- [`BookmarkResumeDispatcher`](../src/Elsa/Workflows/Runtime/Services/BookmarkResumeDispatcher.cs)
  evaluates lookup against the runtime clock and treats an expired bookmark as not found.
- [`ElsaRuntimeStorageManifest`](../src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs)
  declares bookmark indexes by workflow execution and stimulus identity, not by expiration.
- [`RuntimeBookmarkStimulusResumeDispatchTests`](../tests/Elsa/Workflows/Runtime/Tests/RuntimeBookmarkStimulusResumeDispatchTests.cs)
  and [`GlobalBookmarkStimulusLookupTests`](../tests/Elsa/Workflows/Runtime/Tests/GlobalBookmarkStimulusLookupTests.cs)
  pin the non-expired matching rule.
