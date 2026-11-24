# Research: Final JTest acceptance

Each item records a decision, the reason for it and the alternatives considered. File references are to this
repository unless they name the corpus.

## R-1 JTest version reads the corpus

- **Decision**: Use the latest `JTest.Cli` release, installed as a global dotnet tool (install or update it
  outside this repository: its package source mapping blocks `JTest.Cli`).
- **Rationale**: `jtest validate` with 2.0.3 reports all 42 suite files and the setup suite valid against JTest language
  schema 1.0.0, so the 2.x line reads the corpus without conversion. The original pipelines pinned 0.1.3.
- **Alternatives considered**: Pin 0.1.3 as the original pipelines did. Rejected by the coordinator: the latest CLI is
  wanted and reads the files.

## R-2 Elsa 3.6.3 baseline server

- **Decision**: Build `docker/ElsaServer.Dockerfile` from elsa-core release tag `3.6.3` (commit `d6a57f5f`) and run it
  as the original pipeline ran its image: `ASPNETCORE_ENVIRONMENT=Development`, `HTTP_PORTS=8080`, `HTTP__BASEURL`
  set to the published base URL, health check on the root returning `Healthy`. Record the image digest per run.
- **Rationale**: No `elsa-server-v3-6-3` image exists on Docker Hub; the published `elsa-server` tags skip 3.6.x. The
  release tag is the exact version production pins.
- **Alternatives considered**: `elsa-server-v3-6-preview` (not exactly 3.6.3); a production image (not public).

## R-3 Where the suite lives and how it is laid out

- **Decision**: `extensions/Elsa3/tests/JTest/`, keeping the corpus's own layout (`workflow-definitions/`, `tests/`,
  `templates/`, `environments/`, `test-files/`, `global-configuration.json`, `import-and-publish-workflows.json`).
  The corpus's `pipelines/` folder and Nexxbiz README are not copied. Elsa 4 additions sit next to it:
  `templates-elsa4/`, `environments/elsa4.json`, `global-configuration.elsa4.json`, `adjusted/`, `ledger/` and a
  `README.md` with run instructions.
- **Rationale**: Keeping the original layout makes the first commit a plain copy and the later hand-over to the
  original repository a straightforward diff. The folder holds no csproj, so no CI tier gate (§E7) applies.
- **Alternatives considered**: A restructured `suites/` and `definitions/` layout; rejected because every move
  obscures the diff from the source.

## R-4 Running the suites

- **Decision**: Plain `jtest run` with `--env-file` and `JTEST_CONFIG_FILE` pointing at the engine's configuration,
  started by hand from the suite folder. No wrapper script and no xunit project.
- **Rationale**: The coordinator's decision (Q6). The commands fit in the README; a script would only be worth it once
  automation starts.
- **Alternatives considered**: An xunit launcher per suite; rejected (Q6).

## R-5 Elsa 4 host for the run

- **Decision**: The Workbench, started fresh on its default SQLite store, with the Elsa 3 startup reconciliation
  feature and the import persistence feature `Elsa3ImportActivitiesEntityFrameworkCore` (which no feature dependency
  pulls in) enabled through configuration, and `WorkflowsRuntimeCheckpointPersistence` set to `Durable`. Two small
  Workbench changes are needed: a project reference to the reconciliation project and its assembly in the feature
  catalog (`apps/Elsa.Workbench/Elsa.Workbench.csproj`, `apps/Elsa.Workbench/Program.cs`). The feature stays off in
  the default shell; the run enables it.
- **Rationale**: The Workbench already has the Elsa 3 import assemblies in its feature catalog (Program.cs registers
  four), none enabled in the default shell, and not the `Elsa3JsonReconciliation` assembly. Balanced persistence folds per-activity evidence to commit
  boundaries (`RuntimePersistenceModeProjection`), and the suites count individual activity executions, so the run
  needs Durable.
- **Alternatives considered**: A dedicated test host project (more code for the same result); Nuplane delivery of the
  reconciliation package (untested path).
- **To verify in the tasks**: whether configuration overrides (environment variables or a run-specific shells file)
  can enable the feature and set its options without editing the committed `shells.json`.

