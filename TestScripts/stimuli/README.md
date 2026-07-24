# stimuli - stimulus dispatch semantics

Backend tests for `POST runtime/workflows/stimuli` beyond the basic start/resume already covered by
`events/Test-Event.ps1` and `orchestration-controls/Test-SuspendResume.ps1`. This is the Foundation surface that
replaces Elsa 3's "signal" concept: name-based, correlatable, can start and/or resume workflows.

| Script | What it exercises |
|--------|-------------------|
| `Test-CorrelationScopedResume.ps1` | `correlationId` scoping: two waiters on the **same** event with **different** correlations; a correlated `ResumeOnly` resumes only the matching one (the "signal a correlated workflow" pattern). |
| `Test-FanInResume.ps1` | fan-in: an **un-correlated** `ResumeOnly` resumes **every** matching waiter (`resumedCount>=2`). |
| `Test-IdempotentStart.ps1` | `idempotencyKey` dedups the **start** path: same key starts once then `SkippedDuplicate` (`startedCount:0`, `skippedStartCount:1`); a different key starts fresh. |
| `Test-StartAndResume.ps1` | `Mode=StartAndResume` in one call both **starts** a new instance from a start-trigger and **resumes** a waiter (`startedCount>=1` and `resumedCount>=1`). |
| `_StimuliCommon.ps1` | shared helpers: `Invoke-Stimulus` (full request surface), Event start/wait node builders, `New-SuspendedWaiter`. |

## Contract notes (baked into the tests)

- **Request**: `{ stimulusType:"Event", stimulusHash:"sha256:"+SHA256(name), input?, correlationId?, mode?, idempotencyKey? }`. `mode` = `StartOnly` | `ResumeOnly` | `StartAndResume` (default).
- **Response**: `{ startedCount, skippedStartCount, resumedCount, starts[], resumes[] }`; each start is `{ triggerBindingId, artifactId, status, workflowExecutionId }` (`status:"SkippedDuplicate"` + null `workflowExecutionId` when deduped); each resume is `{ workflowExecutionId, status, reason }`.
- **Correlation** that scopes a resume is the mid-flow **`Event` activity's `CorrelationId` input** (captured on the bookmark as `runtime.correlationId`) - **not** the instance identity set by `set-correlation-id`. A null stimulus `correlationId` resumes all matching-hash waiters; a non-null one matches exactly (ordinal).
- **Resume delivery requires a non-empty `input`** (see #1014 / `Test-SuspendResume`); these resume tests all pass one.
- **Idempotency** is start-path only, keyed per artifact, in an in-memory (process-local) dedup store by default.

## Not covered (by design)

**Input on the START path is not observable** through ordinary authoring. Stimulus `input` on a start is seeded on a
reserved `stimulus:input` channel (never the workflow-inputs bag) and delivered only to the triggering node's
internal typed trigger payload; the built-in `Event` start trigger reads only an `eventName` from it. So there is no
authored activity that can surface arbitrary start-input for an end-to-end REST assertion - hence no test for it here
(the resume-path input is exercised throughout).
