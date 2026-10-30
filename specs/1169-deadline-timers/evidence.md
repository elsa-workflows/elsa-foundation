# Qualification evidence

Base: `931123c7ed45aa896539d857c6227a267d08b28f`. Issue #3478. Branch `agent/codex/3478-deadline-timers`.

## Local correctness

Whole-project runs, zero skips: Runtime Scheduling 101, Runtime 3,051, Activities Scheduling 25, Tasks 71, Runtime EF 1,725, Runtime EF native providers 191, Architecture 592. Native providers ran with `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS=1` against PostgreSQL, MySQL and SQL Server. The new provider smoke assertions exercise empty, pending and leased deadline queries.

`dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` passed. Architecture project graph restore and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passed. Public API additions are tracked; the old scheduler constructor and default store behavior remain compatible. No project reference, dependency or persistence schema change.

## Mutation control

Replacing the deadline schedule's `NextWakeup` with the recovery instant alone made five existing new tests fail: five-second startup, earlier insertion, peer discovery, claim expiry and retained notification. The mutated run executed 97 cases: 5 failed/92 passed. Restoring deadline selection passed all 97, and the final two partition/retry regressions expanded the passing whole suite to 99. Earlier qualification failures were an invalid stub `Dispatched` result missing mandatory dispatch facts and tests observing an asynchronous claim before settlement; the stub now returns a valid accepted-duplicate result and the fake-time rig awaits schedule quiescence.

## Rebuilt host

`pwsh -NoProfile -File e2e-tests/scheduling/Test-DelayDeadline.ps1 -EvidencePath <private-json>` passed on a private port and fresh SQLite content root, using normal committed composition. The script stopped only its own host processes.

| Path | Delay started (Europe/Amsterdam) | Following activity started | Elapsed | Outcome |
| --- | --- | --- | --- | --- |
| Local | 2026-10-30 15:50:55.722924 | 15:51:01.121351 | 5.398427 s | Completed once |
| Persisted restart | 2026-10-30 15:51:01.318503 | 15:51:08.933010 | 7.614507 s, including restart | Completed once |

The same owned server passed the original Delay, Timer and Cron scripts. Restart startup crossed the five-second deadline, so this is overdue recovery evidence; deterministic startup tests and the EF future-deadline query cover early discovery of future persisted timers. This is a finite correctness witness under the observed load, not a latency guarantee or benchmark.

## Review and delivery

Initial independent review found disconnected replacement notification wiring and unsafe acceleration of claim-less providers whose retry backoff is pump-local. Both are repaired: the named deferred DurableTimerScheduled event retains the built-in scheduling handler, and only provider-owned claim visibility accelerates deadline discovery. Regression coverage proves both paths. Final independent review and exact-head CI are recorded on the PR; do not infer merge or CI success from this local receipt.

Final local review additionally required the named event convention, saturated recovery-time addition with guaranteed idle completion, and a fake clock in the partition regression. These repairs are included before final qualification.

## Final candidate qualification

Code head `b491ae7be14cf382cc41d0914e464fa365c3133c` received clean independent correctness, standards/API and acceptance reviews against the base, after the concrete repairs above. Final named-event Workbench run: local Delay started at 2026-10-30 15:57:56.287283 Europe/Amsterdam and following activity at 15:58:01.564290, 5.277007 seconds; persisted restart 5.642041 seconds including restart, completed once. Original Delay/Timer/Cron regressions passed again on this rebuilt host.

Discovery versus execution: Scheduling 101/101, Activities 25/25, Tasks 71/71, EF 1725/1725 and native providers 191/191. Runtime discovers 3040 entries and executes 3051 because three deferred-discovery theories expand from one entry to 2/4/8 cases; no discovered entry is missing. Architecture discovers 583 entries and executes 592 because two theories expand to 5/6 cases; no discovered entry is missing. Reconciled every discovery name against executed TRX names, including expanded rows.

The final candidate passed Runtime 3051 and Activities 25 again after the named-event repair. Final EF/Architecture rerun and exact final CI/review links are recorded in the PR evidence comment. This receipt does not turn a pending remote check into a pass.
