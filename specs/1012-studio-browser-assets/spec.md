# Feature Specification: Prebuilt Studio Browser Assets

**Feature Branch**: `agent/codex/3361-studio-browser-assets`
**Created**: 2026-10-26
**Status**: Implemented
**Input**: Program #3340 task #3361: deliver the complete standard Studio frontend through the selected reusable package, so custom host authors reference packages instead of copying source or building frontend assets.

## User Scenarios & Testing

### User Story 1 - Run the standard Studio from packages (Priority: P1)

A host author references the standard hosting package and a selected module, publishes the host, and opens Studio without copying host frontend code or installing a frontend toolchain on the consumer machine.

**Why this priority**: A reusable server package is insufficient if its consumer still has to reconstruct the browser application.
**Independent Test**: Publish a clean external package-only host, hide its build inputs and package cache, deny frontend-tool execution, and open its actual Studio shell and a deep link.

**Acceptance Scenarios**:
1. The published root and deep link serve the packaged application document; application scripts, styles, fonts, images and import-map dependencies all load from published files.
2. The actual browser application boots with a selected packaged module and its discovery metadata. Host, SDK and module versions remain compatible.
3. Missing API and asset paths return real missing-resource responses, while valid static resources retain existing MIME, range and cache revalidation behavior.
4. Consumer restore, publish and execution succeed with no repository source references or frontend-tool invocation.

### User Story 2 - Build modules against one shared browser contract (Priority: P2)

A module author builds against the same SDK and UI sources that the standard host delivers, while module-owned assets remain with their own packages.

**Why this priority**: Moving ownership must preserve the shared runtime, imports and existing extension points.
**Independent Test**: Build existing module clients and load a representative module through the published host's import map; verify its registration and actual rendered route.

**Acceptance Scenarios**:
1. The workspace has one owner for standard application, SDK/UI source facades and built shell assets; Web no longer carries a second copy.
2. Existing module build/typecheck/test entry points resolve the moved shared sources. Module assets retain their existing package-specific URLs.
3. Candidate package versions and discovered host/SDK/module versions agree with the existing compatibility contract; moving source does not introduce fixed incompatible version requirements.

### User Story 3 - Supply an intentional custom frontend (Priority: P2)

A host author can keep reusable server behavior while deliberately supplying a different frontend or fallback document.

**Why this priority**: Package reuse must retain the already selected customization boundary.
**Independent Test**: Publish a consumer with the supported asset opt-out and an application-owned document; verify the reusable server API and independently selected module assets still execute.

**Acceptance Scenarios**:
1. The direct-reference asset opt-out removes the standard browser payload while preserving server boot and module discovery.
2. Independent module assets remain available, and the consumer's own document publishes without collision.
3. A separately named fallback document works with the standard bundle; unsupported same-path overlays fail clearly through the existing SDK behavior.

### Edge Cases

Skipped producer frontend build; missing vendor or SDK output; stale generated Web assets; absolute browser URLs; stable bundle names; module CSS/chunks/fonts; import-map cycles or duplicate React instances; absent optional addons; package dependency closure; clean publish without source/cache; custom fallback; advanced frontend opt-out.

## Requirements

- **FR-001** Deliver all standard browser application assets through the accepted Hosting owner, retaining existing browser URLs and independently packaged module assets.
- **FR-002** Move standard application and shared SDK/UI frontend source/build ownership out of Web without duplicate maintained copies. Update active build, test, tooling, container and contributor entry points.
- **FR-003** Keep browser build inputs and producer-only tooling out of consumer archives and restore/publish execution. Producer CI builds and verifies the complete payload before packing.
- **FR-004** Preserve root/deep-link delivery, real unknown API/asset responses, MIME types, range requests and existing cache revalidation.
- **FR-005** Preserve import-map sharing and the existing version/compatibility contract. Modules continue to compile against the same shared SDK/UI source contract served at runtime.
- **FR-006** Preserve the supported separately named fallback and direct-reference frontend asset opt-out, with essential server behavior independent of asset build integration.
- **FR-007** Qualify actual production archives and an external published host using public composition APIs, isolated package cache/source mapping, hidden inputs and denied consumer frontend tools; temporary wrapper libraries do not establish acceptance.
- **FR-008** Verify existing affected whole suites, actual browser boot/module route, artifact bytes/dependencies and missing-file negative controls before delivery.
- **FR-009** Maintain Program #3340 claims, dependencies, separate verification and reviewed delivery evidence. Do not mark the leaf complete before actual reviewed merge/main qualification.

### Key Entities

Standard browser payload; import map; shared SDK/UI source facade; module-owned static assets; published consumer; asset opt-out. Their existing meanings and hosting boundaries remain in the canonical hosting and asset-delivery contracts.

## Success Criteria

- **SC-001** Every resource in the packed standard payload and selected module payload is present with matching bytes in the independent published host; zero import-map targets or transitive browser resources are missing.
- **SC-002** The actual application and a selected module route render from the published payload after producer, consumer source and package cache are hidden, with zero consumer frontend-tool invocations.
- **SC-003** Supported customization profiles preserve reusable server behavior and independent module assets; missing-file controls fail specifically and restoration passes.
- **SC-004** Affected build/test, artifact and independent review evidence cover each acceptance criterion, followed by actual reviewed merge/main verification.

## Assumptions and Scope

This is the already authorized independent #3361 leaf; prerequisites #3355/#3357 are delivered. The accepted owner is Elsa.Studio.Hosting, with fixed `/studio` browser URLs and existing module `/_content` URLs. No new assets package or arbitrary application PathBase is introduced. Existing private npm package identities and versioning policy are retained unless a concrete incompatibility requires correction. Final full backend/auth/custom-module/optional-addon combinations remain #3363/#3364 and final automation/docs #3365/#3366. #3359 remains stopped; #3360 is locally reviewed but blocked on GitHub app push access, on its saved separate branch. No release, deployment, publisher activation or implementation delegation is authorized by this leaf.
