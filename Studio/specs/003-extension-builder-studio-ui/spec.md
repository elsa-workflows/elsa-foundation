# Feature Specification: Extension Builder — Studio UI (trusted-team v1)

**Feature Branch**: `003-extension-builder-studio-ui`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "Author a Studio UI feature spec (WHAT-level) for an Extension Builder that lets a trusted user, inside the Studio UI, create/edit a .NET project workspace, build it server-side into a NuGet package, promote the validated package into a Nuplane-loadable feed, and see the resulting capability exposed at runtime — with status, diagnostics/logs, and rollback throughout. Generic .NET projects from day one with an Elsa activity/module template as the primary first-class template; trusted teams/admins only in v1; AI/agentic authoring deferred. UI only; the backend build/promote pipeline and its API contracts are specified authoritatively in the separate `elsa-foundation` repository and are consumed by this UI."

## User Scenarios & Testing *(mandatory)*

<!--
  User stories are prioritized user journeys. Each is independently testable:
  implementing just one still yields a viable, demonstrable slice.
-->

### User Story 1 - Author, Build, Promote, and See an Elsa Activity Extension Loaded (Priority: P1)

As a trusted Studio user, I want to create an Elsa activity/module project from a
template, edit its source, build it into a package while watching build logs and
diagnostics, promote the validated package into the feed that the runtime loads,
and then confirm the new activity is available at runtime — all without leaving
the Studio UI.

**Why this priority**: This is the thinnest end-to-end vertical slice that proves
the whole extension boundary: workspace → editor → build → promote → loaded at
runtime. It is the minimum that delivers user value (a brand-new capability live
in the running product) and exercises every backend capability the rest of the
feature builds on.

**Independent Test**: Starting from an empty Extension Builder, a user creates a
project from the Elsa activity template, makes a trivial code edit, triggers a
build, watches it succeed in the log/diagnostics view, promotes the produced
package, and then observes the package reported as loaded plus the contributed
activity appearing in the runtime capability list — with no other user story
implemented.

**Acceptance Scenarios**:

1. **Given** the Extension Builder is open with no projects, **When** the user
   creates a project from the "Elsa activity/module" template and names it, **Then**
   a workspace appears with a populated file tree, the template's starter source
   files, and a clear next-step affordance to build.
2. **Given** a project workspace is open, **When** the user edits a source file and
   triggers a build, **Then** the UI streams build status (queued → running →
   succeeded/failed), shows build logs, and surfaces compiler diagnostics
   (errors/warnings) tied to files and line numbers where available.
3. **Given** a build has succeeded and produced a package, **When** the user
   promotes that package, **Then** the UI shows the promotion/validation outcome and
   the resulting reconcile state (e.g., loaded, pending restart, or failed).
4. **Given** a package has been promoted and loaded, **When** the user views runtime
   status for the project, **Then** the UI lists the package as loaded, shows its
   version, and lists the features/activities the package contributed at runtime.

---

### User Story 2 - Manage the Project Workspace and Files (Priority: P2)

As a trusted Studio user, I want to browse my extension projects, create new
projects (Elsa activity/module template or generic .NET), and create, rename,
edit, and delete files within a project, so I can shape a real .NET project
structure before building.

**Why this priority**: A usable workspace is required for any non-trivial extension
beyond the starter template, but the P1 slice can demo with template defaults
alone. This story makes the builder a real authoring surface.

**Independent Test**: With only this story, a user can create both an Elsa-template
project and a generic .NET project, navigate the file tree, add/rename/delete files
and folders, and edit and save content — verified without build or promote.

**Acceptance Scenarios**:

1. **Given** the project browser is open, **When** the user creates a project, **Then**
   they can choose between the Elsa activity/module template (primary, pre-selected)
   and a generic .NET project template, and provide a project name and package
   identity (id + initial version).
2. **Given** a project is open, **When** the user creates, renames, moves, or deletes
   a file or folder, **Then** the file tree updates accordingly and the editor reflects
   the active file.
3. **Given** the user has unsaved edits in a file, **When** they switch files, leave
   the workspace, or trigger a build, **Then** the UI makes the dirty/unsaved state and
   save outcome explicit and prevents silent loss of work.
4. **Given** multiple projects exist, **When** the user opens the project browser,
   **Then** each project shows its name, package identity, latest build status, and
   runtime/loaded status at a glance.

---

### User Story 3 - Inspect Build Diagnostics and Logs in Depth (Priority: P3)

As a trusted Studio user, I want a clear diagnostics and logs experience for each
build so that when a build fails I can understand why, jump to the offending code,
and re-run the build after fixing it.

