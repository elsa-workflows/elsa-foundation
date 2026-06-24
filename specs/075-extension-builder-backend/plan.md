# Implementation Plan: Extension Builder — Backend Pipeline (Trusted-Team v1)

**Branch**: `sfmskywalker-extension-builder-backend-implementation` | **Date**: 2026-06-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/075-extension-builder-backend/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Implement the `/_elsa/extension-builder` backend pipeline inside `Elsa.Server`, co-located with the existing module-management API and reusing Nuplane admin/runtime catalog services for promotion, reconciliation, rollback status, and capability reporting. The implementation adds server-side persisted authoring workspaces/projects, file editing, per-project serialized builds that create NuGet packages from immutable source snapshots, validation-backed promotion into the Nuplane drop-folder feed, runtime status, rollback, retry, and explicit trusted-team authorization/capability semantics.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET `net10.0`

**Primary Dependencies**: ASP.NET Core minimal APIs, `Nuplane.Admin`, existing `Elsa.Modularity.Nuplane` services, `dotnet` SDK CLI for restore/build/pack, System.Text.Json

**Storage**: Server-local file system under host content root for authoring state, source snapshots, build logs, and artifacts; existing Nuplane configured directory feed for promoted packages

**Testing**: xUnit via existing `dotnet test` projects; focused tests for endpoint auth/capability behavior and extension-builder service logic

**Target Platform**: Single Nuplane-enabled Elsa Server host on platforms supported by the .NET SDK

**Project Type**: Backend web-service/application-host feature

**Performance Goals**: Per-project build requests are deterministic and serialized; nominal template build/promote round trips complete reliably rather than concurrently mutating project artifacts.

**Constraints**: Trusted-team v1 only; authenticated trusted/admin role plus module-management API key baseline; owner-scoped workspaces; last-write-wins file edits; no hostile-code sandboxing, quotas, package signing, cluster fan-out, or AI authoring; project deletion removes authoring state only.

**Scale/Scope**: Single host, local trusted developers, multiple workspaces/projects, serialized builds per project, immutable source snapshot per build without full editable history.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Layering/naming (§2.1, §2.2, §E2.1)**: Keep implementation in the application host unless a reusable domain surface proves necessary; no new global `Features`/`Contracts` bucket, no implementation-to-implementation reference from unrelated packages.
- **Reuse and bridge default (§2.6, §2.7, §3)**: Promotion/reconciliation/runtime catalog behavior must delegate to existing module-management/Nuplane services instead of creating a parallel package manager.
- **Dependency envelope (§2.1, §2.13, §E2.4)**: Do not force build tooling or ASP.NET dependencies into `.Core` libraries; build orchestration remains host/application-layer v1.
- **Test discipline (§2.21, §2.23)**: Add tests for new registration/endpoint/service behavior, authorization failure paths, and pipeline logic that can be isolated without requiring a full hostile-code sandbox.
- **Runtime/package gates (§3, §E2.6)**: Promoted artifacts must be Nuplane-loadable package artifacts; status must surface `Loaded`, `PendingRestart`, or `FailedReconciliation` without implying runtime loaded unvalidated source.

Initial gate status: **PASS**. The plan keeps the feature in `Elsa.Server`, reuses Nuplane/module-management seams, and documents v1 exceptions/out-of-scope boundaries from the spec.

## Project Structure

### Documentation (this feature)

```text
specs/075-extension-builder-backend/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
src/Apps/Elsa.Server/
├── ElsaExtensionBuilderApi.cs
├── ExtensionBuilder/
│   ├── ExtensionBuilderOptions.cs
│   ├── ExtensionBuilderModels.cs
│   ├── ExtensionBuilderService.cs
│   ├── ExtensionBuilderStorage.cs
│   ├── ExtensionBuilderTemplates.cs
│   ├── ExtensionBuilderBuildRunner.cs
│   └── ExtensionBuilderPromotionService.cs
└── Program.cs

tests/Elsa/Modularity/Tests/
├── ExtensionBuilderServiceTests.cs
└── ExtensionBuilderAuthorizationTests.cs
```

**Structure Decision**: Implement as a host-owned application feature in `src/Apps/Elsa.Server` because it depends on ASP.NET request authorization, host content-root storage, the server .NET SDK, and Nuplane admin/runtime operations. Keep reusable Nuplane behavior delegated to `src/Elsa/Modularity/Nuplane/` instead of moving build/promotion orchestration into a `.Core` package prematurely.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
