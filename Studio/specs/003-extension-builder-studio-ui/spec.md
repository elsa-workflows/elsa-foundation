# Feature Specification: Extension Builder — Studio UI (trusted-team v1)

**Feature Branch**: `003-extension-builder-studio-ui`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "Author a Studio UI feature spec (WHAT-level) for an Extension Builder that lets a trusted user, inside the Studio UI, create/edit a .NET project workspace, build it server-side into a NuGet package, promote the validated package into a Nuplane-loadable feed, and see the resulting capability exposed at runtime — with status, diagnostics/logs, and rollback throughout. Generic .NET projects from day one with an Elsa activity/module template as the primary first-class template; trusted teams/admins only in v1; AI/agentic authoring deferred. UI only; the backend build/promote pipeline and its API contracts are specified authoritatively in the separate `elsa-foundation` repository and are consumed by this UI."

> **Backend alignment**: This UI consumes the authoritative backend capability
> surface defined in `elsa-foundation` `specs/075-extension-builder-backend/spec.md`.
> Capability and entity references below use that spec's canonical names. The
> endpoint root is `/_elsa/extension-builder`, which extends the existing
> `/_elsa/module-management` + Nuplane surfaces. See
> [Backend Capability Surface](#backend-capability-surface-consumed-by-this-ui).

## User Scenarios & Testing *(mandatory)*

<!--
  User stories are prioritized user journeys. Each is independently testable:
  implementing just one still yields a viable, demonstrable slice. Priorities
  mirror the authoritative backend spec's slicing (P1–P5).
-->

### User Story 1 - Author, Build, Promote, and See an Elsa Activity Extension Loaded (Priority: P1)

As a trusted Studio user, I want to create an Elsa activity/module project from a
template, edit its source, build it into a package while watching build logs,
promote the validated package into the feed that the runtime loads, and then
confirm the new activity is available at runtime — all without leaving the Studio UI.

**Why this priority**: This is the thinnest end-to-end vertical slice that proves
the whole extension boundary: workspace → editor → build → promote → loaded at
runtime. It is the minimum that delivers user value (a brand-new capability live
in the running product) and exercises every backend capability the rest of the
feature builds on.

**Independent Test**: Starting from an empty Extension Builder, a user creates a
project from the Elsa activity template, makes a trivial code edit, triggers a
build, watches it succeed in the log view, promotes the produced package, and then
observes the package reported as `Loaded` plus the contributed activity appearing
in the runtime status — with no other user story implemented.

**Backend capabilities used**: `CreateWorkspace`/`ListWorkspaces`, `ListTemplates`,
`CreateProject`, `ListProjectFiles`, `ReadProjectFile`, `WriteProjectFile`,
`SubmitBuild`, `GetBuild`, `GetBuildLog`, `PromoteBuild`, `GetRuntimeStatus`.

**Acceptance Scenarios**:

1. **Given** the Extension Builder is open with no projects, **When** the user
   creates a project from the "Elsa activity/module" template (via `ListTemplates` +
   `CreateProject`) and names it, **Then** a workspace appears with a populated file
   tree (`ListProjectFiles`), the template's starter source, and a clear next-step
   affordance to build.
2. **Given** a project workspace is open, **When** the user edits a source file
   (`WriteProjectFile`) and triggers a build (`SubmitBuild`), **Then** the UI streams
   build status (queued → running → succeeded/failed) via `GetBuild` and shows build
   logs via `GetBuildLog`.
3. **Given** a build has succeeded and produced a `BuildArtifact`, **When** the user
   promotes it (`PromoteBuild`), **Then** the UI shows the `PackagePromotionResult`
   (validation outcome) and the resulting runtime state.
4. **Given** a package has been promoted and loaded, **When** the user views runtime
   status (`GetRuntimeStatus`), **Then** the UI lists the package as `Loaded`, shows
   its version, and lists the features/activities the package contributed at runtime.

---

### User Story 2 - Edit, Iterate, and Diagnose Builds (Priority: P2)

As a trusted Studio user, I want to manage my project's files and iterate on code
with a strong build-diagnostics loop, so that when a build fails I can understand
why, jump to the offending code, fix it, and re-build quickly.

**Why this priority**: The inner authoring loop — edit, build, read diagnostics,
fix, repeat — is what makes the builder usable for anything beyond the starter
template. Builds will fail routinely, so the failure UX is core, but the P1 happy
path can ship first.

**Independent Test**: Given a project whose source contains a compile error, a user
edits files in the tree, triggers a build, sees it fail, reads the streamed log,
sees the `BuildDiagnostic` list with severities and locations, navigates from a
diagnostic to the relevant file/line, fixes it, and re-builds to success — verified
independently of promote/runtime.

**Backend capabilities used**: `ListProjectFiles`, `ReadProjectFile`,
`WriteProjectFile`, `DeleteProjectFile`, `SubmitBuild`, `GetBuild`, `GetBuildLog`,
`GetBuildArtifact`.

**Acceptance Scenarios**:

1. **Given** a project is open, **When** the user creates, opens, renames, moves, or
   deletes a file or folder, **Then** the file tree updates and the editor reflects the
   active file, with unsaved (dirty) state made explicit so edits are never silently lost.
2. **Given** a build is running, **When** the user watches the build view, **Then** the
   log streams incrementally and the overall status updates without a manual refresh.
3. **Given** a build failed with compiler errors, **When** the user opens the
   diagnostics list, **Then** each `BuildDiagnostic` shows severity, message, and
   file/line location where available, and selecting it focuses the corresponding
   source location in the editor.
4. **Given** previous builds exist, **When** the user opens build history for a project,
   **Then** they can see prior `BuildResult` outcomes, timestamps, and the logs for each.

---

### User Story 3 - Promotion Validation and Rejection UX (Priority: P3)

As a trusted Studio user, I want clear, actionable feedback when promoting a package
is rejected by server-side validation, so I understand exactly why it was rejected and
what to do next, and never accidentally overwrite an existing package.

**Why this priority**: A build can succeed yet still be unsafe to publish. Promotion
is the gate to the live runtime, so its validation/rejection UX must be unambiguous —
but it depends on the P1 promote path existing.

**Independent Test**: Given built packages that trigger each rejection category, the
user attempts promotion and sees a distinct, actionable message for each of
`duplicate`, `invalid-manifest`, `dependency-policy`, and `malformed-package`, with no
silent overwrite — verified without the editor or runtime stories.

**Backend capabilities used**: `PromoteBuild` (returns `PackagePromotionResult` with a
rejection category), `GetBuildArtifact`.

**Acceptance Scenarios**:

1. **Given** a package whose id + version already exists in the feed, **When** the user
   attempts to promote, **Then** the UI surfaces the `duplicate` rejection, never
   overwrites, and guides the user to bump the version.
2. **Given** a package with manifest problems, **When** promotion is rejected with
   `invalid-manifest`, **Then** the UI explains the manifest issue and points the user at
   what to correct.
3. **Given** a package that violates dependency policy, **When** promotion is rejected
   with `dependency-policy`, **Then** the UI communicates which dependency/policy was
   violated.
4. **Given** a corrupt or malformed artifact, **When** promotion is rejected with
   `malformed-package`, **Then** the UI explains the artifact is unusable and suggests
   re-building.

---

### User Story 4 - Runtime Status, Rollback, and Retry Reconciliation (Priority: P4)

As a trusted Studio user, I want to see the runtime status of my promoted packages
and recover when one does not load cleanly — by retrying reconciliation, rolling back
to a previously loaded version, or being told a restart is required — so a bad
promotion never leaves the runtime stuck with no path forward.

**Why this priority**: Operational safety. Essential for trust, but depends on
promote/runtime status (P1) already existing.

**Independent Test**: Given a project with at least one previously loaded version and a
newly promoted version in `FailedReconciliation`, the user can retry reconciliation,
see a `PendingRestart` state when applicable, and roll back to the prior version with a
clear confirmation and post-rollback runtime status — verified without the editor/build
stories.

**Backend capabilities used**: `GetRuntimeStatus`, `RetryReconciliation`,
`RollbackPackage`.

**Acceptance Scenarios**:

1. **Given** a promoted package in `FailedReconciliation`, **When** the user views its
   runtime status (`GetRuntimeStatus`), **Then** the failure reason/diagnostics are shown
   and a retry action (`RetryReconciliation`) is available.
2. **Given** a promoted package in `PendingRestart`, **When** the user views runtime
   status, **Then** the UI clearly communicates the pending-restart state and what the
   user should expect/do.
3. **Given** a project has more than one promoted version, **When** the user selects a
   previous version and chooses rollback (`RollbackPackage`), **Then** the UI asks for
   confirmation, performs the rollback, and reflects the rolled-back version as `Loaded`.

---

### User Story 5 - Generic (Non-Elsa) .NET Project (Priority: P5)

As a trusted Studio user, I want to create a generic .NET class-library project (not
based on the Elsa activity template) and take it through the same build → promote →
runtime path, so the builder supports extensions beyond Elsa activities from day one.

**Why this priority**: Generic .NET support is in scope from day one, but the
Elsa-activity template is the first-class primary path; generic projects reuse the
same machinery and can follow once P1–P4 are proven.

**Independent Test**: A user creates a project from the generic .NET template, edits
and builds it, and promotes it, observing the same status/diagnostics/runtime
experience as an Elsa project (its contributed capabilities may simply differ or be
none) — verified independently.

**Backend capabilities used**: `ListTemplates`, `CreateProject` (generic template),
plus the same build/promote/runtime operations as P1.

**Acceptance Scenarios**:

1. **Given** the create-project flow, **When** the user lists templates (`ListTemplates`),
   **Then** a generic .NET template is offered alongside the pre-selected Elsa template.
2. **Given** a generic project, **When** the user builds and promotes it, **Then** the
   build, promotion, and runtime-status experience matches the Elsa path, and the runtime
   view honestly reflects whatever capabilities (possibly none) the package contributed.

### Edge Cases

- Promotion of a package whose id + version duplicates an existing feed entry →
  `duplicate` rejection; never a silent overwrite.
- A build is triggered while a previous build for the same project is still running.
- A build succeeds but produces a `BuildArtifact` that contributes nothing loadable.
- Promotion rejected after a successful build for `invalid-manifest`,
  `dependency-policy`, or `malformed-package` reasons.
- Reconciliation fails after a successful promotion (`FailedReconciliation`); the
  runtime keeps the prior version while surfacing the failure.
- A package loads but contributes nothing visible, or contributes items that conflict
  with an already-loaded package.
- Rollback target version no longer present in the feed (e.g., pruned by retention).
- Network/stream interruption during a long build or while streaming logs.
- Concurrent edits to the same project from two trusted sessions/users.
- Very large file trees, very large individual files, or long-running builds.
- A workspace exists but contains no projects, or a project has no built artifact yet.
- The `/_elsa/extension-builder` capability is unavailable or returns an unexpected
  contract shape (the UI must degrade gracefully and explain the limitation).

## Requirements *(mandatory)*

### Functional Requirements

#### Workspace, projects & files

- **FR-001**: The Extension Builder MUST present a browser of workspaces
  (`ListWorkspaces`) and the projects within a workspace, listing at minimum project
  name, package identity (id + version), latest build status, and runtime/loaded status.
- **FR-002**: Users MUST be able to create a workspace (`CreateWorkspace`) and create a
  project within it (`CreateProject`) from a template selected via `ListTemplates`, with
  the Elsa activity/module template offered as the primary, pre-selected option and a
  generic .NET template also available.
- **FR-003**: At project creation, users MUST provide a project name and package
  identity (package id + initial version), and the UI MUST validate these for presence
  and basic format before allowing build/promote.
- **FR-004**: Users MUST be able to view a project's file tree (`ListProjectFiles`) and
  create, open (`ReadProjectFile`), edit/save (`WriteProjectFile`), rename, move, and
  delete (`DeleteProjectFile`) files and folders.
- **FR-005**: The UI MUST make unsaved (dirty) editor state explicit and MUST prevent
  silent loss of edits when switching files, leaving the workspace, or starting a build.
- **FR-006**: Users MUST be able to delete a project (`DeleteProject`) and a workspace
  (`DeleteWorkspace`) with confirmation, and the browser MUST reflect the removal.

#### Code editor

- **FR-007**: The UI MUST provide a code editor for C#/.NET source with standard editing
  affordances (syntax highlighting, multi-file editing, save).
- **FR-008**: The editor MUST surface `BuildDiagnostic` entries inline against the
  relevant file and line where the backend provides location information.
- **FR-009**: Users MUST be able to navigate from a diagnostic entry to the corresponding
  source location in the editor.

#### Build

- **FR-010**: Users MUST be able to trigger a build of the current project revision
  (`SubmitBuild`, producing a `BuildRequest`/`BuildResult`).
- **FR-011**: The UI MUST show build lifecycle status (e.g., queued, running, succeeded,
  failed) via `GetBuild` and update it without requiring a manual page refresh.
- **FR-012**: The UI MUST stream/show build logs (`GetBuildLog`) and present a structured
  list of `BuildDiagnostic`s (errors and warnings) with severity, message, and location
  when available.
- **FR-013**: The UI MUST expose per-project build history, including prior `BuildResult`
  outcomes, timestamps, and the logs/diagnostics for each build.
- **FR-014**: The UI MUST clearly handle a build requested while a build for the same
  project is already in progress (e.g., disable re-trigger or queue) and never present
  ambiguous concurrent-build state.

#### Promote

- **FR-015**: Users MUST be able to promote a successfully built package
  (`PromoteBuild`, sending a `PackagePromotionRequest`), which server-side validates and
  publishes to the Nuplane feed and reconciles.
- **FR-016**: The UI MUST display the `PackagePromotionResult`, including validation
  pass/fail and, on failure, the rejection category — one of `duplicate`,
  `invalid-manifest`, `dependency-policy`, or `malformed-package` — with category-specific,
  actionable guidance.
- **FR-017**: On a `duplicate` rejection, the UI MUST never silently overwrite and MUST
  guide the user to bump the version.
- **FR-018**: The UI MUST only enable promote when a valid `BuildArtifact` exists for the
  project's current revision; otherwise the action is unavailable with an explanation.

#### Runtime status, rollback, recovery

- **FR-019**: The UI MUST show per-project runtime status (`GetRuntimeStatus`,
  `ExtensionRuntimeStatus`) for promoted packages, including the runtime state — one of
  `Loaded`, `PendingRestart`, or `FailedReconciliation` — and the loaded version.
- **FR-020**: The UI MUST list the features/activities a `Loaded` package contributed at
  runtime, or clearly indicate when it contributed none.
- **FR-021**: Users MUST be able to retry reconciliation (`RetryReconciliation`) for a
  package in `FailedReconciliation`, and the UI MUST reflect the new state.
- **FR-022**: Users MUST be able to roll back to a previously promoted/loaded version
  (`RollbackPackage`) with explicit confirmation, and the UI MUST reflect the resulting
  loaded version.
- **FR-023**: When a package is in `PendingRestart`, the UI MUST communicate the
  pending-restart state and expected user action.
- **FR-024**: When a rollback target is unavailable (e.g., pruned by feed retention), the
  UI MUST explain this and prevent the rollback rather than failing opaquely.

#### Cross-cutting UX, consistency, trust, and resilience

- **FR-025**: All Extension Builder screens MUST be composed from the shared Elsa Studio
  UI design language (workbench data grid/list, split inspector, toolbar, status chips,
  alerts, diagnostics/console panels, empty/loading/error states) rather than inventing
  local visual patterns. <!-- aligns with constitution I–III -->
- **FR-026**: All interactive primitives MUST be keyboard operable, expose visible focus
  and accessible labeling, and present disabled/loading/validation states.
- **FR-027**: The Extension Builder MUST be available only to trusted users
  (teams/admins) in v1; it MUST be hidden or access-denied for users lacking the trusted
  capability, and MUST present a clear unauthorized state rather than a broken screen.
- **FR-028**: Every long-running or failure-prone action (build, promote, reconcile,
  rollback) MUST surface progress, success, and failure states with actionable messaging,
  and MUST degrade gracefully if the `/_elsa/extension-builder` capability is unavailable
  or returns an unexpected response.
- **FR-029**: The UI MUST address all backend operations through the
  `/_elsa/extension-builder` capability surface using the canonical operation names so the
  view → capability mappings stay aligned with the authoritative backend spec.

### Backend Capability Surface (consumed by this UI)

Endpoint root: `/_elsa/extension-builder` (extends `/_elsa/module-management` + Nuplane).

| Group | Operations |
|-------|------------|
| Workspace / project | `CreateWorkspace`, `ListWorkspaces`, `GetWorkspace`, `DeleteWorkspace`, `ListTemplates`, `CreateProject`, `GetProject`, `DeleteProject`, `ListProjectFiles`, `ReadProjectFile`, `WriteProjectFile`, `DeleteProjectFile` |
| Build | `SubmitBuild`, `GetBuild`, `GetBuildLog`, `GetBuildArtifact` |
| Promote | `PromoteBuild` (validate + publish to Nuplane feed + reconcile) |
| Runtime / lifecycle | `GetRuntimeStatus`, `RollbackPackage`, `RetryReconciliation` |

Runtime state enum (`ExtensionRuntimeStatus`, per package): `Loaded` | `PendingRestart` | `FailedReconciliation`.

Promotion rejection categories (`PackagePromotionResult`): `duplicate` | `invalid-manifest` | `dependency-policy` | `malformed-package`.

### Key Entities *(include if feature involves data)*

Names mirror the authoritative backend spec.

- **ExtensionWorkspace**: Top-level container owned by a trusted user/team; holds one or
  more projects. Listed/created/deleted via `ListWorkspaces`/`CreateWorkspace`/
  `GetWorkspace`/`DeleteWorkspace`.
- **ExtensionProject**: A .NET project workspace within an `ExtensionWorkspace`. Key
  attributes: name, template kind, package identity (id + version), current revision,
  latest build status, runtime/loaded status. Has many `ProjectFile`s and many builds.
- **ProjectFile**: A node in a project's file tree with a path, type (file/folder), and
  (for files) editable content and dirty state.
- **ExtensionTemplate**: A starting point for a new project (from `ListTemplates`). v1
  offers the Elsa activity/module template (primary) and a generic .NET template; each
  seeds an initial file tree and package identity defaults.
- **BuildRequest**: A submitted request to build a project revision (`SubmitBuild`).
- **BuildResult**: The outcome of a build (`GetBuild`): status (queued/running/succeeded/
  failed), timestamps, diagnostics, and (on success) a produced `BuildArtifact`.
- **BuildDiagnostic**: A single compiler/build message with severity (error/warning/info),
  message text, and optional file + line/column location.
- **BuildArtifact**: The NuGet package produced by a successful build
  (`GetBuildArtifact`), identified by package id + version, eligible for promotion.
- **PackagePromotionRequest**: The request to promote a `BuildArtifact` (`PromoteBuild`).
- **PackagePromotionResult**: The validation/publish outcome: pass, or a rejection
  category (`duplicate` | `invalid-manifest` | `dependency-policy` | `malformed-package`),
  plus resulting runtime state.
- **ExtensionRuntimeStatus**: The current runtime state of a project's loaded package
  (`GetRuntimeStatus`): `Loaded` | `PendingRestart` | `FailedReconciliation`, the loaded
  version, contributed capabilities, the rollback/version history, and failure
  reason/diagnostics when failed.

### Studio Views *(UI surfaces this feature introduces)* — view → capability mapping

- **Workspace & Project Browser** (workbench resource list): create-from-template entry
  point; at-a-glance build/runtime status. → `ListWorkspaces`, `CreateWorkspace`,
  `GetWorkspace`, `DeleteWorkspace`, `ListTemplates`, `CreateProject`, `GetProject`,
  `DeleteProject`, `GetRuntimeStatus` (status badges).
- **Project Workspace (Editor view)**: file-tree + code editor split, build action, inline
  diagnostics. → `ListProjectFiles`, `ReadProjectFile`, `WriteProjectFile`,
  `DeleteProjectFile`, `SubmitBuild`.
- **Build Panel (Logs & Diagnostics)**: streaming status, log output, structured
  diagnostics with navigation to source, build history. → `GetBuild`, `GetBuildLog`,
  `GetBuildArtifact`.
- **Promote & Runtime Inspector**: promotion action + outcome (incl. rejection category),
  runtime/reconcile status, contributed capabilities, version history, recovery actions.
  → `PromoteBuild`, `GetRuntimeStatus`, `RetryReconciliation`, `RollbackPackage`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A trusted user can go from "no project" to "extension activity `Loaded`
  and visible at runtime" entirely within the Studio UI, following the P1 path with no
  external tooling, in under 10 minutes for the starter template.
- **SC-002**: For a build that fails on a compile error, at least 90% of test users can
  locate the offending file/line from the diagnostics view and reach the relevant editor
  location without external documentation.
- **SC-003**: Build status and logs update live; users perceive build progress without
  manually refreshing, and the final outcome (success/failure) is unambiguous in 100% of
  observed builds.
- **SC-004**: 100% of promote attempts that would duplicate an existing package id +
  version are blocked with the `duplicate` rejection; none silently overwrite. Each of the
  four rejection categories renders a distinct, actionable message.
- **SC-005**: For a `FailedReconciliation`, a user can reach a working recovery action
  (`RetryReconciliation` or `RollbackPackage` to a prior loaded version) within the
  runtime status view in under 2 minutes, and the runtime status reflects the outcome.
- **SC-006**: Every Extension Builder screen uses the shared Studio workbench language
  (data grid/list, split inspector, status chips, diagnostics/console panel) with no
  bespoke page-level shell, typography, or status styling.
- **SC-007**: All primary actions (create workspace/project, edit/save, build, promote,
  retry, rollback) are fully operable by keyboard with visible focus and accessible labels.
- **SC-008**: Non-trusted users never reach an interactive builder surface; they always
  see a clear unauthorized/hidden state, in 100% of access checks.
- **SC-009**: A generic (non-Elsa) .NET project completes the same build → promote →
  runtime path as an Elsa project, with the runtime view honestly reflecting its
  contributed capabilities (including none).

## Assumptions

- **Trusted-team v1 only**: The builder targets trusted teams/admins. No multi-tenant
  isolation UI is in scope for v1; tenant isolation is referenced as a later unit.
- **Generic .NET from day one, Elsa template primary**: Project creation supports generic
  .NET projects (P5), with an Elsa activity/module template as the primary, first-class,
  pre-selected template (P1).
- **AI/agentic authoring deferred**: v1 is a manual code editor + build + promote
  experience. The design must not preclude a future AI authoring layer, but no AI UI is in v1.
- **Backend pipeline is authoritative in `elsa-foundation`**: The server-side build,
  validation, promotion, feed loading (Nuplane), and runtime reconciliation (CShells) — and
  their API/entity contracts — are owned by `elsa-foundation`
  `specs/075-extension-builder-backend/spec.md`. This UI consumes that surface at
  `/_elsa/extension-builder` using the canonical names listed above (FR-029).
- **Two-level model**: An `ExtensionWorkspace` contains one or more `ExtensionProject`s;
  the browser reflects both levels.
- **Consumes existing Studio surfaces**: This repo already provides a module/route
  contribution system, shared workbench UI primitives (data grid, inspector, tabs, toolbar,
  status chips, feedback states), and module-management/feed + reconcile surfaces. The
  Extension Builder is a new Studio module/screen that reuses these rather than introducing
  parallel concepts.
- **Editor technology**: A Monaco-style C#/.NET editing experience is assumed for the code
  editor; exact editor integration is an implementation/plan concern, not a spec decision.
- **One package identity per project (v1 assumption)**: Each `ExtensionProject` maps to a
  single package identity (id + version line) for v1; multi-package projects are out of scope.

### Backend-contract alignment items (resolved / remaining)

- ✅ **Resolved**: Canonical capability/endpoint and entity names for build, validation,
  promotion, feed/runtime status, and rollback — now bound to the authoritative
  `elsa-foundation` `075-extension-builder-backend` surface (see FR-029 and the capability
  table). Runtime states (`Loaded`/`PendingRestart`/`FailedReconciliation`) and rejection
  categories (`duplicate`/`invalid-manifest`/`dependency-policy`/`malformed-package`) are aligned.
- [NEEDS CLARIFICATION: How "trusted user" is determined and surfaced to the Studio
  frontend in v1 (capability flag, role, or backend-provided permission) so FR-027 and
  SC-008 can bind to a concrete authorization signal.]
- [NEEDS CLARIFICATION: Whether an `ExtensionWorkspace` is per-user or shared across
  trusted teammates, and where project source is persisted — this drives the concurrent-edit
  behavior referenced in the edge cases.]