**Why this priority**: Builds will fail routinely during authoring; a strong
failure UX is what makes the builder usable day-to-day, but the happy path (P1)
can ship first.

**Independent Test**: Given a project whose source contains a compile error, a user
triggers a build, sees it fail, reads the streamed log, sees the diagnostics list
with severities and locations, navigates from a diagnostic to the relevant file/line,
fixes it, and re-runs to success — verified independently of promote/runtime.

**Acceptance Scenarios**:

1. **Given** a build is running, **When** the user watches the build view, **Then**
   the log streams incrementally and the overall status updates without a manual
   refresh.
2. **Given** a build failed with compiler errors, **When** the user opens the
   diagnostics list, **Then** each entry shows severity, message, and file/line
   location where available, and selecting it focuses the corresponding source
   location in the editor.
3. **Given** a previous build exists, **When** the user opens build history for a
   project, **Then** they can see prior build outcomes, timestamps, and logs for
   each revision they built.

---

### User Story 4 - Recover: Rollback, Retry Reconciliation, and Restart-Required (Priority: P4)

As a trusted Studio user, I want to recover when a promoted package does not load
cleanly — by retrying reconciliation, rolling back to a previously loaded version,
or being told a restart is required — so a bad promotion never leaves the runtime
stuck with no path forward.

**Why this priority**: Operational safety. It is essential for trust but depends on
promote/runtime status (P1) already existing, so it follows.

**Independent Test**: Given a project with at least one previously loaded version
and a newly promoted version that fails reconciliation, the user can retry
reconciliation, see a restart-required state when applicable, and roll back to the
prior version with a clear confirmation and post-rollback runtime status — verified
without the editor/build stories.

**Acceptance Scenarios**:

1. **Given** a promoted package failed reconciliation, **When** the user views its
   runtime status, **Then** the failure reason/diagnostics are shown and a "retry
   reconciliation" action is available.
2. **Given** a promoted package requires a restart to take effect, **When** the user
   views runtime status, **Then** the UI clearly communicates the pending-restart state
   and what the user should expect/do.
3. **Given** a project has more than one promoted version, **When** the user selects a
   previous version and chooses rollback, **Then** the UI asks for confirmation,
   performs the rollback, and then reflects the rolled-back version as the loaded one.

### Edge Cases

- A user attempts to promote a package whose id + version duplicates one already in
  the feed (must be prevented or clearly disambiguated, never silently overwriting).
- A build is triggered while a previous build for the same project is still running.
- A build succeeds but produces no loadable contribution (empty/invalid package).
- Promotion validation rejects a package that built successfully (e.g., manifest,
  signing, or compatibility checks fail server-side).
- Reconciliation fails after a successful promotion; the runtime keeps the prior
  version while surfacing the failure.
- A package loads but contributes nothing visible (no activities/features), or
  contributes items that conflict with an already-loaded package.
- Rollback target version is no longer present in the feed (e.g., pruned by retention).
- Network/stream interruption during a long build or while streaming logs.
- Concurrent edits to the same project from two sessions by trusted users.
- Very large file trees, very large individual files, or long-running builds.
- The backend build/promote capability is unavailable or returns an unexpected
  contract shape (the UI must degrade gracefully and explain the limitation).

## Requirements *(mandatory)*

### Functional Requirements

#### Project workspace & files

- **FR-001**: The Extension Builder MUST present a project browser that lists all
  extension projects with, at minimum, project name, package identity (id +
  version), latest build status, and runtime/loaded status.
- **FR-002**: Users MUST be able to create a new project from a template, with the
  Elsa activity/module template offered as the primary, pre-selected option and a
  generic .NET project template also available.
- **FR-003**: At project creation, users MUST provide a project name and a package
  identity (package id and initial version), and the UI MUST validate these for
  presence and basic format before allowing build/promote.
- **FR-004**: Users MUST be able to view a project's file tree and create, open,
  rename, move, and delete files and folders within a project.
- **FR-005**: The UI MUST make unsaved (dirty) editor state explicit and MUST prevent
  silent loss of edits when switching files, leaving the workspace, or starting a build.

#### Code editor

- **FR-006**: The UI MUST provide a code editor for C#/.NET source with standard
  editing affordances (syntax highlighting, multi-file editing, save).
- **FR-007**: The editor MUST surface build diagnostics inline against the relevant
  file and line where the backend provides location information.
- **FR-008**: Users MUST be able to navigate from a diagnostic entry to the
  corresponding source location in the editor.

#### Build

- **FR-009**: Users MUST be able to trigger a build of the current project revision.
- **FR-010**: The UI MUST show build lifecycle status (e.g., queued, running,
  succeeded, failed) and update it without requiring a manual page refresh.
