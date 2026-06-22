# Feature Specification: Extension Builder — Backend Pipeline (Trusted-Team v1)

**Feature Branch**: `075-extension-builder-backend`

**Created**: 2026-06-22

**Status**: Draft

**Input**: Coordinator handoff: "Extension Builder — backend pipeline (trusted-team v1). Lets a trusted user create and edit a .NET project workspace, build it server-side into a NuGet package, promote the validated package into a Nuplane-loadable feed, and have CShells expose the resulting capability at runtime — with status, diagnostics/logs, and rollback throughout. Backend pipeline only; the UI is a separate spec in elsa-foundation-studio."

**Program goal**: [Feature Composition Readiness](../../docs/program-goals/feature-composition-readiness.md) — this feature is the authoring/build/promote on-ramp that produces the Nuplane-loadable packages that feature composition and CShells later expose.

**Authority note**: This spec is **authoritative for the capability/contract surface** (endpoint paths, operation names, and key entity names). The Studio UI spec in `elsa-foundation-studio` consumes the names defined here. See [Capability & Contract Surface](#capability--contract-surface-authoritative) for the canonical list.

---

## User Scenarios & Testing *(mandatory)*

The actor in all stories below is a **trusted team member or admin** (see Assumptions for the trust model). "The system" is the Extension Builder backend running in-process with a Nuplane-enabled Elsa host.

### User Story 1 - Author, build, promote, and load an Elsa activity end-to-end (Priority: P1)

A trusted developer creates a new extension project from the Elsa activity/module template, builds it into a NuGet package on the server, promotes the validated package into the Nuplane-loadable feed, and confirms the contributed activity becomes available at runtime — all without recompiling or redeploying the host application.

**Why this priority**: This is the thinnest slice that proves the entire pipeline (create → build → promote → load → available). If only this story ships, the product already delivers its core promise: extend a running Elsa app with a new activity through a server-side authoring loop. Every other story builds on the artifacts and statuses this story produces.

**Independent Test**: Fully testable by creating an Elsa-activity project from the template, submitting a build that succeeds, promoting the resulting package, and then asserting that the new activity appears in the runtime feature/activity catalog and can be referenced by a workflow. Requires no UI.

**Acceptance Scenarios**:

1. **Given** a trusted caller with no existing workspace, **When** they create a workspace and then a project from the Elsa activity/module template, **Then** the system returns a project with a default package identity (id + version), target framework, manifest metadata, and a compilable starter activity source file.
2. **Given** an unmodified template project, **When** the caller submits a build, **Then** the build completes with status `Succeeded`, exposes a `.nupkg` artifact reference, and surfaces zero error-level diagnostics.
3. **Given** a successful build artifact, **When** the caller promotes it, **Then** the system validates the package (id, version, manifest, dependencies), publishes it to the Nuplane-loadable feed, triggers reconciliation, and returns a reconcile outcome indicating the package was accepted.
4. **Given** a promoted package that reconciled successfully, **When** the caller queries runtime status for the project, **Then** the status reports the package as loaded and lists the contributed feature(s)/activity(ies) now available in the runtime catalog.
5. **Given** the package is loaded, **When** a workflow author lists available activities at runtime, **Then** the newly contributed activity is present and usable.

---

### User Story 2 - Edit project files and iterate with build diagnostics and logs (Priority: P2)

A developer edits the source files of an existing project (adds/changes/deletes files), rebuilds, and — when the build fails on invalid C# — reads structured diagnostics and the build log to find and fix the problem, then rebuilds successfully.

**Why this priority**: The authoring loop is only useful if the developer can iterate. Diagnostics and logs are what make a failing build actionable. This is the second-most-valuable slice because it turns a one-shot template build into a real edit/build/fix cycle.

**Independent Test**: Testable by listing project files, editing a file to introduce a compile error, submitting a build, asserting status `Failed` with at least one error diagnostic carrying file/line/message and an accessible build log, then correcting the file and asserting a subsequent `Succeeded` build.

**Acceptance Scenarios**:

1. **Given** an existing project, **When** the caller lists files, **Then** the system returns the project's file tree with paths and metadata.
2. **Given** an existing project, **When** the caller creates, updates, or deletes a file, **Then** the change is persisted and reflected in the next file listing and the next build.
3. **Given** a project edited to contain invalid C#, **When** the caller submits a build, **Then** the build status is `Failed`, the response includes diagnostics with severity, message, and source location where available, and the full build log is retrievable.
4. **Given** a previously failing project that has been corrected, **When** the caller submits a new build, **Then** the build status is `Succeeded` and a fresh artifact is produced without affecting prior build records.

---

### User Story 3 - Promotion validation rejects invalid or conflicting packages (Priority: P3)

When a developer attempts to promote a package that is invalid (malformed/missing manifest), conflicts with an already-published package id+version, or declares disallowed dependencies, the system rejects the promotion with a clear, actionable reason and does **not** publish the package or disturb the running app.

**Why this priority**: Promotion is the gate between "built on a workspace" and "loaded into the live app." Without validation, a bad package can break reconciliation or collide with existing modules. This protects the running host, so it ranks above runtime management but below the core happy path and the iterate loop.

**Independent Test**: Testable by attempting promotions of (a) a package whose id+version already exists in the feed, (b) a package with a missing/invalid manifest, and (c) a package declaring a dependency outside policy, and asserting each returns a distinct rejection reason with no change to the published feed or runtime catalog.

**Acceptance Scenarios**:

1. **Given** a package whose id+version already exists in the Nuplane-loadable feed, **When** the caller promotes it, **Then** the system rejects the promotion as a duplicate id+version and the existing published package is unchanged.
2. **Given** a `.nupkg` with a missing or malformed package manifest, **When** the caller promotes it, **Then** the system rejects the promotion with a manifest-validation reason and publishes nothing.
3. **Given** a package that declares a dependency outside the configured promotion policy, **When** the caller promotes it, **Then** the system rejects the promotion with a dependency-policy reason and publishes nothing.
4. **Given** a valid, non-conflicting package, **When** promotion succeeds but reconciliation subsequently fails, **Then** the promotion result reports the failed reconcile outcome with a reason rather than silently reporting success.

---

### User Story 4 - Observe runtime status, roll back, and retry reconciliation (Priority: P4)

After one or more promotions, a developer inspects the runtime status of a project's packages (loaded / pending restart / failed reconciliation), rolls back to a previous package version when a new version misbehaves, and retries reconciliation when a transient failure occurred.

**Why this priority**: Operability of loaded extensions. Status, rollback, and retry are essential for trust in production but only become relevant once packages are being promoted and loaded, so they follow the build/promote/validate stories.

**Independent Test**: Testable by promoting version N, then version N+1, querying runtime status to confirm N+1 is active, rolling back to N, and asserting status reports N active again; and by forcing a reconcile failure and asserting a retry transitions status appropriately.

**Acceptance Scenarios**:

1. **Given** a project with at least one promoted package, **When** the caller queries runtime status, **Then** the system reports per-package state (`Loaded`, `PendingRestart`, or `FailedReconciliation`) and the active features/activities contributed.
2. **Given** a project with package versions N and N+1 where N+1 is active, **When** the caller requests rollback to version N, **Then** the system republishes/activates version N, triggers reconciliation, and runtime status reports N as the active version.
3. **Given** a package whose last reconciliation failed, **When** the caller requests a reconciliation retry, **Then** the system re-runs reconciliation and updates runtime status to reflect the new outcome.
4. **Given** a promoted package that requires a host restart to take effect, **When** the caller queries runtime status, **Then** the status reports `PendingRestart` and indicates that a restart is required to complete loading.

---

### User Story 5 - Create and build a generic (non-Elsa) .NET project (Priority: P5)

A developer creates a project from a generic .NET template (e.g., a plain class library) rather than the Elsa activity/module template, edits and builds it into a package, and promotes it through the same pipeline.

**Why this priority**: The locked scope requires generic .NET project support from day one, but the Elsa activity template is the first-class primary path. Generic projects exercise the same workspace/build/promote machinery without Elsa-specific manifest contributions, so they validate that the pipeline is not hard-wired to Elsa. Lowest priority because the primary product value is delivered by the Elsa template path.

**Independent Test**: Testable by creating a project from the generic .NET template, building it successfully, and promoting it, asserting the pipeline produces a valid package even though no Elsa activity/feature manifest is contributed.

**Acceptance Scenarios**:

1. **Given** the generic .NET template, **When** the caller creates a project, **Then** the system produces a buildable project with package identity and target framework but without requiring Elsa activity/feature manifest content.
2. **Given** a generic project that builds successfully, **When** the caller promotes it, **Then** validation applies the same id/version/dependency rules and publishes the package, while runtime status reflects that it contributes no Elsa features/activities (if none are present).

---

### Edge Cases

- **Invalid C# / compile failure**: build returns `Failed` with diagnostics and a retrievable log; no artifact is published, and no promotion is possible from a failed build.
- **Duplicate package id+version on promotion**: rejected before publish; existing package untouched.
- **Invalid or missing package manifest on promotion**: rejected with a manifest-validation reason.
- **Disallowed dependency on promotion**: rejected with a dependency-policy reason (note: v1 trusted model does not enforce a hostile-code allowlist; see Assumptions — this is a policy/validation check, not a security sandbox).
- **Reconcile failure after a valid publish**: surfaced as a failed reconcile outcome with a reason; runtime status shows `FailedReconciliation`; retry is available.
- **Restart-required loads**: surfaced as `PendingRestart`; caller is told a restart is required to complete loading.
- **Rollback target missing**: rollback to a version that is no longer present in the feed is rejected with a clear reason.
- **Concurrent builds for the same project**: the system must define deterministic behavior (queue or reject) so two in-flight builds cannot corrupt each other's artifacts. [NEEDS CLARIFICATION: Should concurrent builds for the same project be queued, rejected, or run in isolation? Default assumption: serialize per project — only one active build per project at a time, later requests queue.]
- **Empty or non-existent project/workspace/build id**: operations against unknown ids return a not-found result, not a server error.
- **Deleting a workspace/project with loaded packages**: deleting authoring artifacts must not silently unload live packages; the relationship between deleting a project and unloading its promoted packages must be explicit. [NEEDS CLARIFICATION: Does deleting a project also unload/unpublish its promoted packages, or only remove authoring state? Default assumption: deleting a project removes authoring/workspace state only and leaves already-promoted packages in the feed/runtime until explicitly pruned or rolled back.]
- **Build dependency restore**: a project may reference external NuGet dependencies that must be restored at build time; behavior when restore fails (offline, unknown package) must produce a `Failed` build with a restore diagnostic rather than hanging.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Workspace & Project authoring

- **FR-001**: The system MUST allow a trusted caller to create an extension **workspace** and to list, retrieve, and delete workspaces they own.
- **FR-002**: The system MUST allow creating an extension **project** within a workspace from a named **template**, where the Elsa activity/module template is the primary first-class template and at least one generic .NET template is also available.
- **FR-003**: Each project MUST carry editable identity/metadata: package id, package version, target framework, and manifest metadata (display name, description, categories, and feature/activity manifest content for Elsa templates).
- **FR-004**: The system MUST allow a caller to list the files in a project, and to create, read, update, and delete individual project files, with changes persisted for use in subsequent builds.
- **FR-005**: The system MUST reject file or project operations that reference unknown workspaces, projects, or files with a not-found result distinguishable from validation or server errors.
- **FR-006**: The system MUST associate each build with the **source revision** of the project it was built from, so that build results, artifacts, and promotions are traceable to a specific project state. [NEEDS CLARIFICATION: Is an explicit immutable source revision/snapshot required per build (full version history), or is "the project's current files at build submission" sufficient for v1? Default assumption: each build captures a snapshot/revision identifier of the project files at submission; full editable version history is not required in v1.]

#### Build

- **FR-007**: The system MUST allow a caller to submit a **build** of a project's current source revision and MUST return a build identity that can be polled.
- **FR-008**: A build result MUST expose a **status** of at least `Pending`/`Running`, `Succeeded`, and `Failed`.
- **FR-009**: A build result MUST expose **diagnostics** including severity (error/warning), a human-readable message, and source location (file/line/column) where available.
- **FR-010**: A successful build MUST produce a retrievable **`.nupkg` artifact** reference suitable for promotion.
- **FR-011**: The system MUST make the full **build log** retrievable for any build regardless of outcome.
- **FR-012**: A failed build MUST NOT produce a promotable artifact, and the system MUST prevent promotion from a build that did not succeed.
- **FR-013**: The system MUST restore the project's declared external dependencies as part of building, and MUST surface restore failures as build diagnostics with a `Failed` status rather than blocking indefinitely.

#### Promote

- **FR-014**: The system MUST allow a caller to **promote** a successful build's `.nupkg` into the Nuplane-loadable feed.
- **FR-015**: Promotion MUST **validate** the package before publishing, checking at minimum: package id and version well-formedness, manifest presence/validity, and dependency conformance to the configured promotion policy.
- **FR-016**: Promotion MUST reject a package whose id+version already exists in the target feed (duplicate), without altering the existing published package.
- **FR-017**: When validation passes, promotion MUST publish the package to the Nuplane-loadable feed and trigger reconciliation, returning a **reconcile outcome** (accepted / deferred / failed) with a reason where applicable.
- **FR-018**: When validation fails, promotion MUST return a rejection with a distinct, machine-classifiable reason per failure category (duplicate, invalid-manifest, dependency-policy, malformed-package) and MUST publish nothing.
- **FR-019**: Promotion MUST report a subsequent reconciliation failure as a failed outcome with a reason rather than reporting success.

#### Runtime status, rollback, retry

- **FR-020**: The system MUST report **runtime status** for a project's promoted packages, including per-package state (`Loaded`, `PendingRestart`, `FailedReconciliation`) and the active features/activities each contributes to the runtime catalog.
- **FR-021**: The system MUST allow **rollback** of a project to a previously promoted package version, re-activating that version and triggering reconciliation.
- **FR-022**: The system MUST allow a caller to **retry reconciliation** for a package whose last reconciliation failed, and MUST update runtime status to reflect the new outcome.
- **FR-023**: The system MUST indicate when a promoted package requires a host **restart** to complete loading (`PendingRestart`) and MUST not report such a package as fully `Loaded` until loading completes.
- **FR-024**: The system MUST reject rollback to a version that is no longer available in the feed with a clear reason.

#### Cross-cutting

- **FR-025**: All pipeline operations MUST be restricted to trusted callers using the host's existing module-management authorization mechanism; the Extension Builder MUST NOT introduce a weaker access path to package publishing than the existing module-management surface.
- **FR-026**: The Extension Builder backend MUST reuse and extend the existing module-management/Nuplane surface (registry, package upload/publish, feeds, reconcile, retention/prune) rather than introducing a parallel, divergent package-management mechanism.
- **FR-027**: Pipeline operations that change running state (promote, rollback, retry) MUST report whether the change requires reload and/or restart so callers can inform the user.
- **FR-028**: The system MUST NOT implement hostile-code sandboxing, per-tenant build/runtime isolation, package signing, resource quotas, dependency allowlist *enforcement* as a security boundary, or build/runtime audit logging in v1; these are explicitly deferred (see Assumptions and Out of Scope).

### Key Entities *(include if feature involves data)*

- **ExtensionWorkspace**: A trusted-caller-owned container for one or more projects. Attributes: identity, owner/trust context, display name, created/updated timestamps, contained projects.
- **ExtensionProject**: A single .NET project within a workspace. Attributes: identity, template kind (Elsa-activity/module | generic .NET), package id, package version, target framework, manifest metadata, file set, current source revision reference.
- **ProjectFile**: A file within a project. Attributes: relative path, content, kind, last-modified metadata.
- **ExtensionTemplate**: A named project starting point. Attributes: id, kind (Elsa-activity/module primary; generic .NET), description, default file/manifest content.
- **BuildRequest**: A request to build a project's source revision. Attributes: project reference, source revision reference, submission timestamp.
- **BuildResult**: The outcome of a build. Attributes: build identity, project + source revision reference, status (`Pending`/`Running`/`Succeeded`/`Failed`), diagnostics, artifact reference (on success), log reference, timestamps.
- **BuildDiagnostic**: A single build message. Attributes: severity, message, source location (file/line/column, where available), code.
- **BuildArtifact**: The `.nupkg` produced by a successful build. Attributes: package id, version, artifact reference, size, build reference.
- **PackagePromotionRequest**: A request to promote a build artifact into the feed. Attributes: build/artifact reference, target feed reference.
- **PackagePromotionResult**: The outcome of a promotion. Attributes: accepted/rejected status, rejection reason category (duplicate | invalid-manifest | dependency-policy | malformed-package) where applicable, published package reference, reconcile outcome (accepted/deferred/failed + reason), requires-reload/requires-restart flags.
- **ExtensionRuntimeStatus**: The live state of a project's promoted packages. Attributes: per-package state (`Loaded` | `PendingRestart` | `FailedReconciliation`), active version, contributed features/activities, available rollback versions, last reconcile outcome/reason.

---

## Capability & Contract Surface (authoritative)

> This section is the canonical name list the Studio UI spec consumes. Names are stable contract names; transport/shape details (verbs, payloads) are confirmed during planning, but the **operation names, entity names, and path stems below are owned by this spec**.

**Endpoint root**: `/_elsa/extension-builder` (extends, and is co-located with, the existing `/_elsa/module-management` surface; promotion/runtime operations delegate to the existing module-management/Nuplane mechanisms).

**Workspace & project operations**

- `CreateWorkspace` — `POST /_elsa/extension-builder/workspaces`
- `ListWorkspaces` — `GET /_elsa/extension-builder/workspaces`
- `GetWorkspace` — `GET /_elsa/extension-builder/workspaces/{workspaceId}`
- `DeleteWorkspace` — `DELETE /_elsa/extension-builder/workspaces/{workspaceId}`
- `ListTemplates` — `GET /_elsa/extension-builder/templates`
- `CreateProject` — `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects`
- `GetProject` — `GET /_elsa/extension-builder/projects/{projectId}`
- `DeleteProject` — `DELETE /_elsa/extension-builder/projects/{projectId}`
- `ListProjectFiles` — `GET /_elsa/extension-builder/projects/{projectId}/files`
- `ReadProjectFile` / `WriteProjectFile` / `DeleteProjectFile` — `GET|PUT|DELETE /_elsa/extension-builder/projects/{projectId}/files/{*path}`

**Build operations**

- `SubmitBuild` — `POST /_elsa/extension-builder/projects/{projectId}/builds`
- `GetBuild` — `GET /_elsa/extension-builder/builds/{buildId}`
- `GetBuildLog` — `GET /_elsa/extension-builder/builds/{buildId}/log`
- `GetBuildArtifact` — `GET /_elsa/extension-builder/builds/{buildId}/artifact`

**Promote operations**

- `PromoteBuild` — `POST /_elsa/extension-builder/builds/{buildId}/promote` (validates + publishes to the Nuplane-loadable feed + reconciles)

**Runtime status / lifecycle operations**

- `GetRuntimeStatus` — `GET /_elsa/extension-builder/projects/{projectId}/runtime-status`
- `RollbackPackage` — `POST /_elsa/extension-builder/projects/{projectId}/rollback`
- `RetryReconciliation` — `POST /_elsa/extension-builder/projects/{projectId}/retry-reconcile` (delegates to existing module-management reconcile)

**Canonical entity names** (for the Studio spec to reuse): `ExtensionWorkspace`, `ExtensionProject`, `ProjectFile`, `ExtensionTemplate`, `BuildRequest`, `BuildResult`, `BuildDiagnostic`, `BuildArtifact`, `PackagePromotionRequest`, `PackagePromotionResult`, `ExtensionRuntimeStatus`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A trusted developer can take a brand-new Elsa activity project from "created from template" to "activity available at runtime" through create → build → promote → load with no host recompilation and no host redeploy.
- **SC-002**: For the unmodified Elsa activity template, a build succeeds and the contributed activity is observable in the runtime catalog in 100% of attempts under nominal conditions.
- **SC-003**: A build that contains a compile error reports `Failed` with at least one error diagnostic that identifies the offending file and (where the compiler provides it) line, in 100% of such cases, and never produces a promotable artifact.
- **SC-004**: Every promotion of a package whose id+version already exists in the feed is rejected as a duplicate, with the previously published package left unchanged, in 100% of attempts.
- **SC-005**: Every promotion that fails validation returns exactly one machine-classifiable rejection category and publishes nothing.
- **SC-006**: After promoting version N then N+1, a rollback to N results in runtime status reporting N as the active version, and a subsequent reconciliation retry after a forced failure transitions status to reflect the new outcome.
- **SC-007**: Runtime status for any promoted package always resolves to exactly one of `Loaded`, `PendingRestart`, or `FailedReconciliation`, with contributed features/activities listed for loaded packages.
- **SC-008**: A generic (non-Elsa) .NET project can be created, built, and promoted through the identical pipeline, demonstrating the pipeline is not hard-wired to Elsa-specific content.
- **SC-009**: The backend exposes the full pipeline without any UI dependency — every story is verifiable through the contract surface alone.

---

## Assumptions

- **Trust model**: Only trusted teams/admins use the Extension Builder. Same-process runtime package loading is acceptable in v1. No hostile-code sandboxing is implemented. Public-SaaS isolation (per-tenant build/runtime workers, package signing, resource quotas, dependency allowlist *enforcement*, audit) is **out of scope** for v1 and is a required **pre-public-SaaS follow-up unit** (to be authored separately, not here).
- **Generic .NET from day one**: The workspace/project model supports arbitrary .NET projects, with the Elsa activity/module package as the primary first-class template.
- **AI/agentic authoring deferred**: v1 is manual edit + build + load. The design must not preclude a later AI authoring layer, but ships no AI in v1.
- **Builds run server-side**: Building uses the host's .NET build toolchain to compile the project source into a `.nupkg` on the server. (Exact in-process vs out-of-process build execution is an implementation/planning decision; v1 trusted model permits in-process server-side builds.)
- **Reuse existing surface**: Promotion, reconciliation, feeds, and runtime catalog integration extend the existing `/_elsa/module-management` endpoints and the Nuplane runtime package-loading wiring (`NuplaneAssemblyProvider`, `FeatureManagementService`, `RuntimeFeatureCatalog*`, `PackageManifestFeatureCatalogContributor`, `PackageFeatureManifest`, `ModularityApiFeature`, `ShellReloader`, `shells.json`). The proven sample path (`samples/Elsa.Samples.Nuplane.Activities`) and its tests are the reference for a package-contributed activity.
- **Authorization reuse**: Access control reuses the existing module-management authorization mechanism (the management API key / management authorization filter); the Extension Builder does not introduce a weaker publishing path.
- **Dependency policy in v1 is validation, not security**: The promotion dependency check is a correctness/conformance gate, not a security boundary; enforced allowlisting as a security control is deferred to the pre-public-SaaS unit.
- **Restart semantics**: Some package loads may require a host restart (mirroring the existing `RequiresRestart`/`FeedChangesRequireRestart` signals); the pipeline surfaces this rather than guaranteeing hot-load for every change.
- **Single-host scope**: v1 targets a single Nuplane-enabled Elsa host (matching `src/Apps/Elsa.Server`); multi-host/cluster promotion fan-out is out of scope.

## Out of Scope (v1)

- The Extension Builder **UI** (separate spec in `elsa-foundation-studio`, which consumes the contract names defined here).
- AI/agentic authoring assistance.
- Hostile-code sandboxing, per-tenant build/runtime isolation, package signing, resource quotas, enforced dependency allowlisting as a security control, and build/runtime audit logging — all deferred to a **pre-public-SaaS hardening unit**.
- Multi-host/cluster promotion fan-out.
- Full editable source version history / branching of project files beyond the per-build source revision needed for traceability.

## Dependencies

- Existing module-management API surface: `src/Apps/Elsa.Server/ElsaModuleManagementApi.cs` (`/_elsa/module-management/*`).
- Nuplane runtime package loading: `src/Apps/Elsa.Server/NuplaneAssemblyProvider.cs`, `DemoNuplaneObserver.cs`, `DemoPackageEventStore.cs`, `shells.json`.
- Nuplane modularity services: `src/Elsa/Modularity/Nuplane/` (`FeatureManagementService`, `RuntimeFeatureCatalog*`, `PackageManifestFeatureCatalogContributor`, `PackageFeatureManifest`).
- Modularity API: `src/Elsa/Modularity/Api/` (`ModularityApiFeature`, `ShellReloader`, `JsonShellFeatureConfigurationStore`).
- Reference implementation: `samples/Elsa.Samples.Nuplane.Activities/` and its tests under `tests/Elsa/Samples/Nuplane/Activities/Tests/`.
- Constitution gates: `.specify/memory/constitution.md` and `.specify/memory/constitution-framework.md`.

## Open Clarifications

1. **Concurrent builds per project** (FR/Edge): queue, reject, or isolate? Default assumed: serialize per project.
2. **Project deletion vs promoted packages** (Edge): does deleting a project unpublish/unload its packages, or only remove authoring state? Default assumed: authoring state only.
3. **Source revision model** (FR-006): explicit immutable snapshot per build vs current-files-at-submission. Default assumed: snapshot identifier per build, no full editable history in v1.
