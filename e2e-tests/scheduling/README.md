# Classic Delay, Timer and Cron witnesses

These REST witnesses run against the pinned classic Workbench. The deadline script
starts its own loopback host, verifies restart on a disposable SQLite store, and
runs the Delay, Timer and Cron checks in that same host.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 18a16dee878106ac525aa9a861d851a200e43c55
(
  cd "$archive/source"
  dotnet build server/apps/Elsa.Workbench.Host/Elsa.Workbench.Host.csproj -c Debug -p:RestoreLockedMode=true
  pwsh -NoProfile -File e2e-tests/scheduling/Test-DelayDeadline.ps1 -EvidencePath "$archive/scheduling.json"
)
```

| Script | What it exercises |
|--------|-------------------|
| `Test-Delay.ps1` | `Delay` - a **mid-flow** activity that durably suspends the instance for a `Duration`, then auto-resumes when the hosted durable-timer pump fires (no external stimulus). Asserts suspend, then resume-to-completion. |
| `Test-DelayDeadline.ps1` | Starts its own fresh-SQLite host and checks a five-second local deadline, persisted restart recovery, and all three scheduling scripts. Retains optional JSON evidence with activity timestamps. Build Workbench first; no external server is stopped. |
| `Test-Timer.ps1` | `Timer` - a recurring **start trigger**; the recurring-trigger pump starts a new instance every `Interval`. Asserts an instance is auto-started and completes. |
| `Test-Cron.ps1` | `Cron` - a recurring **start trigger** on a cron schedule (UTC); same pump. Asserts an instance is auto-started and completes. |
| `_SchedulingCommon.ps1` | shared helpers (node builders, pump-driven instance waiters). |

The archived shell selects `ActivitiesScheduling`. Its runtime dependencies own the
durable Timer and recurring schedules. The recurring checks allow for the pump's
roughly ten-second sweep cadence.

## Input value formats

| Activity | Input | Format | Examples |
|---|---|---|---|
| `Delay` | `Duration` | .NET `TimeSpan` string | `"00:00:02"`, `"00:05:00"`, `"1.00:00:00"` |
| `Timer` | `Interval` | ISO-8601 duration **or** `TimeSpan` (positive) | `"PT5S"`, `"PT5M"`, `"00:00:30"` |
| `Cron` | `Expression` | Cronos 5-field or 6/7-field (with seconds), UTC | `"* * * * *"`, `"0 9 * * *"`, `"*/15 * * * * *"` |

`Timer`/`Cron` inputs must be authored as **literals** (non-literal/blank values fail publish). Timer intervals must
be strictly positive; Cron is evaluated in **UTC**.
