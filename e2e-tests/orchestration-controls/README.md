# orchestration-controls — runtime pause/resume, bookmarks, triggers, terminate

Backend REST tests for Foundation's **runtime orchestration controls**. Scope is deliberately the
**suspend/resume + bookmark/stimulus + in-workflow terminate** surface. Cancel and Retry/redrive are **out of
scope here** (see "Deferred" below). Shared helper: `_ControlsCommon.ps1`. Runs against a from-source
`Elsa.Server` (see ../README.md).

## The control surface (verified in source)

Foundation's controls are **activity-driven and stimulus-based**, not operator-command endpoints:

| Control | Mechanism | REST |
|---|---|---|
| **Suspend / pause** | A workflow suspends itself at a mid-flow wait: an `Event(CanStartWorkflow=false)` catch, an async `HttpEndpoint`, or a waited `DispatchWorkflow`. Not an operator command. | (implicit) |
| **Resume** | `POST runtime/workflows/stimuli` `{stimulusType, stimulusHash, mode, correlationId?}`; modes `StartOnly` / `ResumeOnly` / `StartAndResume`. | ✅ (matching works; **delivery is broken — #1014**) |
| **Terminate (in-workflow)** | `Finish` intrinsic (`elsa.intrinsic.finish@1`, input `outcome`). | ✅ |
| **Cancel (operator/external)** | Status `Cancelled` + internal cancellation exist; **no REST endpoint** for a normal instance. | ❌ (deferred — see below) |
| **Retry / redrive** | `POST runtime/workflows/dispatches/{dispatchId}/redrive` re-drives a **DispatchWorkflow parent/child dispatch** (not a stimulus resume). | (deferred) |
| **Incident remediation** | Incident outcomes may be `FaultWorkflow`, `ContinueWithIncidents`, or system-owned `WaitForIntervention`; the API exposes only `GET …/incidents` and has no retry/resolve endpoint. | ❌ deferred |

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-SuspendResume.ps1` | mid-flow `Event` wait: asserts the **suspend** half strictly (pre-wait ran, Event `Suspended`, post-wait did not run); attempts resume and tracks it against **#1014** (KNOWN ISSUE while resume doesn't deliver; flips to FIXED if it ever completes) |
| `Test-StimulusRouting.ps1` | stimulus routing/matching (independent of delivery): non-matching hash → no-op; `StartOnly` on a `CanStartWorkflow=false` event starts nothing; `ResumeOnly` matches the waiting bookmark (`resumedCount>=1`) |
| `Test-FinishTerminate.ps1` | `Finish` intrinsic terminates the workflow early; pre-Finish ran, post-Finish did not |

## Key findings

- **Suspend works; resume does not deliver (#1014).** A mid-flow `Event` wait parks the workflow (the Event node reaches `Suspended`, downstream doesn't run), and a matching stimulus reports `resumedCount=1` targeting the instance — but the instance never progresses. Start-via-stimulus, by contrast, completes. Same "dispatched-but-not-delivered" family as #1006 (waited `DispatchWorkflow`). Also note: a suspended workflow's **instance-level status stays `Running`**, never `Suspended`.
- **Stimulus routing/matching is correct.** Mode semantics and bookmark matching behave as expected at the dispatch-count level; only the subsequent resume execution is broken.
- **`Finish` terminate works.**

## Deferred (intentionally not here)

- **External cancel** of an instance — no REST endpoint today; expected to live in the **Alteration API**, which will get its own test suite.
- **Retry / `redrive`** — `dispatches/{id}/redrive` re-drives a `DispatchWorkflow` child dispatch (child-workflow recovery, coupled to the already-broken waited path #1006); it is **not** a stimulus resume/bookmark control, so it belongs with the child-workflow / Alteration-API work, not this suite.
- **Incident remediation over REST** — filed as feature request **#1015** (Elsa 3 had incident strategies; Foundation currently exposes incidents read-only).
