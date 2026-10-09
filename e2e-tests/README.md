# e2e-tests - backend end-to-end REST tests

Backend **end-to-end** tests that drive the `Elsa.Workbench` REST API through the
full workflow lifecycle (login -> design/submit -> publish -> execute/runtime -> observe) against the default
**SQLite** composition. These are the black-box counterpart to the in-process C# tests under `tests/`: they
exercise the real HTTP + persistence + runtime-pump path that unit/integration tests stub out. .NET 10.
The opt-in shared-persistence journeys use two disposable PostgreSQL databases in Docker instead of SQLite.

## Prerequisites

Build and start the server (Development profile -> SQLite, seeded admin `admin` / `Password123!`):

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj
dotnet run --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

**There is no separate schema-deployment step.** Each EF Core module applies or validates its own
migrations when the shell activates, so the server creates and validates its own schema when it boots.

It listens on `http://localhost:5095`. The default `appsettings.json` + `shells.json` already enable
everything the core flow needs (design + publishing + runtime APIs, identity, and the EF Core runtime,
design and publishing modules on SQLite).

`composition/Test-SharedPersistence.ps1` is self-hosted and does not use port 5095 or the separately started
SQLite server. It builds the Workbench and CLI through its test project's references, provisions two disposable
PostgreSQL databases with Testcontainers, runs Workbench in a child process, and disposes its processes and
container. It also changes the authored resource target, checks a successful shell reload and persisted writes on
the new target, then verifies that an invalid candidate keeps the active generation available. Docker must be
available; the script sets `ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL=1` so missing
Docker fails rather than silently skipping the journey. Run `pwsh ./e2e-tests/composition/Test-SharedPersistence.ps1`
from the repository root, or use `powershell -NoProfile -ExecutionPolicy Bypass -File` on Windows.

`diagnostics/Test-SharedDiagnosticsPersistence.ps1` likewise owns a disposable Workbench and two PostgreSQL
databases. It binds Structured Logs and OpenTelemetry to the diagnostics resource, verifies migration histories
and persisted diagnostic rows stay on that target across restart, and checks resource-scoped CLI selection,
same-target validation, wrong-target refusal, and a split-layout refusal before migrations. It requires Docker.
`diagnostics/Test-OpenTelemetryApiMigration.ps1` remains the separate route/authentication/accepted-OTLP smoke
against a running Workbench; its empty OTLP payloads do not prove persisted diagnostic data.

