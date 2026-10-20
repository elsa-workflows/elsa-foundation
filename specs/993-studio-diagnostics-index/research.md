# Research: Studio Diagnostics Index

**Spec**: [spec.md](spec.md)  
**Product source baseline**: `e656cc6042fd86d15d61c7278d846222054eb535`  
**Approved spec commit**: `3cd7d16e9`

## Decisions

### Use the Workbench's existing filtered navigation set

**Decision**: `App.tsx` already computes the navigation and access values. At its existing Diagnostics render call, apply `filterAccessibleNavigation(navigation, access)` and pass that result to Diagnostics. The index preserves each eligible contribution's exact route and label. ShellFrame's existing sidebar/dock filtering and route resolution remain in place; do not refactor App or duplicate access rules.

**Rationale**: The host uses the same navigation data and filter helper for its navigation surfaces. Passing the filtered list gives Diagnostics the exact visible set without reimplementing feature, role, or permission checks. Direct route resolution is separate from sidebar filtering, so filtering a link must not be represented as proof that a bookmarked URL is denied.

**Evidence**: `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:348-372,395-403,431-439,499-504,1048-1072`; `Studio/src/apps/Elsa.Studio.Web/Client/src/app/navigation/placement.ts:105-140`; `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts:214-244`. The current render call passes `api` to Diagnostics; App also has the current `navigation` and `access` values immediately in scope.

Frontend module registration is configured separately from the Workbench backend composition. `Studio/src/apps/Elsa.Studio.Web/shells.json:17,20` enables `OpenTelemetryStudio` and `StructuredLogsStudio`; `StudioFeatureAssemblies.cs:22-35` supplies their assemblies; `StudioModuleManifestProvider.cs:134-148,195-198` includes only modules whose Studio shell features are active. The runtime feature-configuration store interprets `false` as disabled (`StudioShellFeatureConfigurationStore.cs:106-128`), but startup composition uses CShells configuration. For the baseline startup config, use a separate copy of the Studio normal `shells.json` and omit those two keys from `CShells:Shells:Default:Features`, preserving all other entries; verify both modules are disabled in the Studio module manifest. Pair it with backend `Essentials/apps/Elsa.Workbench/shells.baseline.json`. The backend baseline alone does not prove that the frontend omitted the registrations.

**Alternatives considered**:

- Reimplement feature, role, and permission filtering inside Diagnostics — rejected because it duplicates the host's access rules.
- Infer backend or endpoint access from route registration — rejected because registration does not prove backend readiness or endpoint authorization.
- Add missing `requiresPermission` gates to existing built-in route entries — rejected because the task preserves current permissions; OpenTelemetry, Structured Logs, and Runtime settings destinations currently own their API/page-level 401/403 behavior.

### Keep built-in task descriptions local and custom entries neutral

**Decision**: Known built-in diagnostic destinations receive accurate task descriptions. Registered custom entries in the existing Diagnostics grouping retain their exact labels, routes, and access gates. If existing custom metadata does not communicate a task, use neutral navigation wording and do not guess from an unrelated route name. Do not add a description field to the contribution contract.

**Rationale**: The existing navigation contribution has label, path, ordering, placement, and optional feature/role/permission gates, but no description. A new field is outside the approved scope and would impose a contract change on contributors.

**Evidence**: `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts:214-244`; root's #3073 decision recorded in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-selection-readiness.md` and the approved specification's FR-001/FR-013.

**Alternatives considered**:

- Guess a task description from every route name — rejected because it can invent unsupported behavior and would include unrelated routes.
- Add shared description metadata — rejected because the spec excludes a new SDK/contribution field.
- Drop custom diagnostics entries — rejected because the approved spec preserves their existing labels, routes, and access gates.

### Separate route visibility from destination state

**Decision**: The index reports navigable destinations only. Backend loading, unsupported, permission, error, and empty-data states remain owned by each destination. Do not introduce endpoint probes, a shared capability/health model, or a route/widget-count health claim.

**Rationale**: The host has no shared endpoint-health contract. OpenTelemetry's overview request can fail while setting its summary to null, and the Structured Logs page owns separate connection and error states. Registration and widget count do not establish backend health.

**Evidence**: `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:1048-1072`; `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx:203-254,572-576`; `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx:150-238,684-695`.

**Alternatives considered**:

- Poll each tool endpoint from the index — rejected because it duplicates destination work and has no shared capability contract.
- Infer a backend state from route registration, widget count, or the OpenTelemetry overview count — rejected because none is proof of destination health.
- Replace destination states with one Diagnostics-level error — rejected because it would conflate loading, denied, unavailable, failed, and empty outcomes.

### Explain OpenTelemetry limits on both surfaces

**Decision**: The Diagnostics description states that trace inspection is supported and Metrics/OpenTelemetry Logs are unavailable. The OpenTelemetry page itself must identify those views as unavailable or omit their entry affordances and explain the limitation on that page before any placeholder content can be entered. A failed trace request remains distinct from an empty result.

For Structured Logs, the index offers the application-log handoff whenever its filtered navigation set confirms the destination is registered and accessible, as required by approved FR-008. Either surface may offer a direct Structured Logs link only when it establishes both conditions. If OpenTelemetry cannot establish them, it uses a neutral handoff to `/diagnostics` and makes no claim that Structured Logs is present or accessible.

**Rationale**: The current OpenTelemetry page exposes selectable Metrics and Logs tabs with placeholder content. Its Logs placeholder unconditionally says Structured Logs is available. Both claims need to remain truthful, and the page cannot infer endpoint authorization from registration.

