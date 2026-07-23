# TestScripts - backend workflow flow tests

Pure backend (no Studio, no Docker) tests that drive the `Elsa.Server` REST API through the full
workflow lifecycle against the default **SQLite** composition. Verified against Elsa Foundation
@ `6b5996de` (+ the two server fixes below), .NET 10.

## Prerequisites

Start the server from source (Development profile -> SQLite, seeded admin `admin` / `Password123!`):

```bash
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj --launch-profile http
```

It listens on `http://localhost:5095`. The default `appsettings.json` + `shells.json` already enable
everything the flow needs (design + publishing + runtime APIs, identity, `GroundworkUnifiedPersistenceSqlite`).

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
| `branching/Test-ParallelFork.ps1` | `Parallel` fork/join |
| `composition/Test-ChildWorkflowInput.ps1` | parent dispatches a child **and passes it an input**; child echoes it; correlate the child by correlationId |
| `javascript/Test-JavaScriptExpressions.ps1` | pure-ES JS in a Sync HTTP response body (array/object/json/optional-chaining/nullish/flat/replaceAll) |
| `http/Test-HttpMethods.ps1` | one HttpEndpoint accepting GET/POST/PUT/DELETE, each returning a sync response |
| `correlate/Test-Correlate.ps1` | `SetCorrelationId` intrinsic sets the instance correlation id; found by `?correlationId=` |
| `events/Test-Event.ps1` | `Event` start-trigger fired by publishing a stimulus to `runtime/workflows/stimuli` |
| `logging/Test-ValueCapture.ps1` | per-activity value snapshot: a WriteLine's `Text` input is captured (`DiagnosticSnapshot`) and its payload retrieved via the value-evidence endpoint |
| `logging/Test-DiagnosticsSettings.ps1` | read-only `GET runtime/workflows/diagnostics/settings` — the capture policy that governs what value snapshots are captured |

**Events note:** Foundation has no classic `PublishEvent` activity. An `Event` activity is a start trigger;
you publish an event by POSTing a stimulus `{ stimulusType:"Event", stimulusHash:"sha256:"+hex(SHA256(eventName)), mode:"StartOnly" }`
to `runtime/workflows/stimuli` (modes: `StartOnly`/`ResumeOnly`/`StartAndResume`). The response returns the
started `workflowExecutionId` directly.

**HTTP finding:** request-data **echo** cases (query-parameter / route-parameter / request-body / headers) are
**blocked by issue #972**. `HttpEndpoint` exposes request data as outputs, but capturing one into a variable
forces workflow-scope, and reading a workflow-scope variable in a later container node faults at runtime
(`Variable '...' in scope 'workflow' ... is unavailable`). Method-handling (no capture) reproduces cleanly.

**JavaScript finding:** Foundation evaluates binding JS in a **deterministic closed sandbox** — only `args`
(declared expression params); `Date`/`Temporal`/`Intl`/`Math.random`/`crypto` are stripped, and there are **no
classic Elsa host functions** (`newGuid`, `getCorrelationId`, `parseGuid`, `base64Encode`, `getConfiguration`, …).
So the JTest `date-methods`, `elsa-functions`, and `script-handler-functions` suites do **not** reproduce here (by
design, not a bug); pure-ES (array/object/json/modern syntax) works fully.
| `_ElsaCommon.ps1`           | shared helpers (dot-sourced): login, activity lookup, submit/publish/execute, structures, observability |

Run any of them:

```bash
pwsh ./TestScripts/Test-WorkflowFlow.ps1
pwsh ./TestScripts/Test-SequenceWorkflow.ps1 -Lines "one","two","three"
pwsh ./TestScripts/Test-HttpWorkflow.ps1 -Method GET
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

**Passing:** If, Switch, For (inclusive/exclusive), ForEach, Parallel fork/join, SetOutput, **SetVariable**.

Key scoping rule discovered: a variable used by `Set`/`Variable`-read intrinsics must be declared on the
enclosing **container's structure** (e.g. the Sequence's `variables`), not at workflow-state level — otherwise
the Set faults with *"targets undeclared variable"*. `New-VariableDef` + `New-SequenceStructure -Variables`
encode this.

**Still open (branching + single-outcome):**
- `While` — a real loop-variable-scope issue, NOT just missing effort. Two concrete blockers: (1) a JS value
  expression can't read a container variable via `getX()`/`variables.X`/`X` (all fail to evaluate), so a counter
  can't be incremented; (2) a body that `Set`s a boolean condition variable to `false` does **not** propagate to
  the loop condition — the loop runs until `DrainCycleLimitExceededException` (64 cycles). Candidate bug (variable
  mutation inside a loop body not visible to the loop condition) — needs a focused pass to confirm bug vs scoping.
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
- `src/Apps/Elsa.Server/Elsa.Server.csproj` - project refs to `Elsa.Activities.DispatchWorkflow.{Runtime,Design}`.
- `src/Apps/Elsa.Server/shells.json` - features `ActivitiesDispatchWorkflowRuntime` + `ActivitiesDispatchWorkflowDesign`.
- No `Program.cs` change needed: `.WithHostAssemblies()` discovers the referenced assemblies' shell features.

**Known limitation:** `DispatchWorkflow` with `WaitForCompletion=true` currently **faults** - the waited dispatch
poisons with `System.InvalidOperationException: A waited workflow dispatch must suspend with exactly one matching
typed trigger registration` (`WorkflowInvokeActivitySchedulerWorkHandler.AssertSingleDispatchRegistration`). The
fire-and-forget path (`WaitForCompletion=false`) works: the parent completes with outcome `Dispatched` and the
child runs as an independent execution. The test uses fire-and-forget; the waited path is left as a finding
(it lives deep in the runtime scheduler, not in these tests).

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