The three scripts that restart a server also own it: `durability/Test-RestartRecovery.ps1`,
`runtime-alterations/Test-AlterationReplayAndRestart.ps1` and `file-deployment/Test-FileBasedDeployment.ps1`.
Build Workbench first; each script copies the committed configuration to a temporary content root, starts the
built Workbench on a free loopback port, and restarts and stops only that process. They do not use the
separately started server above. See "Running these tests" below for the rules.
`Test-RestartRecovery.ps1` additionally checks that the copied configuration is the legacy SQLite one, and its
post-restart resume currently takes a precise `KNOWN ISSUE #1761` tracker branch for the pre-existing defect;
see [the issue](https://github.com/elsa-workflows/elsa-foundation/issues/1761) and [the durability suite](durability/README.md).

## Hosted Windows and Linux source journey

`.github/workflows/backend-source-platform.yml` is a narrowly path-triggered and manually dispatchable
check on `ubuntu-24.04` and `windows-2025`. It records the exact source revision, runner image, selected
.NET 10.x SDK and PowerShell. Each job starts with empty job-scoped NuGet package, HTTP-cache and CLI-home
paths, restores Workbench and the focused `WriteLineBoundInputExecutionTests` project in locked mode, then
builds/tests with `--no-restore`. It does not restore a package cache or configure repository/environment
secrets. Hosted images still include SDK library packs, so this is not a fresh bare-OS installation.

The platform harness launches the already-built Workbench DLL from an isolated content root, then runs
`Test-WorkflowFlow.ps1` as a child process with the documented Windows PowerShell 5.1 or Linux `pwsh`
invocation. The smoke requires status `Completed`, exactly one completed `write-root` `WriteLine`, and both
a reported incident count and returned incident collection of zero. The harness confirms the exact
WriteLine text in its owned server stdout, stops only its owned PID, verifies that process exited and the
port was released, then removes only its content root. Failure logs are retained for upload.

This hosted path uses CI-owned locked restore and process control around the same project/test targets; it
does not literally replay the contributor guide's ordinary build command or `dotnet run` / Ctrl+C sequence.
It also does not perform the temporary source-edit exercise, prove a clean bare OS, or establish human
Windows acceptance or a .NET support policy. Use the [backend source quickstart](../docs/contributing/backend-source-quickstart.md)
for the interactive contributor route.

## Running these tests — READ THIS (agents included)

> [!WARNING]
> **No script here may stop a process it did not start.** Until
> [#2329](https://github.com/elsa-workflows/elsa-foundation/issues/2329) the restart helpers force-killed whatever
> listened on a port that defaulted to 5095 independently of `-BaseUrl`. On 2026-10-02 a run with
> `-BaseUrl http://localhost:5295` killed a developer's own Workbench on 5095 that way. These rules keep it fixed:
>
> - **`-BaseUrl` is the only place a server location is given.** The port always derives from it. No script has
>   a separate `-Port`, and nothing falls back to 5095 for a process it is about to stop.
> - **A script that restarts a server owns that server.** The three restart-style scripts
>   (`durability/Test-RestartRecovery.ps1`, `runtime-alterations/Test-AlterationReplayAndRestart.ps1`,
>   `file-deployment/Test-FileBasedDeployment.ps1`) launch the already-built Workbench themselves, keep the
>   process they launched, and stop only that process. The shared helper is `_ServerLifecycle.ps1`.
> - **An occupied port is an error, never a takeover.** If the port is held by a process the script did not
>   start, the script fails with the port and the pid in the message and leaves that process running. There is
>   deliberately no `-TakeOverPort` switch: no journey needs to replace a running server, because the owned
>   server runs from its own temporary content root with its own SQLite files. To reuse a port, stop your
>   server yourself.
> - **The platform source harness also owns its server** and uses the same lifecycle helper and free-port rules.
> - **Every other server-dependent script only sends HTTP requests** to `-BaseUrl` (default `http://localhost:5095`).
> - **Do not add `Stop-Process`, `kill`, or a port lookup that feeds one, to a script.** Use
>   `Start-OwnedElsaServer` / `Restart-OwnedElsaServer` / `Remove-OwnedElsaServer`. `Test-ServerLifecycleGuard.ps1`
>   proves the refusal against a bystander listener and needs no server.

Safe invocations:

```powershell
# An ordinary script against your own server, on any port. It only sends requests.
pwsh ./e2e-tests/Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5295

# A restart-style script: no server arguments. It starts, restarts and stops its own Workbench on a free port.
pwsh ./e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1

# The same, on a port you choose. The port must be free, otherwise the script fails and names the port and pid.
pwsh ./e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1 -BaseUrl http://127.0.0.1:5395

# The weaker no-restart run against a server you started. External mode never stops a process.
pwsh ./e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1 -UseExternalServer -RestartServer:$false -BaseUrl http://localhost:5295
```

`Test-RestartRecovery.ps1` takes the same `-UseExternalServer -RestartServer:$false -BaseUrl <url>` form.
`Test-FileBasedDeployment.ps1` always owns its server, because restarting with a mounted folder is the journey.

**The whole suite against a server on a non-default port.** Start your server on that port, then pass the same
`-BaseUrl` to every script that talks to an external server. The scripts that own their server, or need none,
take no server argument and can run while yours is up:

```powershell
# After building, start the server on 5295 instead of the launch profile's 5095 (separate terminal):
#   dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-launch-profile -- --urls http://localhost:5295 --environment Development
# In another terminal, wait until http://localhost:5295/health/ready reports ready, then use the same $base for each script.
$base = 'http://localhost:5295'
$selfHosted = 'Test-RestartRecovery.ps1', 'Test-AlterationReplayAndRestart.ps1', 'Test-FileBasedDeployment.ps1',
              'Test-SharedPersistence.ps1', 'Test-SharedDiagnosticsPersistence.ps1', 'Test-ServerLifecycleGuard.ps1'
$failed = @()
Get-ChildItem ./e2e-tests -Recurse -Filter 'Test-*.ps1' | Sort-Object FullName | ForEach-Object {
    if ($_.Name -in $selfHosted) { pwsh -NoProfile -File $_.FullName }
    else { pwsh -NoProfile -File $_.FullName -BaseUrl $base }
    if ($LASTEXITCODE -ne 0) { $failed += $_.Name }
}
if ($failed) { "FAILED: $($failed -join ', ')" } else { 'all scripts passed' }
```

Each script runs in its own process, so one failure does not end the loop. The two `Test-Shared*` journeys need
Docker. On Windows replace `pwsh -NoProfile -File` with `powershell -NoProfile -ExecutionPolicy Bypass -File`.

- **Windows runner:** use `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`. This machine has **no
  `pwsh`**; the `.EXAMPLE` lines show `pwsh` only as cross-platform shorthand.
- **Rebuild gotcha:** after rebuilding the server from newer source, old SQLite data may be incompatible
  with the new schema and cause errors such as `500` on publish. Stop your own server first. Reset only
  disposable data, following the exact file inventory and allowlist in the [backend quickstart](../docs/contributing/backend-source-quickstart.md#reset-disposable-sqlite-data).
  Preserve valuable workflows in their existing checkout and use a separate disposable checkout; do not
  delete database files with a wildcard or treat this reset as an in-place data migration.
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

Each suite is tagged by whether it genuinely needs the live HTTP + persistence + runtime path (**true e2e**)
or mainly asserts an API contract/shape that belongs in an in-process C# test (**integration-candidate**).
A candidate is retired from here only once its in-process replacement exists.

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
| `composition/Test-SharedPersistence.ps1` | true e2e | disposable PostgreSQL, real Workbench HTTP design/publish/execute/restart, authored-resource reload, and same-target CLI validation |
| `diagnostics/Test-SharedDiagnosticsPersistence.ps1` | true e2e | disposable PostgreSQL, real Workbench OTLP and structured-log writes, restart, two-target placement, scoped CLI validation and negative layout refusal |
| `logging` | mixed | `Test-ValueCapture` is runtime e2e; `Test-DiagnosticsSettings` is a read-only contract check |
| `workflow-version-override` | true e2e | exact-version preflight and promotion through live HTTP + persistence |
| `file-deployment` | true e2e | server restart with a mounted definitions folder; startup reconcile + publish-on-reconcile, readiness gate, restart idempotency (spec 147) |

The former `get-endpoints` and `write-endpoints` suites (GET / CRUD status-and-shape checks) were retired on
2026-09-10: that coverage now runs in-process as the `*ApiContractTests` classes under `tests/` (for example
`ActivitiesDesignApiContractTests`, `PublishingApiContractTests`, `RuntimeAlterationApiContractTests`,
`SecretsApiContractTests`, `StructuredLogsApiContractTests`, `StudioPreferencesApiContractTests`,
`WorkflowsDesignApiContractTests`). Their shared mutation harness survives as `_WriteCommon.ps1` because the
`workflow-version-override` and `reusable-activities` suites dot-source it.

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
| `composition/Test-SharedPersistence.ps1` | opt-in shared-resource journeys: create/publish reusable activity, publish/execute workflow, restart and read persisted state, check primary/diagnostics database placement, run same-target resource-scoped CLI `list` and `validate` and refuse a different target; change the authored resource target, reload the shell, verify writes move, then reject an invalid candidate while the active shell remains available |
| `diagnostics/Test-SharedDiagnosticsPersistence.ps1` | opt-in two-target Workbench journey: persist OTLP and structured logs on the diagnostics target, restart, validate module scope with CLI and refuse a split diagnostics layout before database migration |
| `javascript/Test-JavaScriptExpressions.ps1` | pure-ES JS in a Sync HTTP response body (array/object/json/optional-chaining/nullish/flat/replaceAll) |
| `http/Test-HttpMethods.ps1` | one HttpEndpoint accepting GET/POST/PUT/DELETE, each returning a sync response |
| `http/Test-HttpEcho.ps1` | capture request data (`ParsedContent`/`RouteData`/`Request`) into workflow variables and echo it back in a sync response (request-body, route-parameter, query-parameter, header; #972/#984) |
| `http/Test-RuntimeConcurrencyCorrectness.ps1` | the #2392 reference HTTP workflow under concurrent clients (default 16 × 4, `-Cadence Immediate`/`Coalesced`): every response HTTP 200 `Alice Smith`, one Completed instance per request, zero incidents. Correctness only; no timings (#2532) |
| `http/Test-SendHttpRequestStatusOutcomes.ps1` | the per-status outcome ports the compiler pins from `SendHttpRequest.ExpectedStatusCodes` are connectable on the PUBLISHED node and route correctly, including the `Unmatched status code` catch-all (#1119) |
| `bpmn/Test-BpmnCallActivity.ps1` | BPMN `callActivity` bound to a REAL waited `DispatchWorkflow` child (spec 133): child completes -> parent resumes via `Completed`; child faults -> the error boundary routes (no parent incident) |
| `correlate/Test-Correlate.ps1` | `SetCorrelationId` intrinsic sets the instance correlation id; found by `?correlationId=` |
| `events/Test-Event.ps1` | `Event` start-trigger fired by publishing a stimulus to `runtime/workflows/stimuli` |
| `logging/Test-ValueCapture.ps1` | per-activity value snapshot: a WriteLine's `Text` input is captured (`DiagnosticSnapshot`) and its payload retrieved via the value-evidence endpoint |
| `logging/Test-DiagnosticsSettings.ps1` | read-only `GET runtime/workflows/diagnostics/settings` — the capture policy that governs what value snapshots are captured |
| `diagnostics/Test-OpenTelemetryApiMigration.ps1` | live Workbench smoke: eight query/SSE plus three OTLP routes in the real shell, authorized query/SSE, and accepted OTLP 204 |
| `runtime-alterations/Test-AlterationPlans.ps1` | bulk `CancelWorkflow`, root `ModifyVariable`, Sequence `ScheduleActivity` with visible child completion, `RescheduleActivity` with visible supersession, retained-identity `Migrate` smoke path; plus paging, cooperative cancellation, and redacted reads |
| `runtime-alterations/Test-AlterationReplayAndRestart.ps1` | idempotency replay and restart-safe continuation from a durably captured first target page against a real SQLite server the script owns |
| `Test-ServerLifecycleGuard.ps1` | no server needed: a restart-style launch refuses a port held by a bystander process, names the port and pid, and leaves the bystander running (#2329) |
| `_ElsaCommon.ps1`           | shared helpers (dot-sourced): login, activity lookup, submit/publish/execute, structures, observability |
| `_ServerLifecycle.ps1`      | owned-server lifecycle (dot-sourced by the restart-style scripts): port derived from the base URL, temporary content root, start / restart / stop of the one process the script launched |
| `_WriteCommon.ps1`          | shared mutation harness (dot-sourced): `Invoke-Write` / `Assert-Write` / `Complete-WriteSuite` for status-and-shape assertions on writes |
| `workflow-version-override/Test-WorkflowVersionOverride.ps1` | automatic/exact promotion preflight, exact SemVer promotion, immutable version read |
| `file-deployment/Test-FileBasedDeployment.ps1` | file-based deployment at startup (spec 147) on a server the script owns: definitions folder composed via env vars (`JsonWorkflowReconciliation` + `PublishOnReconcile`), `/health/ready` gate, imported + published + executable, idempotent restart |

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

Each prints step-by-step progress and the resulting instance (status + per-activity executions). The
single-WriteLine `Test-WorkflowFlow.ps1` smoke exits unsuccessfully unless its one returned `write-root`
WriteLine activity and workflow both completed with zero reported and returned incidents. On an HTTP
failure, it prints the failing step, status and ProblemDetails body.

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
- `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` - project refs to `Elsa.Activities.DispatchWorkflow.{Runtime,Design}`.
- `src/apps/Elsa.Workbench/shells.json` - features `ActivitiesDispatchWorkflowRuntime` + `ActivitiesDispatchWorkflowDesign`.
- No `Program.cs` change needed: `.WithHostAssemblies()` discovers the referenced assemblies' shell features.

Fire-and-forget (`WaitForCompletion=false`, used by the test) works: the parent completes immediately with
outcome `Dispatched` while the child runs independently.

**Waited path (`WaitForCompletion=true`) works again (re-verified 2026-07-23 @ `3dd732d07`).** The #1006
hang is gone since the #982 node-scoped resume-target fix (`4c2386551`): the parent suspends at the dispatch
node, the child runs, and the parent resumes with the child's terminal outcome (`Completed`/`Faulted`) —
covered end-to-end by `bpmn/Test-BpmnCallActivity.ps1` (a BPMN `callActivity` is a waited `DispatchWorkflow`
by convention). `Test-ChildWorkflow.ps1` still covers the fire-and-forget path.

**Known defect (issue #1031):** a dispatched child that faults can be surfaced through a scheduler-poison path — its
fault-recording checkpoint commit fails, so the child surfaces a
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
