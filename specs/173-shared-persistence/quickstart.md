# Shared persistence validation quickstart

Status: Phase 0 research is complete. #1968 (shared Runtime, Workflows Design, Activities Design and Publishing persistence) and #1969 (separate Structured Logs and OpenTelemetry persistence) are not implementation-complete. This guide is the executable validation target for those slices; it does not claim that the resource-aware CLI or host wiring exists today.

The contract is defined by [the shared-persistence specification](spec.md), [the implementation plan](plan.md), [the authored configuration decision](decisions/authored-persistence.md), [the target-selection decision](decisions/tooling-target-selection.md), [the target-verification decision](decisions/tooling-target-verification.md), and [the configuration-context decision](decisions/tooling-configuration-context.md).

## T001 package compatibility checkpoint (2026-09-23)

All seven centrally managed CShells packages now resolve to published `0.0.30-preview.158`. Every downloaded nuspec identifies CShells merge commit `58e4296196637ff3ea6cd3d97e9df875f547b818`; [publication run](https://github.com/valence-works/cshells/actions/runs/35912858168) and [closed compatibility prerequisite #136](https://github.com/valence-works/cshells/issues/136) contain upstream evidence. A normal feed restore with `--no-http-cache` cleared stale preview.157 index data; no local package override remains.

Verified on the implementation branch:

```bash
dotnet restore tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj --no-http-cache --verbosity quiet
dotnet test tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj --no-restore --verbosity quiet
dotnet test tests/essentials/Activities/Design/Api/Tests/Elsa.Activities.Design.Api.Tests.csproj --verbosity quiet
dotnet test tests/essentials/Modularity/EntityFramework/Tests/Elsa.Modularity.EntityFramework.Tests.csproj --verbosity quiet
```

Results: 188 modularity, 26 activity API, and 17 EF modularity tests passed, with zero failures or skips. The modularity test build also compiled its actual Workbench and Foundation Host project references against the published packages. The duplicated catalog fake is shared and implements the supported typed refresh contract while retaining detailed descriptors for settings and activity attribution. Existing compiler/analyzer warnings remain; no resource-mode, PostgreSQL, restart, migration-tooling, or new-mode acceptance evidence is claimed by this checkpoint.

## T002-T003 fixture and detached-model checkpoint (2026-09-23)

The shared-resources test project is registered in `Elsa.Server.slnx` and the required PostgreSQL CI container matrix. Its fixture provisions two separate databases in one disposable `postgres:16-alpine` container, passes their connection references to a real Workbench child process, and supports a clean process restart. `ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL=1` makes missing Docker a failure in the CI leg rather than a skipped pass.

```bash
dotnet test tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests.csproj --verbosity quiet
dotnet test tests/essentials/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj --verbosity quiet
```

Local results: two fixture tests passed against Docker with no skips; 280 existing persistence tests passed with no skips after the internal detached model was added. The fixture test proves that a table written in the primary database is absent from the diagnostics database and that the current Workbench can start and restart while both named connection references are available. It does **not** prove resource selection, migration placement, or new-mode behavior. The resolver, adapters, and resource-mode host journey remain to be implemented and tested.

## T004 pure resolver checkpoint (2026-09-23)

The EF-owned internal resolver selects a shell feature binding, then shell default, root default, or legacy mode. It resolves Provider and ConnectionName atomically, preserves case-insensitive resource lookup, refuses malformed or missing selected resources and authored legacy target conflicts, and reports unknown unenrolled bindings without carrying their values. Inputs are detached metadata; no connection string, feature construction, configuration provider, or database access enters this resolver. The adapter and runtime preparation seam remain open tasks.

```bash
dotnet test tests/essentials/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj --no-restore --verbosity quiet
```

Result: 296/296 tests passed, including 16 new resolver cases, with zero skips. A temporary mutation that ignored a feature binding made the binding-precedence test fail (1/1); restoring the source made all 16 focused resolver tests pass. T010 remains incomplete until raw authored presence, explicit false/zero, and adapter/source cases are covered.

## T005 explicit enrollment checkpoint (2026-09-23)

The 13 reviewed feature classes now carry `EfPersistenceResourceParticipantAttribute`. The EF-owned participant catalog combines that marker with existing `ShellFeature` identity, `UsesEfModule` declarations, `EfProviderAgreement` provider-setting metadata, and `EfModuleCatalog` context ownership. Discovery does not construct feature classes or configure their services. Missing or conflicting module ownership stays unresolved for the later EF validator instead of changing legacy startup.

```bash
dotnet test tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj --no-restore --verbosity quiet
```

Result: 220/220 migrations tests passed with zero skips, including two new enrollment cases. The test locks the exact 13 stable IDs and module/context pairs while excluding IAM, Secrets, distributed stores, Dashboard, and other unmarked features. A marked probe with a throwing constructor/configurator was discovered without either running. T011 architecture and integration coverage remains open; marker discovery alone does not prove resource-mode activation.

## T006 configuration adapter checkpoint (in progress, 2026-09-23)

The EF-owned adapter now reads root resource definitions, root/shell defaults and shell bindings from the composed preparation view and the selected shell's raw configuration. It recovers explicit null legacy target presence lost during CShells feature flattening, distinguishes reset raw fields from final code-configured fields, and leaves known inactive bindings inert. The public facade returns only Provider/ConnectionName scalar patches and redacted applicability, and returns an empty patch for a refusal. It does not construct feature classes, resolve connection values, or access a database. Architecture guards were updated to inventory the new files and documented test visibility; the shared PostgreSQL fixture was moved to its required `Tests/` project path. Local evidence: 29/29 targeted adapter/resolver tests, 7/7 enrollment/facade tests, 4/4 previously red architecture checks, and 2/2 relocated PostgreSQL fixture tests passed with zero skips. Generated maps and solution filters passed their freshness checks. The full changed-head CI gate has not yet run.

T006 remains open while the configuration-source boundary is reviewed. `IConfiguration` exposes JSON scalar booleans/numbers as strings, so it cannot by itself prove that a selected scalar was originally authored as a string. Strict wrong-type selection reporting needs a source-aware rule or a reviewed conservative naming restriction. Runtime registration, generation reload, EF validation, and database proof are separate later tasks and are not claimed by this checkpoint.

A full local architecture-suite attempt reported 243 passed and 15 EF dependency-guard failures. A targeted rerun showed those guards need evaluated Debug and Release assets from a full `Elsa.Server.slnx` restore, which this worktree does not have. The four architecture checks addressing the previous hosted failure passed locally. The fresh hosted CI run is the remaining full-gate evidence; the local full-suite attempt is not reported as green.

## What success must prove

The shared layout selects one named PostgreSQL resource for every enabled enrolled Runtime, Workflows Design, Activities Design and Publishing consumer. The diagnostics layout selects a second named resource for both Structured Logs and OpenTelemetry while leaving the primary consumers on their original target. The first slice does not redirect host-owned OpenIddict, private stores, or unknown persistence consumers ([spec](spec.md#normative-supported-participants-and-constraints)).

Each successful run must show:

1. The host started with the intended shell and source context.
2. The effective target was resolved before feature binding and migration/provider checks.
3. The selected module set matches the intended resource target.
4. The real HTTP workflow path completed through design, publish, execute and query.
5. PostgreSQL migration-history and domain tables are present in the intended database and absent from the other target.
6. The same facts remain true after a clean host restart.

An offline `list`, `plan`, or `script` result is configuration and migration evidence only. It does not prove live target parity. Live `apply`, `validate`, and `post-migrate` must compare the supplied connection from `--connection-env` or `--connection-stdin` with the selected resource's expected named connection before opening a database ([target verification](decisions/tooling-target-verification.md)).

## Prerequisites

The current source-run Workbench prerequisite is the project and launch profile documented by [the e2e README](../../e2e-tests/README.md#prerequisites):

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj
dotnet run --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

The current baseline listens on `http://localhost:5095`. `GET /` is the existing health check; the file-deployment suite additionally uses `GET /health/ready` as the shell-activation readiness gate. Stop the server before rebuilding. After a source rebuild, the e2e README requires disposable SQLite files (`src/apps/Elsa.Workbench/elsa.db*` and `src/apps/Elsa.Workbench/elsa-diagnostics.db*`) to be removed before a fresh SQLite baseline is started. Do not delete a PostgreSQL target; reset or recreate only a database explicitly allocated for this run.

The first new-mode proof must use a rebuilt Workbench with two disposable PostgreSQL databases. Separate schemas in one database are not sufficient evidence for the selected split layout, which requires distinct physical targets, for example:

```text
primary target:     <shared-primary-database>
diagnostics target: <diagnostics-database>
```

The repository currently documents the Workbench project and HTTP profile, but does not yet contain a committed PostgreSQL resource fixture or a resource-aware e2e script. Add the exact fixture/profile path when #1968 implementation lands; do not substitute a guessed launch profile.

T001 pins all seven CShells packages to `0.0.30-preview.158`, which includes the preparation hook and the detailed catalog compatibility fix required by Elsa. The published prerequisite and its evidence are recorded in [research R2](research.md#r2-cshells-lifecycle-integration). Verify the implementation branch with:

```bash
rg -n 'PackageVersion Include="CShells(|\.Abstractions|\.AspNetCore|\.AspNetCore\.Abstractions|\.FastEndpoints|\.FastEndpoints\.Abstractions|\.Management\.Api)"' Directory.Packages.props
```

All CShells packages used by the host must resolve to the reviewed published version before claiming runtime/reload evidence.

Load database credentials through a secret manager, a protected service definition, or the process environment. Do not put a password or connection string in a command argument, checked-in file, plan, manifest, log, or shell history. For PostgreSQL inspection, a protected `pg_service.conf` entry avoids placing the value in `argv`:

```bash
export PGSERVICE="${ELSA_PG_SERVICE:?Set the protected PostgreSQL service name}"
export ELSA_EF_CONNECTION="${ELSA_EF_CONNECTION:?Set the target connection through a protected environment source}"
export HOST_DIR="${HOST_DIR:?Set the rebuilt host output directory}"
export SHELL_NAME="${SHELL_NAME:?Set the exact selected shell identity}"
export ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-Production}"
export EVIDENCE_DIR="${EVIDENCE_DIR:-$PWD/artifacts/shared-persistence}"
mkdir -p "$EVIDENCE_DIR"
```

The CLI contract permits the connection value only through `--connection-env NAME` or `--connection-stdin`; only the environment-variable name travels through the worker request ([CLI README](../../src/essentials/Cli/README.md#flags), [ADR 0076 D7](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md#d7--connection-by-environment-variable-or-stdin-only)).

## Baseline HTTP and persistence smoke

Run the existing SQLite baseline before the new-mode proof. It characterizes the unchanged workflow behavior and catches a host-composition regression independently of resource selection:

```bash
pwsh ./e2e-tests/Test-WorkflowFlow.ps1
pwsh ./e2e-tests/reusable-activities/Test-PublishingLifecycle.ps1
pwsh ./e2e-tests/persistence-querying/Test-InstancePaging.ps1
pwsh ./e2e-tests/persistence-querying/Test-IncidentQuery.ps1
```

The scripts log in through `POST /_elsa/identity/login` and use the cookie session for subsequent requests. The workflow path is:

```text
POST /design/workflows/definitions/submit
POST /publishing/workflows/preflight
POST /publishing/workflows/{versionId}/publish
POST /runtime/workflows/executables/{artifactId}/execute
GET  /runtime/workflows/instances
```

These routes and the authentication/session behavior are documented in [e2e-tests/README.md](../../e2e-tests/README.md#contract-notes-baked-into-the-scripts). The existing scripts use the local development seed; do not pass production credentials as command-line parameters. For another environment, make the script consume a protected credential source before running it.

For restart behavior, run the existing durability and file-deployment checks against the rebuilt host:

```bash
pwsh ./e2e-tests/durability/Test-RestartRecovery.ps1
pwsh ./e2e-tests/file-deployment/Test-FileBasedDeployment.ps1
```

`Test-FileBasedDeployment.ps1` proves the concrete `/health/ready` gate, import/publish/execute behavior, and idempotent restart for its file-deployment fixture. It is useful restart evidence, but it does not prove shared PostgreSQL resource routing.

## Shared layout proof (#1968)

After the resource-aware implementation and fixture exist, use one explicit shell and one explicit shared resource. The proposed invocation shape is intentionally shown as a contract example only:

```bash
dotnet elsa persistence list \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME"

dotnet elsa persistence plan \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME" \
  --resource primary \
  --provider PostgreSql \
  --from-host

dotnet elsa persistence apply \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME" \
  --resource primary \
  --provider PostgreSql \
  --from-host \
  --connection-env ELSA_EF_CONNECTION
```

`--configuration-context` and `--resource` are proposed #1967/#1968 flags. `--shell` exists in the current CLI, but its resource-context semantics and negotiated transport are future contract work. The current CLI does not implement this resource-aware contract; do not run these commands against the current build and do not report them as passing until the protocol, host capability negotiation, and target-selection implementation land.

The expected shared participant set is the eight Runtime feature identities, `WorkflowsDesignEntityFrameworkCore`, `ActivitiesDesignEntityFrameworkCore`, and `WorkflowsPublishingEntityFrameworkCore` ([spec](spec.md#normative-supported-participants-and-constraints)). `--from-host` must include dependency-enabled participants before deriving modules. A hand-written module list must not silently omit another enabled participant sharing the same Runtime context.

The acceptance sequence is:

1. `list` shows the host-discovered module set without opening PostgreSQL.
2. `plan` resolves the selected resource and migration order without resolving an expected secret or opening PostgreSQL.
3. `script` emits only selected SQL and a redacted `migration-plan.json`; it does not open PostgreSQL.
4. `apply` uses the actual connection only from `ELSA_EF_CONNECTION` or stdin, compares it with the named connection in the selected context, and refuses before context/connection creation on mismatch.
5. `validate` succeeds against the same target and reports no pending migrations.
6. The HTTP workflow smoke completes while the Workbench runtime uses the shared target.

For a stdin check, use a protected pipe supplied by the deployment environment; do not replace it with a literal connection string:

```bash
some-protected-secret-command | dotnet elsa persistence validate \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME" \
  --resource primary \
  --provider PostgreSql \
  --from-host \
  --connection-stdin
```

The placeholder `some-protected-secret-command` is intentionally not a repository run path. Use the deployment system's existing secret injection mechanism.

## Diagnostics split proof (#1969)

The diagnostics layout binds both `DiagnosticsStructuredLogsEntityFrameworkCore` and `DiagnosticsOpenTelemetryEntityFrameworkCore` to the diagnostics resource while the shared Runtime/Design/Publishing set remains on the primary resource. Resource names do not prove physical target equality; the tooling must resolve the named references in one context and apply the strict expected-versus-supplied connection check ([target selection](decisions/tooling-target-selection.md#module-cannot-be-partially-validated)).

Use the same explicit context and shell, selecting the diagnostics resource for the diagnostics module set. The final command shape is future contract documentation, not a currently runnable command:

```bash
dotnet elsa persistence plan \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME" \
  --resource diagnostics \
  --provider PostgreSql \
  --modules Diagnostics.StructuredLogs,Diagnostics.OpenTelemetry

dotnet elsa persistence validate \
  --host "$HOST_DIR" \
  --configuration-context workbench-json-v1 \
  --environment "$ENVIRONMENT_NAME" \
  --shell "$SHELL_NAME" \
  --resource diagnostics \
  --provider PostgreSql \
  --modules Diagnostics.StructuredLogs,Diagnostics.OpenTelemetry \
  --connection-env ELSA_EF_CONNECTION
```

Run the existing diagnostics HTTP checks against the rebuilt host after the database proof:

```bash
pwsh ./e2e-tests/diagnostics/Test-OpenTelemetryApiMigration.ps1
pwsh ./e2e-tests/logging/Test-DiagnosticsSettings.ps1
pwsh ./e2e-tests/logging/Test-ValueCapture.ps1
```

The current diagnostics smoke covers the real query, SSE, OTLP routes and the read-only runtime diagnostics settings endpoint. Its route set includes:

```text
GET  /runtime/workflows/diagnostics/settings
POST /diagnostics/opentelemetry/resources/search
POST /diagnostics/opentelemetry/traces/search
POST /diagnostics/opentelemetry/metrics/search
POST /diagnostics/opentelemetry/logs/search
GET  /diagnostics/opentelemetry/storage
GET  /diagnostics/opentelemetry/collector-configuration
GET  /_elsa/studio/diagnostics/opentelemetry/stream
POST /elsa/otlp/v1/traces
POST /elsa/otlp/v1/metrics
POST /elsa/otlp/v1/logs
```

These existing checks prove route composition and diagnostics behavior; they do not yet prove that the diagnostics EF contexts use a second PostgreSQL target. That requires the #1969 resource fixture and database inspection below.

## Artifact compatibility

Keep legacy script/manifest version 1 behavior unchanged. Explicit context produces the version-2 artifact in [the tooling contract](contracts/tooling.md). Verify repeated generation is byte-identical and changes to serialized non-secret context/evidence are reported even when SQL is unchanged. Script-check reconstructs context/resource/shell/environment selection from the committed manifest; it does not accept the new selection flags. Test old/partial host capabilities and malformed version-2 responses before permitting legacy execution or output. Neither offline artifact version resolves expected connection values.

## Database proof and restart

Run database inspection from a protected PostgreSQL service, without passing a password or connection string as an argument:

```bash
psql --no-psqlrc --command '\dt' | tee "$EVIDENCE_DIR/primary-tables.txt"
psql --no-psqlrc --command \
  "select table_name from information_schema.tables where table_schema = 'public' and table_name like '__EFMigrationsHistory_%' order by table_name;" \
  | tee "$EVIDENCE_DIR/primary-history-tables.txt"
```

Repeat with the diagnostics service name. The evidence must show the expected module history rows and domain tables in the selected target, and that the other target does not contain modules assigned to it. Use the generated migration plan and module descriptors as the source of the exact expected table/history names; do not infer ownership from similarly named tables. Existing documentation identifies Runtime history as `__EFMigrationsHistory_ElsaRuntime` and Activities Design history as `__EFMigrationsHistory_ElsaActivitiesDesign` ([tools/ef README](../../tools/ef/README.md#reviewable-sql-scripts-script--script-check)).

The database check is incomplete until it also records:

- provider and schema for every selected context;
- migration-history IDs at the expected head;
- representative Runtime, Design, Publishing and diagnostics rows created by the HTTP journeys;
- absence of a diagnostics module table/history row in the primary target and absence of a primary module row in the diagnostics target;
- transaction/context-affinity evidence required by the selected layout, without claiming that resource-name equality proves physical target equality.

Then stop and start the same published/rebuilt host again using the same source context. Re-run `GET /health/ready`, the workflow smoke, diagnostics smoke, and the history/table queries. A passing restart proves durable placement and recomputation for that fixture; it does not prove another provider, arbitrary custom feature ownership, or an independently running host with different environment overrides.

## Negative and refusal matrix

Run each case against a disposable host/target and capture exit code, redacted refusal, and database side-effect count. For invalid resource composition, prove refusal before feature effects and artifact output. For live expected-target failure, prove zero DbContext/connection creation and database activity. Failures after valid target checks retain their existing operation-specific boundary; do not claim every database error happens before database access.

| Case | Expected result |
|---|---|
| Resource definition exists but no enrolled feature binding/default selects it | Legacy/no-resource behavior; definitions alone do not activate a target. |
| Explicit binding is `null`, empty, or unknown | Refuse; these values are not binding removal. |
| Remove a binding at its owning source | Inherit the shell default, then root default, then legacy configuration; unrelated authored settings remain. |
| Direct feature is disabled and has a resource binding | It is inactive for applicability. A dependency reintroduced by final CShells ordering is active and must produce an explicit disabled-required conflict before feature side effects. |
| Binding names an unknown or un-enrolled feature | Preserve it as unresolved; do not enroll it or report readiness. |
| A dependency-enabled enrolled participant selects a resource | Include it in the effective participant/module set. Do not use only directly requested feature IDs. |
| Applicable resource plus authored legacy Provider, ConnectionString, or ConnectionName | Refuse as ambiguous; initialized CLR defaults do not count as authored. |
| Missing resource or incomplete Provider/ConnectionName pair | Refuse selected invalid intent before side effects; never substitute SQLite. |
| Missing expected named connection value | Live commands refuse before context/connection creation; offline commands leave the live prerequisite unresolved without looking up the value. |
| Wrong `--provider`, mixed Runtime provider/target, or incompatible target module | Refuse before a context/connection is created. |
| Offline `list`, `plan`, or `script` | Do not resolve expected live connection values or claim target parity. |
| `--connection-env` or `--connection-stdin` omitted for a live operation | Refuse; actual input is never manufactured from the expected configured value. |
| Context-aware request sent to an old host | Refuse through capability negotiation before sending an unsupported field; never fall back to the legacy provider-only projection. |
| Environment-only resource override with no explicit environment context | Mark external resolution unchecked; do not claim resource readiness or strict target verification. |

For source/presence cases, test both root and shell defaults, JSON overlays, explicit false/zero/empty/null, feature object-map and array forms, and the reset behavior recorded in [authored persistence](decisions/authored-persistence.md#effective-authored-presence). Preserve a redacted canary through error paths to prove no connection value appears in stdout, stderr, manifest, plan, exception, public request metadata, or process arguments. The existing trusted internal live-operation JSON may carry the actual connection; it must never be printed or exported.

## Reload and legacy feature-editor guard

The runtime must recompute resource-derived settings from a fresh source snapshot before binding on every shell generation. The delivered CShells preparation seam runs after global/default composition and dependency expansion, before feature construction, binding, configurators, and service registration ([research R2](research.md#r2-cshells-lifecycle-integration)).

For a supported reload fixture:

1. Start the host and record `/health/ready`, the effective resource evidence, and baseline database placement.
2. Change the authored binding/default in the disposable shell configuration without changing unrelated settings.
3. After the fixture observes the configuration-provider change, invoke Workbench's existing `POST /_admin/shells/reload/{name}` with the selected URL-encoded shell name. Supply `X-Elsa-Module-Management-Key` from a protected credential source matching `Elsa:ModuleManagement:ApiKey` ([mapping](../../src/apps/Elsa.Workbench/Program.cs), [authentication](../../src/apps/Elsa.Workbench/ManagementApiKeyAuthentication.cs)). Do not put the key in argv or evidence. No configured key returns 404; a missing/wrong supplied key returns 401. The existing reload response may be HTTP 200 with `success: false`; inspect `success`, `newShell`, `drain` and safe `error` fields, then verify readiness and generation. This reuses the root host-control API and requires no new endpoint.
4. Confirm the next generation uses the new effective target and that the old materialized target is not retained.
5. Re-run the relevant workflow/diagnostics smoke and database proof. A failed candidate must retain the previous active generation. The authored file the operator changed remains changed; this does not claim source rollback.

The legacy feature editor must run mandatory resource applicability preparation after restoring masked secrets and validating the request, before every ordinary activation guard, save, catalog refresh and shell reload. The current ordering seam is `FeatureManagementService.ApplyAsync`: request load/restore/validation, activation guards, then save and reload (`src/essentials/Modularity/Nuplane/Services/FeatureManagementService.cs:20-50`). Exercise `POST /modularity/features/apply` only after mandatory resource preparation is implemented. A current-only, candidate-only or invalid applicable resource selection must return HTTP 409 with the `[resource-managed-configuration]` prefix in the existing `errors.generalErrors` message before all ordinary guards and leave `shells.json`, the feature catalog, and active shells unchanged; a purely legacy request must retain the existing behavior. Existing activation-guard tests demonstrate the intended no-save/no-reload boundary (`tests/essentials/Modularity/EntityFramework/Tests/EfPendingMigrationActivationGuardTests.cs:221-239`), but they are not proof of the new resource guard.

## Evidence record

For each run, record:

```text
source commit / package versions:
host project and output directory:
shell and explicit environment:
configuration context mode:
resource and selected module set:
provider and schema:
CLI command shape (without secrets):
exit code and redacted output:
HTTP suite names and results:
database target/service identity:
migration-history evidence:
domain-row evidence:
restart result:
reload result:
negative cases and side-effect checks:
known unverified sources (environment, custom providers, host process parity):
```

Do not attach raw configuration snapshots, connection strings, passwords, hashes of secrets, or process listings containing secret-bearing arguments. Report source mode and the identities checked, as required by [FR-011 and FR-014](spec.md#functional-requirements).

## Current implementation gaps

This guide deliberately leaves the following as implementation gates rather than pretending they pass:

- CShells `0.0.30-preview.158` is pinned; the resource preparation adapter is not implemented yet.
- The resource resolver, runtime pre-binding adapter, Workbench resource fixture, and management pre-guard are not present in this specification tree.
- The proposed `--configuration-context` and `--resource` protocol fields, resource-aware `--shell` transport, and old-host capability negotiation are not implemented by the current CLI.
- No committed #1968 shared-layout or #1969 diagnostics PostgreSQL e2e script exists yet.
- Current e2e scripts prove SQLite/default-shell HTTP behavior and diagnostics routes, not named-resource target placement.
- The exact Workbench PostgreSQL launch/profile and disposable target provisioning path must be committed with implementation before this quickstart can be used as a release gate.
- A green pure resolver, component suite, or existing SQLite e2e run is not database, restart, transaction, or cross-domain target proof.
