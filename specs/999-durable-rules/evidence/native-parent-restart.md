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
| `A_terminal_result_claimed_for_delivery_before_the_rebuild_is_consumed_exactly_once_after_the_lease_expires` | Terminal stored, delivery lease claimed by the first host, no dispatch, no acknowledgement | The dead owner's one-second lease is honoured; the rebuilt host then delivers exactly once and a later attempt finds nothing |
| `A_round_budget_fault_survives_a_host_rebuild_and_faults_the_parent_once` | Terminal `Faulted` (`rules.round-budget-exhausted`) stored, parent still waiting | After the rebuild the delivery faults the child once with the same code; the parent faults; nothing left to deliver |
| `A_parent_cancellation_issued_before_the_rebuild_settles_the_rules_run_after_it` | Parent cancelled, the cancel intent still in the native outbox | The rebuilt host's sweep delivers the intent; terminal `Cancelled`, native child `Cancelled`, the obligation settles with parent-terminal proof |

## How the restart is simulated

`RulesPersistedNativeFixture.ReopenAsync` disposes the harness (DI container and SQLite connections) while keeping the
database file, then builds a new fixture over the same file with the same executable and execution id; the new
generation uses activity ids its predecessor cannot have persisted. The returned fixture owns the file.

## Limits

- In-process rebuild, not a process kill. The process-kill Workbench witness for Rules is the following leaf; the
  pre-admission Workbench script `e2e-tests/rules/Test-RulesAdmissionRecovery.ps1` remains outside CI.
- SQLite only; the compiled Sequence parent of the existing journey (the hand-built Flowchart parent is not repeated).
- Local run on 9 November 2026 (macOS, .NET SDK 10.0.402): 4 facts passed; the whole project 282 tests passed.
