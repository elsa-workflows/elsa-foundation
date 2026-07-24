# scheduling - Delay / Timer / Cron

Backend tests for the three `Elsa.Activities.Scheduling` activities, driven through the REST API.

| Script | What it exercises |
|--------|-------------------|
| `Test-Delay.ps1` | `Delay` - a **mid-flow** activity that durably suspends the instance for a `Duration`, then auto-resumes when the hosted durable-timer pump fires (no external stimulus). Asserts suspend, then resume-to-completion. |
| `Test-Timer.ps1` | `Timer` - a recurring **start trigger**; the recurring-trigger pump starts a new instance every `Interval`. Asserts an instance is auto-started and completes. |
| `Test-Cron.ps1` | `Cron` - a recurring **start trigger** on a cron schedule (UTC); same pump. Asserts an instance is auto-started and completes. |
| `_SchedulingCommon.ps1` | shared helpers (node builders, pump-driven instance waiters). |

## Required server features (opt-in)

These activities are **discoverable in the design catalog by default, but do not run** unless the scheduling
features are composed - a `Delay` faults at construction (`IDurableTimerScheduler` unresolved) and `Timer`/`Cron`
never fire. The `src/Apps/Elsa.Server/shells.json` default shell must enable:

```json
"ActivitiesScheduling": {},
"WorkflowsRuntimeScheduling": {},
"WorkflowsRuntimeRecurringTriggers": {}
```

`ActivitiesScheduling` depends on both runtime features; the runtime features register the `IDurableTimerStore` /
`IDurableTimerScheduler` + `DurableTimerPumpTask` and the recurring schedule store + `RecurringTriggerPumpTask`
respectively. Both pumps sweep roughly every 10s, so a fire lands within `duration|interval|occurrence` **+ up to
~10s** of sweep jitter (the tests budget for this).

## Input value formats

| Activity | Input | Format | Examples |
|---|---|---|---|
| `Delay` | `Duration` | .NET `TimeSpan` string | `"00:00:02"`, `"00:05:00"`, `"1.00:00:00"` |
| `Timer` | `Interval` | ISO-8601 duration **or** `TimeSpan` (positive) | `"PT5S"`, `"PT5M"`, `"00:00:30"` |
| `Cron` | `Expression` | Cronos 5-field or 6/7-field (with seconds), UTC | `"* * * * *"`, `"0 9 * * *"`, `"*/15 * * * * *"` |

`Timer`/`Cron` inputs must be authored as **literals** (non-literal/blank values fail publish). Timer intervals must
be strictly positive; Cron is evaluated in **UTC**.
