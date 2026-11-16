# Delivery tasks

Closed 16 November 2026. The [charter closeout](../../docs/program-goals/runtime-efficiency.md#closeout-16-november-2026)
records each outcome.

- [x] M0: audit and reconcile every older open child against its acceptance and current code/evidence.
- [x] M1: closed by the owner on 1 November on two causal fixes; the historical cause stays unconfirmed.
- [x] M2: extend demand-driven materialization with custom-extension, structure and closed-scope controls (#3601).
- [x] M3: prepare JavaScript and conversion metadata with fresh state, concurrency and lifetime proof (#3598, #3601).
- [x] M4: applicable current-core work delivered; the rest of #2564 closed as superseded by the kernel (#4229).
- [x] M5: handed to kernel release 1 qualification (#4229 gate 3); not run on the current core.
- [x] Review full integration; whole affected suites, accounting, architecture/maps and rebuilt backend e2e (#3601 and its leaves).
- [x] Exact reviewed-head hosted gates; merge and qualify containing main (#3601 containing main `bd0749d86`).
- [x] Remove owned fixtures, restore the existing runner state, publish results and complete program bookkeeping.
  The task fixture groups were deleted by 3 November (#2531 [cleanup record](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6615399245)).
  Read on 16 November: every VM in the subscription is deallocated, including the shared Codex dev fleet that holds
  `elsa-dev-09`. Known leftover: 21 of this program's run commands on `elsa-dev-09` report provisioning Succeeded and
  execution Pending. Three are M5 launches. The full list:
  `elsa-d2-bootstrap-v1`, `elsa-perf-277b59-admission-submit-r2`, `elsa-perf-277b59-first-green-submit-r2`, `elsa-perf-755851-admission-submit-r1`, `elsa-perf-755851-first-green-submit-r1`, `elsa-perf-801560-http-admission-r1`, `elsa-perf-801560-http-lock-launch-r1`, `elsa-perf-8281ae-http-admission-r1`, `elsa-perf-a0d27c-architecture-closeout-launch-r5`, `elsa-perf-a0d27c-architecture-closeout-progress-01-r4`, `elsa-perf-a0d27c-backend-launch-r5`, `elsa-perf-a0d27c-http-admission-r1`, `elsa-perf-a0d27c-http-admission-r2`, `elsa-perf-a233e3-http-admission-r1`, `elsa-perf-d4a538-m5-functional-launch-r1`, `elsa-perf-d4a538-m5-lock-evaluation-launch-r1`, `elsa-perf-e12bb1-m5-functional-launch-r2`, `elsa-perf-epoch2-readonly-admission-r1`, `elsa-xfer-perf277-source-r2`, `elsa-xfer-perf3915-functional-r2`, `elsa-xfer-perf755-source-r1`. Deleting them was tried
  with the owner's approval and refused by Azure (`OperationNotAllowed` while the VM is deallocated). Starting the VM
  may run them, so delete that VM, or its run commands, before anyone starts it.