**Evidence**: `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx:167-199,203-254,572-576`; `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:348-372,499-504`; approved spec FR-007/FR-008.

**Alternatives considered**:

- Keep apparently functional placeholder tabs and rely only on index copy — rejected because users can still enter an unexplained or misleading placeholder.
- Always render a direct Structured Logs link from OpenTelemetry — rejected because its route may be absent or filtered.
- Add a backend capability API — rejected by scope.

### Rename only the settings discovery text

**Decision**: Present the existing capture-policy destination as “Runtime evidence settings” while preserving its address, view access, save authorization, effective policy, and save payload. A user who can view but cannot save continues to find and read the page; the existing write gate remains unchanged.

**Rationale**: This is a discovery-label change. It must not expand or narrow permissions or alter persisted policy behavior.

**Evidence**: `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx:87-95,142-156`; `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage.tsx:37,79-95,115-119,144-169`; `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/runtimeDiagnosticsSettings.test.tsx:23-38,57-120`.

**Alternatives considered**:

- Change the URL or settings behavior to match the new label — rejected because the approved spec requires the exact current destination and behavior.
- Hide the destination from view-only users — rejected because they currently can view settings and the approved spec requires preserving that discovery.
- Broaden write permissions or rely on a disabled button as the only authorization — rejected because the existing backend write gate remains authoritative.

### Preserve contributed diagnostic widgets and their isolation

**Decision**: Keep existing diagnostic widgets, their content and their loading, ready, streaming, and error behavior. One widget failure must not hide other widgets or tool links. Widget count is not a destination-health signal.

**Rationale**: The existing Diagnostics surface hosts module-contributed widgets, including loading and subscription behavior and per-widget error isolation. Adding tool navigation must preserve that surface.

**Evidence**: `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx:1075-1149`; `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts:382-401`; existing preservation coverage in `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx:15-101`.

**Alternatives considered**:

- Replace the widget section with navigation links — rejected because it removes existing contributions.
- Derive a health summary from widget count or a widget's status — rejected because widgets are independent extensions and do not establish service readiness.

### Keep proof levels explicit

**Decision**: Use focused component tests for actual user-visible states, a bounded browser fixture for actual host/navigation components, and the required composed Workbench before/after comparison for normal and baseline shells. Report fixture evidence and composed-host evidence as different things. The live comparison remains a pending acceptance gate until the safe source-backed procedure is executed.

**Rationale**: Existing classifier tests do not prove what the OpenTelemetry or Structured Logs page displays. The existing browser fixture uses a Vite harness and does not start Elsa Workbench or load its real module manifest. A historical #3058 smoke is not a repeatable procedure for this work.

**Evidence**: `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-validation-seams.md`, particularly the “Assertions to make RED before product edits” and “Composed Workbench limit” sections. The current test commands and supplied baseline results are documented in [quickstart.md](quickstart.md).

**Alternatives considered**:

- Treat error-classifier tests as proof of destination UI states — rejected because they do not render the destination pages.
- Treat the Vite browser fixture as a composed Workbench run — rejected because it contains no real host composition or authenticated session.
- Reuse historical runtime data/configuration — rejected because the old smoke has neither this scenario nor a repeatable isolation/cleanup recipe.

## Contract decision

No `contracts/` artifact is needed. This work adds no external interface, wire shape, API operation, SDK field, or public contribution contract. It consumes existing in-app navigation metadata and preserves existing destination routes and settings behavior. User-visible state requirements are covered by the specification and tested at the component/host boundaries rather than published as a new external contract.

## Baseline evidence

Root reports that the untouched product at `e656cc6042fd86d15d61c7278d846222054eb535` passed an offline install with the existing lockfile and these current test counts: Web 563 tests across 49 files; OpenTelemetry 5 tests in 1 file; Structured Logs 10 tests in 1 file; Workflows 2,124 tests across 167 files. These are supplied baseline results, not rerun during this planning step. They do not prove the new destination DOM states, the filtered Diagnostics index, or either live Workbench composition.

## Remaining technical research

A source-derived isolation proposal is available at `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-isolated-workbench-procedure.md`; it has not been executed. It identifies fresh configuration/content roots, SQLite isolation, disposable identity handling, invocation/readiness commands, and cleanup. Its initial Data Protection concern is resolved at planning time through the existing host configuration: `AddConfiguredDataProtection` supports an EF SQLite key store with an absolute connection string, and `ApplicationName` isolates each run (`Essentials/src/Foundation/DataProtection/EntityFrameworkCore/Hosting/DataProtectionConfigurationExtensions.cs:57-70`; `Essentials/src/Persistence/EntityFramework/EfHostStoreOptions.cs:7-16`). Root will verify the private key database placement during QA; no config change or host launch has been made here. The composed-host acceptance gate remains pending until both normal and baseline before/after checks and cleanup are completed. The normal shell supports a seeded disposable development admin. The baseline omits identity, so its anonymous run proves registration/absence only, not authenticated permission filtering or write authorization. Keep settings read-only/write evidence in component tests or authenticated normal-composition evidence. For frontend absence, pair backend `Essentials/apps/Elsa.Workbench/shells.baseline.json` with a separate copy of `Studio/src/apps/Elsa.Studio.Web/shells.json` that omits `OpenTelemetryStudio` and `StructuredLogsStudio` from the default Features object; verify both client modules are disabled in the manifest. A backend-only baseline would test missing support while frontend routes may remain registered. A browser fixture does not substitute for composed-host evidence. This is a validation-preparation question, not a product contract or scope ambiguity.
