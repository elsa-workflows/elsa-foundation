# Research: Extension Builder — Backend Pipeline

## Decision: Co-locate Extension Builder with `Elsa.Server` module-management APIs

**Rationale**: The feature is explicitly single-host, trusted-team v1, and depends on host content-root storage, ASP.NET endpoint authorization, the server `dotnet` SDK, and Nuplane admin services already wired in `Elsa.Server`. Keeping orchestration in the app host avoids forcing ASP.NET/build-tooling dependencies into Elsa `.Core` packages and preserves the constitution dependency envelope.

**Alternatives considered**: A new `Elsa.Modularity.ExtensionBuilder.Core` package was rejected for v1 because the build/promotion pipeline is host-operational rather than a reusable domain contract yet. A parallel package-management implementation was rejected because the spec requires reuse of module-management/Nuplane.

## Decision: Resolve open clarifications with the coordinator defaults

**Rationale**: The handoff supplies explicit defaults for all open questions: serialize builds per project, delete authoring state only, capture an immutable source snapshot id per build, enforce authenticated admin/trusted access server-side, expose advisory capability flags, persist owner-scoped workspaces, and use last-write-wins file edits.

**Alternatives considered**: Rejecting concurrent builds, deleting promoted packages with project deletion, maintaining full editable source history, or relying solely on frontend authorization were all rejected because they conflict with the approved defaults.

## Decision: Use server-local file storage for v1 authoring state and artifacts

**Rationale**: The repo already uses local `appsettings.json`, package directories, sample Nuplane packages, and single-host assumptions. File storage provides process-restart persistence without introducing a database schema or EF provider dependency for a trusted-team backend slice.

**Alternatives considered**: EF Core persistence was rejected for this v1 because the Extension Builder state is host-operational and no existing domain persistence boundary owns it. In-memory storage was rejected because FR-031 requires restart survival.

## Decision: Build through an out-of-process `dotnet` CLI runner

**Rationale**: Server-side .NET project builds naturally require restore/build/pack, diagnostics, logs, and isolation from the host process state. A CLI runner can run against an immutable source snapshot directory, capture stdout/stderr logs, parse MSBuild diagnostics, and serialize per project.

**Alternatives considered**: In-process MSBuild APIs were rejected as a heavier dependency and higher coupling choice for the app host. A remote build worker is out of scope for trusted-team v1 and public-SaaS hardening.

## Decision: Promote by validating the built `.nupkg`, copying it into the configured Nuplane directory feed, and triggering reconciliation

**Rationale**: Existing module-management upload already resolves the configured drop-folder feed and triggers `INuplaneAdminOperations.TriggerReconcileAsync`. Extension Builder should reuse the same feed/reconcile path while adding build-aware validation categories: `duplicate`, `invalid-manifest`, `dependency-policy`, and `malformed-package`.

**Alternatives considered**: A separate Extension Builder feed was rejected as a divergent package-management path. Publishing before validation was rejected because FR-018 requires no feed change on validation failure.

## Decision: Capability flags are advisory; endpoint filters remain authoritative

**Rationale**: Studio needs `can-create-workspace`, `can-edit-files`, `can-build`, `can-promote`, and `can-rollback` flags, but FR-029 requires server-side enforcement at each operation. The endpoint filter should require the existing module-management API key baseline and a trusted role when auth plumbing is present; `GetCapabilities` reports the effective result for UX.

**Alternatives considered**: Returning all capabilities true without enforcement was rejected because it would create a weaker path than module-management. Delegating trust solely to Studio was rejected by FR-029.

## Decision: Runtime status maps Nuplane/catalog state into the spec states

**Rationale**: `FeatureManagementService` and Nuplane admin package records already expose active package and feature catalog facts. Extension Builder can persist promotion/reconcile outcomes and map package status to `Loaded`, `PendingRestart`, or `FailedReconciliation`, including contributed features/activities where the manifest/catalog exposes them.

**Alternatives considered**: Inventing a separate runtime registry was rejected because it would drift from Nuplane's runtime state and the existing module-management registry.
