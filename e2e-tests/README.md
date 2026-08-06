# e2e-tests - backend end-to-end REST tests

Pure backend (no Studio, no Docker) **end-to-end** tests that drive the `Elsa.Workbench` REST API through the
full workflow lifecycle (login -> design/submit -> publish -> execute/runtime -> observe) against the default
**SQLite** composition. These are the black-box counterpart to the in-process C# tests under `tests/`: they
exercise the real HTTP + persistence + runtime-pump path that unit/integration tests stub out. .NET 10.

## Prerequisites

Build the server, deploy the complete reference-composition schema to the fresh SQLite database, then start the
server (Development profile -> SQLite, seeded admin `admin` / `Password123!`):

```bash
dotnet build src/Apps/Elsa.Workbench/Elsa.Workbench.csproj
dotnet tool run groundwork -- apply \
  --manifest-assembly src/Apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithDiagnosticsDeploymentSchema \
  --provider sqlite \
  --connection 'Data Source=src/Apps/Elsa.Workbench/elsa-groundwork.db' \
  --output json \
  --safe
dotnet run --project src/Apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

It listens on `http://localhost:5095`. The default `appsettings.json` + `shells.json` already enable
everything the core flow needs (design + publishing + runtime APIs, identity, `GroundworkUnifiedPersistenceSqlite`).
Run schema deployment while the server is stopped. The manifest assembly must come from the server output directory,
where all feature assemblies needed by the complete deployment schema are colocated.

## Running these tests — READ THIS (agents included)

- **Windows runner:** use `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`. This machine has **no
  `pwsh`**; the `.EXAMPLE` lines show `pwsh` only as cross-platform shorthand.
- **Rebuild gotcha:** after rebuilding the server from newer source, **delete the SQLite DBs first**
  (`elsa-groundwork.db*`, `elsa.sqlite.db*`, `*.schema.lock` under `src/Apps/Elsa.Workbench/`; stop the server /
  free port 5095 first), then re-run the Groundwork schema deployment command above before starting the server.
  Old documents carry an older schema version a newer build refuses to read, which surfaces as spurious `500`s
  on publish.
