# Feature Specification: File-based workflow deployment at startup

**Feature Branch**: `780-file-workflow-deployment`

**Created**: 2026-08-06

**Status**: Draft

**Issue**: [elsa-workflows/elsa-foundation#1157](https://github.com/elsa-workflows/elsa-foundation/issues/1157) — "No file-based workflow deployment: JsonWorkflowReconciliation is unshipped, folder-less, and never publishes" (gaps 1–3 + readiness note are in scope here; gap 4, hand-authorable `(activityTypeKey, version)` input, is the excluded stretch item)

**Input**: File-based workflow deployment at startup (JsonWorkflowReconciliation completion): start an Elsa server with workflow definition JSON files on disk (e.g. a docker volume mounted at `/app/workflow-definitions`) and have those workflows imported AND published — executable — when the server is up, with zero API calls. Scope: (1) package `JsonWorkflowReconciliationFeature` into the server app (`Elsa.Workbench`, formerly `Elsa.Server`); (2) add a `FolderPath` option (mutually exclusive with `FilePath`/`Files`, deterministic `*.json` scan); (3) opt-in `PublishOnReconcile` that publishes the latest reconciled version of each source-owned definition via the in-process publishing engine (`PublishWorkflow` request), living on the Publishing side of the domain seam, idempotent across restarts; (4) docs + example (shells.json snippet, definition-file authoring guidance, readiness note); (5) unit tests + e2e test (start server with definitions folder, wait for `/health/ready`, assert definition imported, active publication exists, executable executes).

## Context

This feature completes the CI/testing/GitOps deployment story begun in `specs/085-workflow-definition-gitops`. The prerequisite landed in `specs/145-publishing-engine-split` (PR #1109): the publish engine is now a standalone, endpoint-free `WorkflowsPublishing` feature callable in-process via the `PublishWorkflow` request.

Verified current-state gaps (2026-08-06, re-verified after the `Elsa.Server` → `Elsa.Workbench` rename):

1. `JsonWorkflowReconciliationFeature` (`src/Elsa/Workflows/Design/Reconciliation/Json/`) exists but is **not packaged** into the server app: `Elsa.Workbench.csproj` references only the *activities* reconciliation projects, and `Program.cs` does not register its assembly in the feature catalog. A mounted `shells.json` entry is silently skipped ("requested feature(s) not available in the runtime feature catalog") — empirically confirmed against the published container image.
2. Its options support only `FilePath` (single file) XOR `Files` (explicit ordered list). No folder scanning. (The git reconciliation source enumerates directories — precedent exists.)
3. Reconciliation **imports only**: the version reconciler materializes design definitions/versions and never touches the Publishing domain. Nothing produces a publication or executable, so nothing is runnable after startup.
4. Reconciliation runs as a startup task (`[SingleNodeTask]`, `[Order(2)]`) during shell activation, which happens **after** the server starts listening; `GET /health/ready` is the real readiness gate (the docker HEALTHCHECK's `/` returns 200 unconditionally).

## User Scenarios & Testing *(mandatory)*

The "users" are **operators/CI pipelines** deploying workflow definitions as files (GitOps), **shell composers** enabling the capability via configuration, and **workflow authors** producing the definition files.

### User Story 1 - Deploy workflow definitions from mounted files, zero API calls (Priority: P1)

An operator starts the server container with workflow definition JSON files on a mounted volume and a `shells.json` that enables `JsonWorkflowReconciliation`. When the server reports ready, every definition in those files exists in the design store — imported at startup, with no API calls and no code changes.

**Why this priority**: This is the packaging gap that blocks everything else. The feature code exists and works; it simply cannot be activated in the shipped server app. Without it the configuration entry is silently skipped.

**Independent Test**: Configure `JsonWorkflowReconciliation` (with `FilePath` or `Files`) in the server's shell configuration, start the app, wait for `/health/ready`, and assert the definition is present via `GET /design/workflows/definitions?name=...`. Deliverable even without folder support or publishing.

**Acceptance Scenarios**:

1. **Given** a server app whose shell configuration enables `JsonWorkflowReconciliation` pointing at a definition file, **When** the app starts and reaches readiness, **Then** the definitions in that file exist in the design store with the configured `SourceId` as their source identity.
2. **Given** the same configuration, **When** the runtime feature catalog is built, **Then** `JsonWorkflowReconciliation` resolves (no "requested feature(s) not available" skip).

---

### User Story 2 - Reconciled definitions are published and executable (Priority: P1)

An operator sets `PublishOnReconcile: true` on the JSON reconciliation source. After startup, the latest reconciled version of each definition owned by that source is published — an active publication exists and the resulting executable can be started — with zero API calls.

**Why this priority**: Import alone does not deliver the deployment story; "deployed" means executable. This is the half of the goal the publishing-engine split was done to enable.

**Independent Test**: Start the server with a definitions source and `PublishOnReconcile: true`; after readiness, assert an active publication exists (`GET /publishing/workflows/{definitionId}/slots`) and `POST /runtime/workflows/executables/{artifactId}/execute` completes.

**Acceptance Scenarios**:

1. **Given** a reconciliation source with `PublishOnReconcile: true`, **When** a reconcile pass completes successfully, **Then** the latest reconciled version of each source-owned definition is published via the in-process publish engine and an active publication occupies that definition's publication slot.
2. **Given** `PublishOnReconcile` unset or `false`, **When** the reconcile pass completes, **Then** behaviour is exactly today's: definitions are imported and nothing is published (opt-in, default off).
3. **Given** a server restart with unchanged definition files, **When** the reconcile-and-publish pass runs again, **Then** no duplicate publications are created — versions whose publication slot already holds an active publication for that version are skipped (idempotent).
4. **Given** one definition whose publish fails (e.g. validation error), **When** the pass runs, **Then** the failure is surfaced in logs/problem details with actionable detail, and publishing of the remaining definitions still proceeds — no silent skips, no all-or-nothing abort.

---

### User Story 3 - Point at a folder of definition files (Priority: P2)

An operator mounts a directory of `*.json` definition files (e.g. `-v ./defs:/app/workflow-definitions:ro`) and configures a single `FolderPath` instead of enumerating files. New files added to the repo folder are picked up on the next deploy without touching `shells.json`.

**Why this priority**: This is the ergonomic GitOps shape — one stable configuration, content-driven deployment. Valuable but workable around via `Files` until it lands.

**Independent Test**: Configure `FolderPath` at a directory containing several `*.json` files, start the server, and assert every file's definitions were imported; assert configuration with both `FolderPath` and `FilePath` (or `Files`) is rejected at registration.

**Acceptance Scenarios**:

1. **Given** `FolderPath` pointing at a directory with multiple `*.json` files, **When** the server starts, **Then** all files are read in a deterministic, documented order and their definitions imported.
2. **Given** a configuration setting more than one of `FilePath` / `Files` / `FolderPath` (or none), **When** the feature registers, **Then** registration fails with a clear validation error naming the exactly-one rule (extending the existing XOR validation pattern).
3. **Given** an empty folder or a folder containing non-JSON files, **When** the server starts, **Then** startup succeeds, non-`*.json` entries are ignored, and an empty scan is logged (not an error).

---

### User Story 4 - Author and operate with confidence (docs) (Priority: P3)

A workflow author and an operator can follow documentation to produce working definition files and a working deployment: a `shells.json` snippet, authoring guidance (pin `definitionId`; how to obtain resolved `actver_*` activity-version ids), and the readiness note (`/health/ready` is the gate, `/` is not).

**Why this priority**: The file format has sharp edges (omitted `definitionId` generates a fresh random id per restart → duplicate definitions; `activityVersionId` must be a resolved deterministic catalog id). Without guidance the feature produces confusing failure modes.

**Independent Test**: Follow the docs from scratch: author a definition file, configure the shell, start the container, reach readiness, execute the workflow.

**Acceptance Scenarios**:

1. **Given** the docs, **When** an author creates a definition file following them, **Then** the file imports and publishes without trial-and-error (pinned `definitionId`, resolvable `activityVersionId`s).
2. **Given** the docs' deployment example, **When** an operator applies the `shells.json` snippet and volume mount, **Then** the acceptance flow (below) passes.

---

### Edge Cases

- **Omitted `definitionId`**: the definition factory generates a fresh random id per restart, producing duplicates across restarts. Docs MUST direct authors to pin `definitionId`; the import path SHOULD warn when a file-sourced definition omits it.
- **Unresolvable `activityVersionId`**: a `state.rootActivity.activityVersionId` that is not a resolved `actver_*` catalog id must fail with an actionable error naming the file and definition, not import a broken definition.
- **Malformed JSON file in a folder**: surfaced as an error naming the file; the pass's handling of remaining files follows the existing multi-file semantics (documented behaviour, not silent skip).
- **Restart idempotency (import)**: unchanged files on restart must not create new definition versions (existing content-hash reconciliation behaviour must hold for folder-sourced files too).
- **Restart idempotency (publish)**: an active publication already occupying the slot for the latest reconciled version ⇒ publish is skipped for that definition.
- **Multi-node**: reconcile and publish-on-reconcile must run on a single node per pass (`[SingleNodeTask]` + distributed lock, per the git export startup task precedent).
- **Ordering across the seam**: the publish step must run only after a successful reconcile pass for the source; a failed reconcile pass must not trigger publishing of partially imported state.
- **Deleted definitions**: a file envelope marked `deleted` (or a definition removed from the source) follows existing reconciliation deletion semantics; publish-on-reconcile must not resurrect or publish deleted definitions.
- **Readiness vs. liveness**: reconciliation (and publish-on-reconcile) run during shell activation after the listener is up; `/` returns 200 before workflows are deployed. Docs and the e2e test must gate on `/health/ready`.
- **Mounted-volume folder quirks**: read-only mounts must work; the folder scan must not double-read files exposed through mount-implementation symlinks (e.g. Kubernetes ConfigMap `..data` layouts) — a top-level, non-recursive scan avoids this class of problem.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The server app (`Elsa.Workbench`) MUST package the workflow reconciliation features — project references for `Elsa.Workflows.Design.Reconciliation` and `Elsa.Workflows.Design.Reconciliation.Json` and registration of their assemblies in the runtime feature catalog — so that a `JsonWorkflowReconciliation` entry in shell configuration activates instead of being silently skipped.
- **FR-002**: `JsonWorkflowReconciliationOptions` MUST gain a `FolderPath` option. Exactly one of `FilePath` / `Files` / `FolderPath` MUST be configured; the feature MUST reject any other combination at registration with a clear validation error (extending the existing XOR validation pattern).
- **FR-003**: `FolderPath` MUST scan `*.json` files in the folder's top level only (non-recursive), ordered deterministically by file name using ordinal comparison, and feed them through the same read path as `Files`. The recursion decision and ordering MUST be documented on the option.
- **FR-004**: An empty or all-ignored folder MUST NOT fail startup; the scan result MUST be logged. A missing folder MUST fail with an actionable error naming the configured path.
- **FR-005**: The options MUST gain an opt-in `PublishOnReconcile` flag (default `false`). When `true`, after a successful reconcile pass the latest reconciled version of each definition owned by that source MUST be published via the in-process publish engine (`PublishWorkflow` request from `Elsa.Workflows.Publishing.Core`).
- **FR-006**: The publish step MUST live on the Publishing side of the Design↔Publishing domain seam — an event subscriber on the reconciliation-completed event or a publishing-domain startup task ordered after the reconciler — and MUST NOT introduce a Design→Publishing (or Publishing→Design implementation) reference that violates the architecture guard tests and the constitution's artifact seam.
- **FR-007**: Publish-on-reconcile MUST be idempotent across restarts: before publishing, the step MUST check the definition's publication slot and skip versions for which an active publication of that version already exists.
- **FR-008**: Publish-on-reconcile MUST run single-node per pass (`[SingleNodeTask]` + distributed lock), following the `GitWorkflowExportStartupTask` precedent.
- **FR-009**: Publish failures MUST be surfaced as structured log entries / problem details naming the definition and cause; a failure for one definition MUST NOT abort publishing of the others, and MUST NOT fail shell activation (the server still reaches readiness; the failure is observable).
- **FR-010**: Documentation MUST cover: a `shells.json` snippet for the mounted-folder deployment, definition-file authoring guidance (envelope shape, pinned `definitionId`, how to compute or obtain resolved `actver_*` activity-version ids), and the readiness note (`/health/ready` gates deployment completion; `/` does not).
- **FR-011**: Unit tests MUST follow the existing `JsonWorkflowReconciliation*Tests` patterns (feature registration incl. XOR validation, folder scan determinism, publish-on-reconcile behaviour incl. idempotent skip and failure isolation). An e2e test in `e2e-tests/` MUST follow its conventions: start the server with a definitions folder configured, wait for `/health/ready`, assert the definition exists (`GET /design/workflows/definitions?name=...`), an active publication exists (`GET /publishing/workflows/{definitionId}/slots`), and `POST /runtime/workflows/executables/{artifactId}/execute` completes.

### Key Entities

- **JSON workflow reconciliation source**: the file-backed source contributing definition version envelopes; gains `FolderPath` and `PublishOnReconcile` options.
- **Definition file (envelope array)**: top-level JSON array of version envelopes (`definitionId?`, `name`, `description?`, `version` SemVer, `state`, `sourceCreatedAt?`, `contentHash?`, `deleted?`), camelCase; `state` activity references use resolved `actver_*` catalog ids.
- **Publish-on-reconcile step**: the publishing-domain unit that reacts to a completed reconcile pass and publishes latest reconciled versions idempotently.
- **Publication slot**: the per-definition slot whose active publication is the idempotency check and the "executable exists" evidence.
- **Readiness gate**: `/health/ready`, which turns ready only after shell activation (and thus startup reconciliation) completes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** *(acceptance)*: A container built from `main`, started with `-v ./defs:/app/workflow-definitions:ro` and a shell configuration containing `"JsonWorkflowReconciliation": { "Options": { "SourceId": "mounted-definitions", "FolderPath": "/app/workflow-definitions", "PublishOnReconcile": true } }`, reaches `/health/ready` with every definition in the folder imported, published, and executable — with zero API calls.
- **SC-002**: Restarting that container with unchanged files produces zero new definition versions and zero new publications (idempotent import and publish), verified by comparing store contents across restarts.
- **SC-003**: With `PublishOnReconcile` absent, observable behaviour of existing `FilePath`/`Files` configurations is unchanged (existing tests pass unmodified).
- **SC-004**: Architecture guard tests pass: no new dependency crosses the Design↔Publishing seam in a prohibited direction.
- **SC-005**: The e2e test passes in CI: readiness reached, definition present, active publication present, execution completes.
- **SC-006**: A misconfiguration (two path options, missing folder) fails fast at registration/startup with an error message that names the offending option(s) — no silent skip.

## Assumptions

- **Folder scan shape**: top-level, non-recursive `*.json` scan with ordinal file-name ordering. Rationale: mounted-volume layouts (notably Kubernetes ConfigMap `..data` symlink trees) make recursive scans double-read files; authors who need staging/ordering beyond file-name order can use `Files`. If recursion is later wanted, it becomes a new explicit option rather than a behaviour change.
- **"Latest reconciled version"** means the highest-version envelope the source contributed for a definition in the completed pass (per existing SemVer ordering), not "latest in the store".
- **Publish engine availability**: `PublishOnReconcile: true` requires the `WorkflowsPublishing` engine feature to be active in the same shell; the feature declares/validates this dependency rather than failing at first publish.
- **Stretch item excluded**: accepting `(activityTypeKey, version)` pairs in files and resolving them to `actver_*` at import is a separate follow-up task; this feature documents the precompute recipe instead.
- **Existing single-file/multi-file read semantics** (error handling for malformed JSON, content-hash based no-op detection) are correct and are reused, not redesigned.
- **The `Elsa.Server` → `Elsa.Workbench` rename** (landed on `main` 2026-08-06) is complete; all packaging work targets `Elsa.Workbench` and the `Workbench*` architecture tests.
