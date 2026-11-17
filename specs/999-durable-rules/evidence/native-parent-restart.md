# Native parent survives a host restart while waiting on RunRules

Part of [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892); the leaf is
[planned on the program issue](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6738102053).

## What is proved

A native compiled Sequence parent durably suspended on `RunRules` over SQLite survives a host restart at each hand-off
point of the Rules integration and then reaches exactly one native outcome. The tests are
`RulesPersistedNativeRestartTests` in `Extensions/Rules/tests/Tests/RulesPersistedNativeRestartTests.cs` (tier `pr`,
shard `pr-1`):

| Case | Cut | Proof |
|---|---|---|
| `A_suspended_RunRules_survives_a_host_rebuild_before_any_round_and_completes_once` | Parent suspended, run admitted, no round executed | Rebuilt host drives the rounds and delivers; one consumed receipt, `after-rules` runs once, a second delivery attempt finds nothing |
| `A_terminal_result_claimed_for_delivery_before_the_rebuild_is_consumed_exactly_once_after_the_lease_expires` | Terminal stored, delivery lease claimed by the first host, no dispatch, no acknowledgement | While the dead owner's three-second lease is in force the rebuilt host's delivery attempt finds nothing and the child stays suspended; after expiry it delivers exactly once and a later attempt finds nothing |
| `A_round_budget_fault_survives_a_host_rebuild_and_faults_the_parent_once` | Terminal `Faulted` (`rules.round-budget-exhausted`) stored, parent still waiting | After the rebuild the delivery faults the child once with the same code; the parent faults; nothing left to deliver |
| `A_parent_cancellation_issued_before_the_rebuild_settles_the_rules_run_after_it` | Parent cancelled with the child in flight; the Rules run is still `Running` with no terminal when the host dies, so the cancel intent is undelivered | The rebuilt host's sweep delivers the intent; terminal `Cancelled`, native child `Cancelled`, the delivery obligation is acknowledged and nothing remains to deliver |

## How the restart is simulated

`RulesPersistedNativeFixture.ReopenAsync` disposes the harness (DI container and SQLite connections) while keeping the
database file, then builds a new fixture over the same file with the same executable and execution id; the new
generation uses activity ids its predecessor cannot have persisted. The returned fixture owns the file. Rules lease
expiry is database time (`strftime('now')`), so the held-result case uses a real three-second lease and guards its
negative assertion by elapsed time instead of a test clock.

## Limits

- In-process rebuild, not a process kill; the process-kill Workbench witness is the section below. The pre-admission
  Workbench script `e2e-tests/rules/Test-RulesAdmissionRecovery.ps1` remains outside CI.
- SQLite only; the compiled Sequence parent of the existing journey (the hand-built Flowchart parent is not repeated).
- Local run on 9 November 2026 (macOS, .NET SDK 10.0.402): 4 facts passed; the whole project 282 tests passed.

## Process-kill witness (production Workbench)

`RunRulesPendingParentResultCrashTests` in `Extensions/Rules/tests/Hosting/Tests` (tier `integration`, trait
`child-process`, shard `workbench-extensions-hosting`) is the process-kill counterpart, mirroring the #3998 BPMN witness and
the ETL witness in `Extensions/Etl/tests/Hosting/Tests`. It starts the production `Elsa.Workbench` as a child process
through the shared `WorkbenchProcess` harness with `RulesRuntime`, `RulesRuntimeEntityFrameworkCore` (SQLite),
`RulesWorkflowsRuntime` and `RulesWorkflowsDesign` enabled in its own copied `shells.json`, publishes a native parent over
the REST API (Root `RunRules`, Sequence and Flowchart, each with the shared `seed → derived` program), and lets the
Workbench's own Rules worker run the rounds to a terminal.

Identity: a source-built Workbench has no packaged deployment receipt, so the fixture enables
`RulesRuntime.AllowSourceBuildIdentity` ([ADR 0088 amendment](../../../docs/adr/0088-rules-owns-its-execution-domain.md#amendment--2026-11-09-accepted)).
The restarted process loads the same build, so it advertises the same source-build pin and may recover the run; this is
exactly the byte-identical-build condition the amendment states, and it establishes no package provenance.

Cut: the Rules run is terminal and its delivery lease is claimed by the running process, which is frozen just before the
parent resume by the `ELSA_HOLD_RULES_NATIVE_DELIVERY` test hold in `RulesNativeDeliveryExecution` (the same seam shape as
the ETL and BPMN deliveries; production hosts never set the variable, and the seam writes an `.engaged` marker so the test
observes the frozen claim). The test reads the Rules SQLite file directly, since Rules has no runtime inspection REST
surface: exactly one run row, status `Succeeded`, terminal stored, delivery owned and not acknowledged. The process is then
killed with `SIGKILL` (`Process.Kill(entireProcessTree: true)` on Windows) and a fresh process is started over the same
content root. Proof: the parent was `Running` with `RunRules` `Suspended` and no incidents before the kill; the killed
process exited non-zero and the restarted process has a different id; after the restart the parent is `Completed`, exactly
one `RunRules` activity is `Completed`, the following `WriteLine` ran once (Sequence, Flowchart; its line appears exactly
once in the restarted process's console output and never in the killed process's), the single run row is still
`Succeeded` and its delivery is acknowledged.

Limits: completion only (fault and cancel across a process kill remain covered by the in-process witnesses above);
SQLite only; the hold is armed before the parent starts, so the worker cannot deliver ahead of the freeze. The fixture sets
`DeliveryLeaseSeconds` to 3 (production default 60; the lease is database time and has no heartbeat while a delivery is in
flight), so the dead owner's lease has lapsed by the time the restarted worker first polls; each row takes about 12 seconds
locally against a 90-second post-restart deadline.
