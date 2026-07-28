# concurrency - Parallel fork/join under suspension and fault

End-to-end tests for the non-trivial runtime behavior of the `Parallel` composite - the parts the in-process C#
harness (in-memory store + synchronous drain) cannot reproduce: multiple concurrent bookmarks under one instance,
and the stateless fault-join decision over the real incident/checkpoint pipeline. (Happy-path fork/join is covered
by `branching/Test-ParallelFork.ps1`.)

| Script | What it exercises |
|--------|-------------------|
| `Test-ParallelSuspendedBranches.ps1` | Two branches each suspend on a distinct `Event` → one instance holds **two concurrent bookmarks**. Resuming E1 advances **only** branch-1 (branch-2 stays parked, Parallel Running); resuming E2 joins the Parallel to Completed. Multi-bookmark **partial resume**. |
| `Test-ParallelFaultJoin.ps1` | [A] default threshold: a faulting branch makes the join unsatisfiable → the Parallel **Faults** (no hang) while sibling branches still ran and an incident is recorded. [B] `Threshold=2`: two successes satisfy the join → the Parallel completes **Done** despite the faulted branch. |
| `_ConcurrencyCommon.ps1` | shared helpers (Event wait, ResumeOnly stimulus with input per #1014, Fault node, incident inspection). |

## Notes / findings

- A faulted branch surfaces its own `ActivityReturnedFault` incident (message from the `Fault` activity); under the
  default (all-branches) threshold the composite faults deterministically rather than hanging.
- Under `Threshold=2`, the workflow reaches **Completed** while still carrying the faulted branch's **Blocking**
  incident — the join resolves on the successful branches and the faulted branch keeps its incident. This is the
  documented stateless-join / `ReplaySafe` behavior, not a hang; the test asserts Completed + siblings ran.

Requires the server from source (see ../README.md).
