# Data model: qualifying native compensation (M5)

Extends [spec 260 data-model.md](../260-compensation-operator-actions/data-model.md). The authored step structure is unchanged, and so is every table: there is no schema migration. What changes is three scope inputs, a scope state version, one runtime intent field, one additive journal field and new validation codes.

## Scope inputs (new, OD17 A)

| Input | Type | Default | Rule |
|---|---|---|---|
| `handlerRetryDelay` | duration | `00:00:00` | The wait before the second attempt. `0` retries at once (M2–M4 behaviour). Literal: 0 to 24 h. |
| `handlerRetryBackoffMultiplier` | number | `2` | Growth per further attempt. Literal: at least 1. |
| `handlerRetryMaxDelay` | duration | `00:05:00` | Cap on any one wait. Literal: at least `handlerRetryDelay`, at most 24 h. |

Wait before attempt n+1, where n failed attempts are recorded for the entry:

```
wait(n) = min(handlerRetryMaxDelay, handlerRetryDelay × handlerRetryBackoffMultiplier^(n − 1))
```

Example (delay 30 s, ×2, cap 5 min, `handlerMaxAttempts` 5): waits of 30 s, 60 s, 120 s and 240 s before attempts 2 to 5. An expression value outside a literal's range is clamped into it at run time, and the attempt records `delayClamped: true`.

All three inputs are pinned at publication like every scope input; a run in flight never gains a delay.

## Scope private state version 5

| Field | Change |
|---|---|
| `version` | `5`. The persister reads 1 to 5 and writes 5. `CompensationScopeStateUpcaster.FromVersion4` adds no data: no older payload carries a scheduled retry. An unknown version throws, as today. |
| `entries[].handlerAttempts[].notBefore` | New, optional. Set on an attempt scheduled through R-7; absent for an immediate attempt and for every attempt upcast from versions 1 to 4. |
| `entries[].handlerAttempts[].delayClamped` | New, optional, `true` only when FR-509's clamp applied. |

An attempt waiting for its due time is `{ result: Running, notBefore: t }` on an entry `Compensating`; it becomes an ordinary running attempt when its handler starts. No status or phase is added.

## Runtime additions (R-7)

| Item | Value | Where |
|---|---|---|
| `RuntimeChildActivityScheduleRequest.NotBefore` | optional due time | `Workflows/Runtime/Core/Models/RuntimeChildActivityScheduleRequest.cs` |
| `RuntimePostCommitIntent.NotBefore` | optional; becomes the outbox item's `AvailableAt` | `Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs` |
| `RuntimeDelayedScheduleOptions.MaxDelay` | `24:00:00` | new options class |
| `IRuntimeDelayedScheduleCapability` | marker, registered with the outbox sweep | `Workflows/Runtime/Core/Contracts` |
| Fault codes | `runtime.delayed-schedule.too-far`, `runtime.delayed-schedule.unavailable` | runtime constants |

The outbox item and the scheduler work item keep their shapes. `AvailableAt` already exists on the outbox item and in every store.

## Journal projection

`compensation.journal/1` stays version 1. Each attempt gains the optional fields `notBefore` and `delayClamped` (additive; readers ignore unknown fields). Studio derives:

| Journal | Studio state | Label (text + icon) |
|---|---|---|
| attempt `Running`, `notBefore` > now | `RetryScheduled` | "⏲ Retry scheduled (1st) · Attempt 2 of 3 · after 10:42:30" |
| attempt `Running`, `notBefore` ≤ now, handler not started | `RetryScheduled` | "⏲ Retry due (1st) · Attempt 2 of 3" |
| attempt `Running`, handler started | `Retrying` (M4) | unchanged |

When the journal is unavailable (a completed scope), the M1–M3 derivation has no `notBefore` and shows attempts as in M4.

## Validation codes (new)

| Type | Severity | Path | Message |
|---|---|---|---|
| `CompensationScope/InvalidRetryDelay` | Error | `{scope}/inputs/handlerRetryDelay` | "Handler retry delay must be between 0 and 24 hours." |
| `CompensationScope/InvalidBackoffMultiplier` | Error | `{scope}/inputs/handlerRetryBackoffMultiplier` | "Handler retry backoff multiplier must be at least 1." |
| `CompensationScope/InvalidRetryMaxDelay` | Error | `{scope}/inputs/handlerRetryMaxDelay` | "Handler retry max delay must be at least the retry delay and at most 24 hours." |
| `CompensationScope/ReplaySafeStep` | Warning | `{scope}/steps/{index}` | "\"{step}\" is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none." |
| `CompensationScope/ReplaySafeHandler` | Warning | `{scope}/steps/{index}` | "The compensation of \"{step}\" is marked safe to repeat, so an undo may run again after a restart without a committed claim. Mark it as an external effect unless it is idempotent and has no effect of its own." |

## Side-effect profile reader (FR-513)

```csharp
// Activities/Runtime/Core/Contracts/IActivitySideEffectProfileReader.cs (new)
public interface IActivitySideEffectProfileReader
{
    /// The profile the executable compiler would compile for this activity version, or null when it cannot be resolved.
    ValueTask<SideEffectProfile?> ReadAsync(string activityVersionId, CancellationToken cancellationToken = default);
}
```

One implementation, in Publishing, shares the attribute rule with `ExecutableNodeCompiler.ResolveSideEffectProfile`. Reusable (graph) activities resolve to `External`; unknown versions to null.

## Studio validation model

| Type | Change |
|---|---|
| `ValidationError` (`workflowTypes.ts`) | gains `severity?: "Error" \| "Warning" \| string \| null`; a missing value reads as `Error`. |
| Publication review | splits entries into errors (blocking) and warnings (listed, non-blocking). |

## Qualification test configuration (new)

| Variable | Effect |
|---|---|
| `ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS` | The `compensation` EF container suite fails, rather than skips, when a provider container is unavailable. Set by CI from `tools/ci/ef-suites.json`. |
| `ELSA_STUDIO_URL`, `ELSA_STUDIO_USER`, `ELSA_STUDIO_PASSWORD`, `ELSA_STUDIO_READONLY_USER`, `ELSA_STUDIO_READONLY_PASSWORD` | Unchanged from M4; the live journeys skip without `ELSA_STUDIO_URL`. |