- **FR-011**: The UI MUST stream/show build logs and present a structured list of
  build diagnostics (errors and warnings) with severity, message, and location when
  available.
- **FR-012**: The UI MUST expose per-project build history, including prior outcomes,
  timestamps, and the logs/diagnostics for each build.
- **FR-013**: The UI MUST clearly handle the case where a build is requested while a
  build for the same project is already in progress (e.g., disable re-trigger or
  queue), and never present ambiguous concurrent-build state.

#### Promote

- **FR-014**: Users MUST be able to promote a successfully built package, triggering
  server-side validation and placement into the runtime-loadable feed.
- **FR-015**: The UI MUST display the promotion outcome, including validation
  pass/fail with reasons, and the resulting reconcile state.
- **FR-016**: The UI MUST prevent (or clearly disambiguate) promoting a package whose
  id + version duplicates an existing feed entry, and MUST never silently overwrite.
- **FR-017**: The UI MUST only enable promote when a valid, successfully built package
  exists for the project's current revision; otherwise the action is unavailable with
  an explanation.

#### Runtime status, rollback, recovery

- **FR-018**: The UI MUST show per-project runtime status for promoted packages,
  including at least: loaded, pending restart, and failed reconciliation states, plus
  the loaded version.
- **FR-019**: The UI MUST list the features/activities (capabilities) a loaded package
  contributed at runtime, or clearly indicate when it contributed none.
- **FR-020**: Users MUST be able to retry reconciliation for a package whose
  reconciliation failed.
- **FR-021**: Users MUST be able to roll back to a previously promoted/loaded version,
  with explicit confirmation, and the UI MUST reflect the resulting loaded version
  after rollback.
- **FR-022**: When a promoted package requires a restart to take effect, the UI MUST
  communicate the pending-restart state and expected user action.
- **FR-023**: When a rollback target is unavailable (e.g., pruned by feed retention),
  the UI MUST explain this and prevent the rollback rather than failing opaquely.

#### Cross-cutting UX, consistency, trust, and resilience

- **FR-024**: All Extension Builder screens MUST be composed from the shared Elsa
  Studio UI design language (workbench data grid/list, split inspector, toolbar,
  status chips, alerts, diagnostics/console panels, empty/loading/error states)
  rather than inventing local visual patterns. <!-- aligns with constitution I–III -->
- **FR-025**: All interactive primitives MUST be keyboard operable, expose visible
  focus and accessible labeling, and present disabled/loading/validation states.
- **FR-026**: The Extension Builder MUST be available only to trusted users
  (teams/admins) in v1; it MUST be hidden or access-denied for users who lack the
  trusted capability, and MUST present a clear unauthorized state rather than a broken
  screen. <!-- v1 trust model; multi-tenant isolation UI deferred -->
- **FR-027**: Every long-running or failure-prone action (build, promote, reconcile,
  rollback) MUST surface progress, success, and failure states with actionable
  messaging, and MUST degrade gracefully if the backend capability is unavailable or
  returns an unexpected response.
- **FR-028**: The UI MUST reference backend capabilities through a single consumed
  capability surface so that final canonical endpoint/entity names from the
  `elsa-foundation` backend spec can be aligned without reworking the UI's user-facing
  behavior. <!-- backend contract is authoritative elsewhere; align names later -->

### Key Entities *(include if feature involves data)*

- **Extension Project**: A user-owned .NET project workspace in the builder. Key
  attributes: name, template kind (Elsa activity/module | generic .NET), package
  identity (id + version), current revision, latest build status, runtime/loaded
  status. Has many files and many builds.
- **Project File / Folder**: A node in a project's file tree with a path, type
  (file/folder), and (for files) editable content and dirty state.
- **Project Template**: A starting point for a new project. v1 offers the Elsa
  activity/module template (primary) and a generic .NET template; each seeds an
  initial file tree and package identity defaults.
- **Build**: An attempt to compile a project revision into a package. Attributes:
  status (queued/running/succeeded/failed), timestamps, streamed log, diagnostics
  list, and (on success) a produced package reference.
- **Build Diagnostic**: A single compiler/build message with severity (error/warning/
  info), message text, and optional file + line/column location.
- **Package (Built Artifact)**: The NuGet package produced by a successful build,
  identified by package id + version, eligible for promotion.
- **Promotion**: The act/record of validating and placing a built package into the
  runtime-loadable feed. Attributes: validation outcome (pass/fail + reasons) and
  resulting reconcile state.
- **Feed Entry / Promoted Version**: A package version present in the runtime feed,
  with version, promotion timestamp, and load state; the set of these per project
  forms the rollback history.