## R-6 HTTP trigger URLs

- **Decision**: No host change. The corpus already takes the trigger base URL from `elsaExecutorApi:baseUrl`; the
  Elsa 4 environment file sets it to `{base}/workflows/http`, Elsa 4's default `ActivitiesHttp.BasePath`.
- **Rationale**: The converter copies the Elsa 3 path unchanged, and suites build `{executor base}/{endpoint}`.
- **Alternatives considered**: Setting `ActivitiesHttp.BasePath` to `/workflows`; unnecessary, and it touches the
  reserved `/workflows/test-runs` namespace.

## R-7 Identity mapping

- **Decision**: Definition ids need no mapping: the converter keeps the Elsa 3 definition id, and at converter
  revision 0 the version id equals the Elsa 3 version id. Activity ids need mapping in the Elsa 4 templates: Elsa 4
  node ids are the Elsa 3 `nodeId` path (for example `Workflow1:c2e04992faf9e89c:d7f8239de7e28db3`), while suites pass
  the short Elsa 3 `id`; templates match a node whose id equals the short id or ends with `:<id>`.
- **Rationale**: `Elsa3ConversionDiagnostics.NodeId` prefers `nodeId`, and publishing sets `executableNodeId` and
  `authoredActivityId` from it. Matching in the template keeps the suites unchanged.
- **Alternatives considered**: Rewriting suite globals to full node ids; rejected because it changes every suite and
  breaks the Elsa 3 run.

## R-8 Status and version vocabulary

- **Decision**: Elsa 4 templates translate to the Elsa 3 output vocabulary, so `workflowStatus`/`workflowSubStatus`
  keep their meaning: Completed → Finished/Finished, Faulted → Finished/Faulted, Cancelled → Finished/Cancelled,
  Suspended → Running/Suspended, Running → Running/Executing, Pending → Running/Pending. A version parameter given as
  an integer `n` matches Elsa 4 version `n.0.0`. The table lives in [contracts/templates.md](contracts/templates.md).
- **Rationale**: 37 assertions expect `Finished`, 29 `Completed`, and a few `Running`, `Suspended` or `Faulted`. The
  meaning is identical; only the words differ, which is what a template exists to absorb.
- **Alternatives considered**: Moving expected statuses into environment values during the engine-neutral refactor.
  Kept as the fallback for any status the table cannot express; the refactor must not change an expectation.

## R-9 What each template can observe on Elsa 4

- **Decision**: Templates use these Elsa 4 sources: `POST /_elsa/identity/login` then `GET /_elsa/identity/token` for
  a bearer token; `GET /design/workflows/definitions/{id}` for definition lookups; `/runtime/workflows/instances/...`
  for instances, activity executions and variables. Import and publish templates become checks that the
  reconciliation published the definition. Counts of started or completed activities are computed from the activity
  execution list. Per-activity outputs and variable values come from value snapshots and variable write payloads,
  which require capture to be on for the run.
- **Rationale**: Every Elsa 3 output has an Elsa 4 source with the same meaning except the ones listed in the
  contract as gaps; those are reported (FR-011), not approximated.
- **Alternatives considered**: The execution-evidence long-poll stream, which could replace fixed waits; rejected for
  now because it is in-memory, can drop records, and replacing waits is a suite change, not a template change.

## R-10 Fixed waits

- **Decision**: Keep the 22 fixed `wait` steps as they are. A wait that proves too short on either engine is raised
  as a recorded suite change (FR-012).
- **Rationale**: Changing waits silently would hide timing behaviour the suite was written to tolerate.
- **Alternatives considered**: Template-level polling; possible later, but it changes how suites wait.

## R-11 Prerequisites and known defects

- **Decision**: Phases 1 and 2 and the Elsa 4 template set do not wait for spec 1207. The qualifying Elsa 4 run
  (SC-004) is made on an initiative head where spec 1207 is implemented. A definition that fails to publish is read
  from the reconciliation receipt and dispositioned like any other result.
- **Rationale**: FR-020; dispositions are evidence-led.
- **Alternatives considered**: Waiting for spec 1207 before starting; it would idle the baseline work.