- **Opt-in features:** `scheduling/` requires `ActivitiesScheduling` + `WorkflowsRuntimeScheduling` +
  `WorkflowsRuntimeRecurringTriggers` in `shells.json` (enabled by default since #1053); `DispatchWorkflow`/`bpmn`
  require the DispatchWorkflow features (see "Composition change" below). A suite whose features aren't composed
  will fault, not skip.

**A failing e2e test is a signal, not a verdict.** It means one of two things: (1) a genuine **regression** in the
server, or (2) the test is **stale** because the codebase moved — a contract/response shape changed, a tracked bug
was fixed, or a feature was renamed. Before changing the server *or* the test, determine which: pull current
`main`, rebuild with a fresh DB, re-run, and reconcile. Tests marked `KNOWN ISSUE #NNNN` are living trackers that
pass green on purpose and are written to **auto-flip to a strict assertion once the bug is fixed** — if a tracker
starts "failing", the referenced bug was probably fixed and the tracker should be tightened, not worked around.

## Test categorization (true e2e vs integration-candidate)

Every suite is kept; each is tagged by whether it genuinely needs the live HTTP + persistence + runtime path
(**true e2e**) or mainly asserts an API contract/shape that could later move to a C# integration test
(`WebApplicationFactory`) or a TestContainers harness (**integration-candidate**). Nothing is deleted here;
candidates are flagged for future migration so coverage is never dropped before its replacement exists.

| Suite | Category | Why |
|---|---|---|
| root `Test-WorkflowFlow/Sequence/If/Switch/Http/ChildWorkflow` | true e2e | full lifecycle, real HTTP trigger, dispatch |
| `branching`, `single-outcome`, `variables`, `javascript` | true e2e | real publish + runtime of composites / intrinsics / JS sandbox |
| `events`, `stimuli`, `orchestration-controls`, `correlate` | true e2e | stimulus / bookmark / resume + correlation runtime |
| `fault-handling`, `persistence-querying` | true e2e | incident recording + instance query over the runtime |
| `reusable-activities` | true e2e | authoring lifecycle + graph inlining at runtime |
| `scheduling` | true e2e | hosted durable-timer / recurring-trigger pumps |
| `runtime-alterations` | true e2e | durable plan admission, capture, hosted orchestration, checkpoint outcomes, replay and restart |
| `bpmn`, `composition` | true e2e | waited `DispatchWorkflow` + BPMN error boundary |
| `logging` | mixed | `Test-ValueCapture` is runtime e2e; `Test-DiagnosticsSettings` is a read-only contract check |
| `get-endpoints` | integration-candidate | GET status/shape contract; migrate to `WebApplicationFactory` |
| `write-endpoints` | integration-candidate | CRUD status/shape contract; migrate to `WebApplicationFactory` |
| `workflow-version-override` | true e2e | exact-version preflight and promotion through live HTTP + persistence |

The two integration-candidate suites (`get-endpoints`, `write-endpoints`) mostly assert HTTP status codes and
response shapes with little runtime behavior — the natural long-term home is an in-process `WebApplicationFactory`
(or TestContainers) test in `tests/`, at which point they can be retired from here.

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-WorkflowFlow.ps1`     | single `WriteLine` -> submit -> publish -> execute -> observe |
| `Test-SequenceWorkflow.ps1` | `Sequence` root running N `WriteLine` children in order (composite activity via `Structure`) |
| `Test-IfWorkflow.ps1`       | `If` decision composite; runs both conditions and asserts the correct Then/Else branch |
| `Test-SwitchWorkflow.ps1`   | `Switch` composite; asserts the matching case branch runs (or the default) |
| `Test-HttpWorkflow.ps1`     | `HttpEndpoint` start-trigger; publishes, then fires a real HTTP request at `/workflows/http/<path>` |
| `Test-ChildWorkflow.ps1`    | parent/child dispatch: a parent `DispatchWorkflow` fires a separately-published child workflow |
| `single-outcome/Test-ForLoop.ps1` / `Test-ForEachLoop.ps1` / `Test-SetOutput.ps1` / `Test-SetVariable.ps1` | loops + Set/SetOutput intrinsics |
| `single-outcome/Test-WhileLoop.ps1` | `While` loop terminated by a body `Set` of the condition variable (#977) |
| `single-outcome/Test-WhileCounter.ps1` | `While` driven by a JS-incremented counter — body reads/writes a variable from JS via `getVariable` (#984 + #977) |
| `branching/Test-ParallelFork.ps1` | `Parallel` fork/join |
| `composition/Test-ChildWorkflowInput.ps1` | parent dispatches a child **and passes it an input**; child echoes it; correlate the child by correlationId |
| `javascript/Test-JavaScriptExpressions.ps1` | pure-ES JS in a Sync HTTP response body (array/object/json/optional-chaining/nullish/flat/replaceAll) |
| `http/Test-HttpMethods.ps1` | one HttpEndpoint accepting GET/POST/PUT/DELETE, each returning a sync response |
| `http/Test-HttpEcho.ps1` | capture request data (`ParsedContent`/`RouteData`/`Request`) into workflow variables and echo it back in a sync response (request-body, route-parameter, query-parameter, header; #972/#984) |
| `http/Test-SendHttpRequestStatusOutcomes.ps1` | the per-status outcome ports the compiler pins from `SendHttpRequest.ExpectedStatusCodes` are connectable on the PUBLISHED node and route correctly, including the `Unmatched status code` catch-all (#1119) |
| `bpmn/Test-BpmnCallActivity.ps1` | BPMN `callActivity` bound to a REAL waited `DispatchWorkflow` child (spec 133): child completes -> parent resumes via `Completed`; child faults -> the error boundary routes (no parent incident) |
| `correlate/Test-Correlate.ps1` | `SetCorrelationId` intrinsic sets the instance correlation id; found by `?correlationId=` |
| `get-endpoints/Test-IntrinsicAuthoringCatalog.ps1` | the authoring catalog offers the five author-facing engine intrinsics and withholds the four engine-internal ones; a node authored from the `SetCorrelationId` descriptor's own template publishes and runs (#1113) |
| `events/Test-Event.ps1` | `Event` start-trigger fired by publishing a stimulus to `runtime/workflows/stimuli` |
| `logging/Test-ValueCapture.ps1` | per-activity value snapshot: a WriteLine's `Text` input is captured (`DiagnosticSnapshot`) and its payload retrieved via the value-evidence endpoint |
| `logging/Test-DiagnosticsSettings.ps1` | read-only `GET runtime/workflows/diagnostics/settings` — the capture policy that governs what value snapshots are captured |
| `runtime-alterations/Test-AlterationPlans.ps1` | bulk `CancelWorkflow`, root `ModifyVariable`, Sequence `ScheduleActivity` with visible child completion, `RescheduleActivity` with visible supersession, retained-identity `Migrate` smoke path; plus paging, cooperative cancellation, and redacted reads |
| `runtime-alterations/Test-AlterationReplayAndRestart.ps1` | idempotency replay and restart-safe continuation from a durably captured first target page against the real SQLite server |
| `_ElsaCommon.ps1`           | shared helpers (dot-sourced): login, activity lookup, submit/publish/execute, structures, observability |
| `workflow-version-override/Test-WorkflowVersionOverride.ps1` | automatic/exact promotion preflight, exact SemVer promotion, immutable version read |

**Events note:** Foundation has no classic `PublishEvent` activity. An `Event` activity is a start trigger;
you publish an event by POSTing a stimulus `{ stimulusType:"Event", stimulusHash:"sha256:"+hex(SHA256(eventName)), mode:"StartOnly" }`
to `runtime/workflows/stimuli` (modes: `StartOnly`/`ResumeOnly`/`StartAndResume`). The response returns the
started `workflowExecutionId` directly.

**HTTP finding (resolved):** request-data **echo** now works (`http/Test-HttpEcho.ps1` — request-body,
route-parameter, query-parameter, and header). This was originally deferred under issue #972: capturing an `HttpEndpoint` output forced a
workflow-scope variable, and reading a workflow-scope variable in a later node faulted. Both halves are fixed on
current main. Two authoring notes: (1) `HttpEndpoint` exposes `Request` + `RouteData` (both **required** to bind)
and `ParsedContent` (optional); output capture must target a workflow-scope `Variable`. (2) `WriteHttpResponse.Body`
materializes as `System.String` and a captured Object/JsonElement variable does not implicitly convert — reading it
through a **JS** binding (`JSON.stringify(getVariable('x'))` / member access, #984) both echoes the value and
yields a string.

**JavaScript finding:** Foundation evaluates binding JS in a **deterministic closed sandbox** — only `args`
(declared expression params); `Date`/`Temporal`/`Intl`/`Math.random`/`crypto` are stripped, and there are **no
classic Elsa host functions** (`newGuid`, `getCorrelationId`, `parseGuid`, `base64Encode`, `getConfiguration`, …).
So the JTest `date-methods`, `elsa-functions`, and `script-handler-functions` suites do **not** reproduce here (by
design, not a bug); pure-ES (array/object/json/modern syntax) works fully.

Run any of them:

```bash
# Windows (no pwsh): powershell -NoProfile -ExecutionPolicy Bypass -File ./e2e-tests/Test-WorkflowFlow.ps1
pwsh ./e2e-tests/Test-WorkflowFlow.ps1
pwsh ./e2e-tests/Test-SequenceWorkflow.ps1 -Lines "one","two","three"
pwsh ./e2e-tests/Test-HttpWorkflow.ps1 -Method GET
```

Each prints step-by-step progress and the resulting instance (status + per-activity executions). On any
failure it stops and prints the failing step, HTTP status, and the ProblemDetails body.

## Contract notes baked into the scripts (learned by testing)

- **Auth is a cookie session**, not a bearer token. `POST /_elsa/identity/login` returns a session object and
  sets an `Elsa.Identity.Cookie`; reuse it with `-WebSession`.
- **Routes live at the shell root** (`/design/...`, `/publishing/...`, `/runtime/...`, `/_elsa/identity/...`),
  and the health check is `GET /`.
- **Create takes `{ name, description, state }`** at `POST /design/workflows/definitions/submit` (definition +
  version 1.0.0 in one call). The graph is `state.rootActivity`; composites nest children under `Structure`.
- **`referenceKey` casing is per-activity** - inspect it via `GET /publishing/activities/{versionId}/construct`.
  `WriteLine` uses lowercase `text`; `HttpEndpoint` uses `Path` / `SupportedMethods` / `CanStartWorkflow`.
- **Execute route contains `executables/`**: `POST /runtime/workflows/executables/{artifactId}/execute`.
- **Artifacts are content-addressed.** Publishing the same workflow content twice yields the same artifact with
  multiple live publications, so pass `sourceReferenceId` (from the publish response) to execute to pin one.
- **Inbound HTTP endpoints** are served under `/workflows/http`; an async trigger returns `202` with
  `{ started: [executionId], resumed: [] }`.

## JTest-derived suite (translating tests/ from JTEST-nexxbiz)

We are re-expressing the JTest cases (which target classic Elsa 3.x) as focused Foundation-native scripts,
one concept per file, under category subfolders. Foundation's palette is **structural composites + node
intrinsics**, so classic activities map like this:

| Classic Elsa (JTest) | Foundation | Script |
|---|---|---|
| FlowDecision / If | `If` | `branching/` (If via core) |
| FlowSwitch / Switch | `Switch` | Switch via core |
| FlowFork + FlowJoin | `Parallel` | `branching/Test-ParallelFork.ps1` |
| For / ForEach | `For` / `ForEach` | `single-outcome/Test-ForLoop.ps1`, `Test-ForEachLoop.ps1` |
| SetOutput (activity) | `elsa.intrinsic.set-output@1` intrinsic | `single-outcome/Test-SetOutput.ps1` |

**Passing:** If, Switch, For (inclusive/exclusive), ForEach, Parallel fork/join, SetOutput, **SetVariable**,
**While** (body-Set terminated, #977), **While with a JS counter** (#984 + #977), **HttpEndpoint request-data
echo** (#972, request-body + route-parameter + query-parameter + header).

**Key scoping rule (corrected).** Since the #972 two-guard validation landed
(`IntrinsicVariableTargetValidator` at design time + `ExecutableNodeCompiler.ValidateIntrinsicVariableTargets`
at publish), a variable reference and its declaration must agree on scope — and the design validator and the
runtime (`VariableScope`) now resolve identically, via the reference's `DeclaringScopeId`. Two authorings work
end to end, verified live:

- **Workflow scope (simplest):** declare the variable in `state.Variables` (`Submit-Workflow -Variables`) and
  reference it with **no** `declaringScopeId`. This is what the scripts use.
- **Container scope:** declare the variable on a container (e.g. the Sequence's `variables`) **and** give every
  reference to it (Set target, `Variable`-read, loop condition) a `declaringScopeId` equal to that container's
  node id. Verified deterministic (5/5).

What fails is the **mismatch** the old scripts had: declaring on the container while referencing with no
`declaringScopeId` (i.e. workflow-scope). The #972 guard now rejects that at publish
(*"targets variable '…' in scope 'workflow', which is not visible from this node's scope"*). Container-scope is
**not** a runtime bug — an incomplete `declaringScopeId` on a read/condition is just an authoring error.
`Test-SetVariable.ps1` / `Test-WhileLoop.ps1` were updated from the mismatching container declaration to clean
workflow scope.

**Resolved (`While`):** Both original blockers are fixed. (1) A JS value expression **can** now read a container
variable via `getVariable('X')`/`variables.X`/`getX()` — the visible variable frames are projected into the isolated
engine as a host-pinned read surface (issue #984), so a counter can be incremented from JS. (2) A body that `Set`s
the loop-condition variable now propagates to the loop condition — `While` re-materializes its inputs per pass
(issue #977 / PR #985) instead of reconstructing from a frozen snapshot, so the loop exits after the expected pass
instead of faulting at the 64-cycle drain limit.

**Still open (branching + single-outcome):**
- No Foundation 1:1 for classic `FlowSwitch` default/fail, implicit joins, or `Switch` MatchAny mode
  (Foundation removed the flow-activity model). Covered at the concept level via `If`/`Switch`/`Parallel`;
  these are architectural gaps, not bugs.

## Advanced tier (JTest logging + storage-drivers)

The classic JTest "advanced" suites target Elsa 3.x persistence/observability primitives that Foundation
does not have. One maps to a Foundation-native equivalent; the other is a genuine architectural gap.

### logging -> per-activity value capture + diagnostics policy (reproduced)

Classic Elsa configured *what activity state is persisted* per activity via `logPersistenceConfig`
(Include/Exclude). Foundation has **no `logPersistenceConfig`**. Its equivalent is a runtime **value-capture**
model governed by a server-wide **diagnostics settings** policy:

- Each activity execution captures **value snapshots** (`captureMode` `DiagnosticSnapshot`) for its inputs;
  the activity-execution detail lists them (`name`, `subject`, `evidenceId`, `captureState`) and the full
  payload is fetched separately at
  `runtime/workflows/instances/{wf}/activity-executions/{ae}/value-evidence/{evidenceId}/payload`.
  Covered by `logging/Test-ValueCapture.ps1`.
- `GET runtime/workflows/diagnostics/settings` reports the effective capture level, the host policy ceiling,
  and the snapshot limits (why payloads come back as a **bounded preview**, not the raw value:
  full-payload capture is disabled by host policy, `maxStringLength=256` etc.). Covered by
  `logging/Test-DiagnosticsSettings.ps1` (read-only — it does not PUT/mutate the server-global setting).

Capture is **boundary-level**: a bare root activity captures no snapshot (`valueSnapshotCount=0`), while the
same activity nested in a `Sequence` does (`=1`) — which is why the value-capture test nests the WriteLine.

### storage-drivers -> no equivalent (architectural gap)

Classic Elsa let each variable pick a storage driver (Memory vs WorkflowInstance/persistent). Foundation has a
**single durable-value storage driver** (`elsa.json`, `WellKnownRuntimeDurableValueStorageDrivers.Json`) — there
is no Memory/WorkflowInstance dichotomy to select between, so the classic per-variable-driver tests do not
reproduce. This is an architecture difference (like the removed flow-activity model), not a bug; no script.

## Composition change: DispatchWorkflow (child workflows)

`Test-ChildWorkflow.ps1` needs the `DispatchWorkflow` activity, which the reference server did not compose.
Enabling it (separate from the bug fixes below):
- `src/Apps/Elsa.Workbench/Elsa.Workbench.csproj` - project refs to `Elsa.Activities.DispatchWorkflow.{Runtime,Design}`.
- `src/Apps/Elsa.Workbench/shells.json` - features `ActivitiesDispatchWorkflowRuntime` + `ActivitiesDispatchWorkflowDesign`.
- No `Program.cs` change needed: `.WithHostAssemblies()` discovers the referenced assemblies' shell features.

Fire-and-forget (`WaitForCompletion=false`, used by the test) works: the parent completes immediately with
outcome `Dispatched` while the child runs independently.

**Waited path (`WaitForCompletion=true`) works again (re-verified 2026-07-23 @ `3dd732d07`).** The #1006
hang is gone since the #982 node-scoped resume-target fix (`4c2386551`): the parent suspends at the dispatch
node, the child runs, and the parent resumes with the child's terminal outcome (`Completed`/`Faulted`) —
covered end-to-end by `bpmn/Test-BpmnCallActivity.ps1` (a BPMN `callActivity` is a waited `DispatchWorkflow`
by convention). `Test-ChildWorkflow.ps1` still covers the fire-and-forget path.

**Known defect (issue #1031):** a dispatched child that faults can be surfaced through a scheduler-poison path — its
fault-recording checkpoint commit fails (`GroundworkRuntimeCheckpointWriterException`), so the child surfaces a
`Critical`/`SchedulerWorkPoisoned` incident instead of the normal `ActivityReturnedFault` one and the faulted
activity's state stays uncommitted (`Running`). Dispatched executions only; a directly-executed `Fault`
workflow records its incident cleanly. Parent-side outcome delivery is unaffected (the waited parent still
resumes with `Faulted`), which is why `bpmn/Test-BpmnCallActivity.ps1` scenario B passes despite it.

## Server fixes made while building these tests

These are genuine defects surfaced by the tests and fixed in the source (candidates for a PR):

1. **`GET /runtime/workflows/instances` returned 500.**
   `ListWorkflowInstancesRequestHandler` projects a per-instance incident count via
   `IIncidentStateStore.CountAsync`, which runs the `list-by-workflow-execution` bounded query with a `Count`
   terminal operation - but that query only declared `Documents`, so the provider rejected it
   (*"does not declare result operation 'Count'"*).
   Fix: `ElsaRuntimeStorageManifest.WithCursorPaging` now declares `Count` (with a consistent
   `SupportsTotalCount`) on the cursor-paged collection queries.

2. **Publishing an `HttpEndpoint` start trigger returned 500 unless every option was authored.**
   Unauthored nullable options (`SupportedMethods`, `Policy`, `RequestTimeout`, `RequestSizeLimit`) compile to a
   `Literal` binding with an *absent* value; the publish preflight mis-read `LiteralValue == null` as
   "non-literal" and threw - contradicting its own documented contract that unauthored options apply defaults.
   Fix: `HttpEndpointTriggerStimulusProvider` now rejects only genuinely non-`Literal` sources and treats an
   omitted/null literal as unauthored (applies the default).