- **Runtime Capability Contribution**: A feature/activity a loaded package contributed
  at runtime, shown so the user can confirm what their extension added.
- **Reconcile / Runtime Status**: The current runtime state of a project's loaded
  package: loaded, pending restart, or failed reconciliation, plus failure
  reason/diagnostics when failed.

### Studio Views *(UI surfaces this feature introduces)*

- **Extension Builder Home / Project Browser**: Workbench resource list of projects
  with create-from-template entry point and at-a-glance build/runtime status.
- **Project Workspace (Editor view)**: File-tree + code editor split, with a build
  action and an inline diagnostics affordance; the primary authoring surface.
- **Build Panel (Logs & Diagnostics)**: Streaming build status, log output, and a
  structured diagnostics list with navigation to source; includes build history.
- **Promote & Runtime panel (Inspector)**: Promotion action and outcome, runtime/
  reconcile status, contributed capabilities, version history, and recovery actions
  (retry reconcile, rollback, restart-required messaging).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A trusted user can go from "no project" to "extension activity loaded
  and visible at runtime" entirely within the Studio UI, following the P1 path with no
  external tooling, in under 10 minutes for the starter template.
- **SC-002**: For a build that fails on a compile error, at least 90% of test users can
  locate the offending file/line from the diagnostics view and reach the relevant
  editor location without consulting external documentation.
- **SC-003**: Build status and logs update live; users perceive build progress without
  manually refreshing, and the final outcome (success/failure) is unambiguous in 100%
  of observed builds.
- **SC-004**: 100% of promote attempts that would duplicate an existing package id +
  version are blocked or explicitly disambiguated by the UI before submission; none
  silently overwrite.
- **SC-005**: For a failed reconciliation, a user can reach a working recovery action
  (retry reconcile or rollback to a prior loaded version) within the runtime status
  view in under 2 minutes, and the runtime status reflects the recovery outcome.
- **SC-006**: Every Extension Builder screen uses the shared Studio workbench language
  (data grid/list, split inspector, status chips, diagnostics/console panel) with no
  bespoke page-level shell, typography, or status styling.
- **SC-007**: All primary actions (create project, edit/save, build, promote, retry,
  rollback) are fully operable by keyboard with visible focus and accessible labels.
- **SC-008**: Non-trusted users never reach an interactive builder surface; they always
  see a clear unauthorized/hidden state, in 100% of access checks.

## Assumptions

- **Trusted-team v1 only**: The builder targets trusted teams/admins. No multi-tenant
  isolation UI is in scope for v1; tenant isolation is referenced as a later unit.
- **Generic .NET from day one, Elsa template primary**: Project creation supports
  generic .NET projects, with an Elsa activity/module package template as the primary,
  first-class, pre-selected template.
- **AI/agentic authoring deferred**: v1 is a manual code editor + build + promote
  experience. The design should not preclude a future AI authoring layer, but no AI UI
  is in v1.
- **Backend pipeline is authoritative elsewhere**: The server-side build, package
  validation, promotion, feed loading (Nuplane), and runtime reconciliation (CShells)
  — and their API/entity contracts — are specified authoritatively in the separate
  `elsa-foundation` repository. This UI consumes that capability surface; capability
  names here are generic placeholders to be aligned with the backend's final canonical
  names (see FR-028).
- **Consumes existing Studio surfaces**: This repo already provides a module/route
  contribution system, shared workbench UI primitives (data grid, inspector, tabs,
  toolbar, status chips, feedback states), and module-management/feed + reconcile
  surfaces (`/_elsa/module-management/feeds`, feed-changes-require-restart, reconcile).
  The Extension Builder is a new Studio module/screen that reuses these rather than
  introducing parallel concepts.
- **Editor technology**: A Monaco-style C#/.NET editing experience is assumed for the
  code editor; exact editor integration is an implementation/plan concern, not a spec
  decision.
- **One package per project (v1 assumption)**: Each extension project maps to a single
  package identity (id + version line) for v1; multi-package projects are out of scope.

### Backend-contract alignment items (to confirm with coordinator)

- [NEEDS CLARIFICATION: Canonical backend capability/endpoint and entity names for
  build, package validation, promotion, feed entries, runtime/reconcile status, and
  rollback — to map UI views in FR-028 to the authoritative `elsa-foundation` spec.]
- [NEEDS CLARIFICATION: How "trusted user" is determined and surfaced to the Studio
  frontend in v1 (capability flag, role, or backend-provided permission) so FR-026 and
  SC-008 can bind to a concrete authorization signal.]
- [NEEDS CLARIFICATION: Where project workspace source is persisted and whether it is
  per-user or shared across trusted teammates, which drives the concurrent-edit
  behavior referenced in the edge cases.]
