# Phase 0 Research: Extension Builder Studio UI

## Decision: Implement as a built-in Studio Web route

**Rationale**: `src/Elsa.Studio.Web/Client/src/app/App.tsx` already owns built-in routes for Modules and Package feeds while external modules contribute through `api.navigation.add` and `api.routes.add`. Extension Builder is a first-party trusted-team surface tightly coupled to backend capability discovery and should sit beside these operational screens.

**Alternatives considered**: A separate module package would exercise contribution loading but add build/package overhead and duplicate routing seams for a feature that must ship with the host. A standalone shell would violate the constitution and fragment navigation.

## Decision: Reuse module-management workbench patterns

**Rationale**: Existing module management and package feeds pages use dense resource lists, summary strips, toolbar actions, tabs, status chips, dialogs, alert states, sticky inspectors, and Nuplane/reconcile vocabulary. Extension Builder maps naturally to the same language: workspace/project browser, editor split, build diagnostics/logs, and promote/runtime inspector.

**Alternatives considered**: Dashboard cards or a wizard-only experience would hide operational state and conflict with the Workbench constitution. A full IDE clone is unnecessary for trusted-team v1.

## Decision: Use a typed `extensionBuilderApi.ts` client boundary

**Rationale**: The backend contract is authoritative in `elsa-foundation`; this repo should isolate operation names, routes, request/response shapes, and capability flags in one typed module. Tests can mock this boundary consistently while the UI remains aligned with canonical operations.

**Alternatives considered**: Inline `api.backend.http` calls in the page would scatter endpoint strings and make contract drift harder to audit. Generating an OpenAPI client is not available in this repo.

## Decision: Editor v1 uses repo-standard controlled text editor with code affordances

**Rationale**: No Monaco/CodeMirror dependency is present. A controlled `<textarea>` styled with the Studio mono font provides reliable multi-file editing, dirty state, save gating, diagnostics navigation, and tests without adding a new dependency. The component boundary can be replaced with Monaco later if the repo adopts it.

**Alternatives considered**: Adding Monaco would improve C# editing but increases bundle/test complexity and introduces a new runtime dependency. Deferring the editor would fail P1/P2.

## Decision: Poll build/runtime state with explicit logs and diagnostics refresh

**Rationale**: The canonical contract lists `GetBuild` and `GetBuildLog`, not a streaming transport. Timed polling for queued/running builds and follow-up runtime refreshes matches existing module-management follow-up refresh patterns.

**Alternatives considered**: SignalR/SSE streaming is not in the provided contract. Manual refresh only would fail SC-003.

## Decision: Treat capabilities as advisory UX gating

**Rationale**: Defaults resolve authorization as backend-owned. UI reads `GET /_elsa/extension-builder/capabilities` before showing interactive builder state and disables/hides individual actions according to `can-create-workspace`, `can-edit-files`, `can-build`, `can-promote`, and `can-rollback` while relying on server enforcement.

**Alternatives considered**: Mapping to local roles would invent client-side security policy outside the contract. Ignoring flags would fail FR-027/FR-027a.
