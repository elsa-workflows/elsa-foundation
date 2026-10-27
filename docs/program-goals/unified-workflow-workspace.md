# Unified Workflow Authoring and Execution

- **Status:** Active; owner authorized planning and implementation on 27 October 2026.
- **Area:** Studio workflow authoring, runtime inspection and existing Test Run controls.
- **Steward:** Sipke Schoorstra; one claimed delivery feature at a time.
- **Kanban:** [GitHub Project 64](https://github.com/orgs/elsa-workflows/projects/64/views/1).
- **Charter, decisions and progress:** [Program #3392](https://github.com/elsa-workflows/elsa-foundation/issues/3392).

## Outcome and boundaries

An author designs, executes, interacts with and inspects a workflow in one workspace, retaining the
canvas, viewport, selection and nested scope. The existing side inspector presents configuration and
run data for the same activity. Test Runs use the current draft snapshot through the existing
Publishing-to-Runtime path; execution evidence belongs to that exact snapshot and pinned executable.

Reuse existing runtime overlays, occurrence browsing, value/incident inspection and test-wait commands.
Keep the standalone run viewer as another entry point to shared inspection components. Observation
and animation honor checkpoint granularity, capture policy, authorization and redaction. Recorded
playback is distinct from live state and never controls engine execution.

The same live observation and confirmed-motion presentation also applies to the standalone
workflow-instance viewer, including instances already running when opened directly. Share execution
observation, exact scoped selection and motion between the two entry points; preserve each view's
existing authorized controls and navigation. Qualification must cover both surfaces.

No browser execution engine, debugger/breakpoints, arbitrary rewind, hot patching, new persistence
model, forced checkpoint cadence, blanket payload capture or new workflow-model semantics. Proposed
execution-evidence and commit-journal ADRs are not dependencies. A consequential new observation
contract requires an explicit decision before implementation.

Following uses a shared serial runtime observer in both views. The editor's Test Run adapter also
reconciles existing Publishing metadata and waits. Pausing observation leaves the workflow running
and retains the deliberately inspected occurrence. A stop
receipt remains pending until runtime confirms a terminal state. Historical snapshot inspection is
read only; returning to editing restores the separate draft and its navigation.

Reload recovery is explicit and tab scoped: retain only run/artifact references and a SHA-256 digest
of the executed design, with no authored inputs, captured values or credentials in browser storage.
Restore rechecks current authorization, exact run identity, expiry and the freshly loaded design.
When the saved design differs, the prior editor snapshot cannot be reconstructed after reload; offer
the existing run viewer instead. Session/endpoint changes revoke recovery references. Recovery and
reconnection never dispatch another execution. In-page draft changes retain the full captured editor
snapshot in memory for inspection.

## Delivery plan

| Epic | Deliverable feature | Dependency |
|---|---|---|
| [One workspace #3393](https://github.com/elsa-workflows/elsa-foundation/issues/3393) | [Attach Test Runs #3395](https://github.com/elsa-workflows/elsa-foundation/issues/3395) | Existing editor, draft Test Runs and runtime overlays |
| #3393 | [Exact activity inspection #3396](https://github.com/elsa-workflows/elsa-foundation/issues/3396) | #3395 |
| [Interactive execution #3394](https://github.com/elsa-workflows/elsa-foundation/issues/3394) | [Follow and interact #3397](https://github.com/elsa-workflows/elsa-foundation/issues/3397) | #3395, #3396 |
| #3394 | [Confirmed-path motion #3398](https://github.com/elsa-workflows/elsa-foundation/issues/3398) | #3396, #3397; audit actual transition evidence before implementation |
| #3394 | [Integrated qualification #3399](https://github.com/elsa-workflows/elsa-foundation/issues/3399) | All delivery features |

Acceptance criteria and task checklists live on their owning feature issues. The native sub-issue
hierarchy, blocking relationships and the owner-requested Kanban board are the control surface; no
separate PRD or SpecKit tree is required. Board columns are To do, In progress, In review, Blocked and
Done. Keep them aligned with delivery status; Done requires verified merge. Promote checklist tasks only for independent ownership or a material dependency. Add an
ADR or focused contract note only when a concrete decision needs one. The owner's explicit lightweight
workflow choice supersedes the usual SpecKit artifact sequence for this program.

## Sequencing and coordination

Run beside the active programs, displacing none and reactivating no parked work. Coordinate shared
Studio paths with [Hosting #3340](studio-hosting-package-reuse.md),
[Expressions #2310](studio-expression-developer-experience.md),
[Compensation #3035](native-compensation.md) and [Unified Execution #2892](unified-execution.md).
Reuse completed UX qualification #3057 and occurrence browsing #3377 without reopening them. Existing
native/BPMN/ETL boundaries remain intact. Releases, deployment and publisher activation are outside scope.

## Completion

Each feature carries meaningful regression and composed-host evidence on its PR. Final qualification
covers simple and repeated execution, exact nested/parallel/reusable occurrences, wait/resume, faults,
stop, draft/run drift, reconnect, unavailable/redacted data, keyboard access, themes, narrow layouts
and reduced motion on rebuilt normal hosts. Required exact-head CI and resulting-main verification
must pass. Program setup alone is not delivery; open limitations stay explicit on #3392.
