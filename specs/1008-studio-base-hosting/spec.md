# Feature Specification: Reusable Studio base hosting

**Feature Branch**: `agent/codex/3357-studio-hosting`

**Created**: 2026-10-26

**Status**: Implemented

**Input**: Deliver [#3357](https://github.com/elsa-workflows/elsa-foundation/issues/3357) within
[Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340): provide standard
Studio startup, discovery, runtime configuration and endpoint wiring without copied host infrastructure.
Implement the relevant portion of the accepted [hosting contract](../../docs/reports/studio-hosting-contract.md).

## User Scenarios & Testing

### User Story 1 - Compose a small Studio host (Priority: P1)

A developer selects Studio modules and application configuration, then uses common registration,
pipeline and endpoint entry points. Hosting does not require package administration, themes or the
reference distribution's module set.

**Independent Test**: Start a host selecting an external fixture feature and its own shell configuration.
Read module discovery and runtime configuration through real HTTP without optional capabilities installed.

**Acceptance Scenarios**:

1. **Given** a selected shell with Studio API and a third-party feature, **When** the host starts,
   **Then** discovery reports that feature and configured host/SDK versions.
2. **Given** a shell name other than Default and a content root different from the working directory,
   **When** requests arrive, **Then** activation, routing and discovery select the configured shell.
3. **Given** host-owned configuration with local file loading explicitly disabled,
   **When** the host starts, **Then** it uses that configuration without requiring or creating another shell file.
4. **Given** missing mandatory Studio API configuration or invalid explicit hosting options,
   **When** the host starts, **Then** it rejects the configuration with an actionable error.

### User Story 2 - Preserve routing and request lifetimes (Priority: P1)

A module author contributes protected endpoints and asynchronous discovery behavior. Cold requests and
requests overlapping shell replacement must follow the same authorization and service lifetime rules as warm requests.

**Independent Test**: Request a protected fixture endpoint first after startup and again when warm; overlap
an asynchronous discovery request with shell replacement and observe scope disposal.

**Acceptance Scenarios**:

1. **Given** a cold protected shell endpoint, **When** an unauthorized request arrives,
   **Then** its policy runs before the handler and denies access on both first and subsequent requests.
2. **Given** an authorized request, **When** the shell endpoint is selected, **Then** the expected handler runs.
3. **Given** an in-flight discovery operation and a shell reload, **When** the old generation drains,
   **Then** the operation retains its tracked scope until completion or the framework's bounded drain policy.
4. **Given** a generation disposed between registry lookup and scope acquisition,
   **When** discovery begins, **Then** hosting retries acquisition within a bounded policy or returns unavailable;
   it never exposes an untracked raw provider.
5. **Given** a canceled request, **When** activation or discovery awaits work,
   **Then** cancellation propagates without starting unbounded replacement work.

### User Story 3 - Serve browser configuration and files predictably (Priority: P2)

A developer provides browser files and a fallback document. The runtime script exposes only supported
browser settings, while unknown API routes remain distinguishable from application routes.

**Independent Test**: Serve temporary real files through the common pipeline and inspect HTTP bodies,
MIME types, revalidation responses, discovery priority and unknown API behavior.

**Acceptance Scenarios**:

1. **Given** a configured backend management key, **When** runtime configuration is requested,
   **Then** neither the key nor server-only backend configuration is exposed.
2. **Given** stable entry files, **When** they and the fallback document are served,
   **Then** responses require revalidation and conditional requests behave correctly.
3. **Given** an unknown `/_elsa` API route, **When** it is requested,
   **Then** the response is 404 rather than fallback HTML.
4. **Given** a separately named application fallback document, **When** a browser route is requested,
   **Then** that configured document is served through the host's static provider.

### Edge Cases

- Missing shell blueprint or failed activation leaves static files available; it does not bypass a real endpoint policy.
- Blank/invalid shell names, blank fallback documents and unsafe query-token prefixes fail validation.
- Options callbacks override bound configuration; later configuration reload does not silently change installed routing/security choices.
- An external configuration writer acquires the same mutation lease exposed to optional adapters; canceled acquisition and repeated lease disposal are safe.
- The base package remains usable when no optional module assembly is selected.

## Requirements

### Functional Requirements

- **FR-001**: Provide common registration, pipeline and endpoint composition with explicit module assembly customization.
- **FR-002**: Include mandatory Studio API discovery without requiring the reference Web application or optional capabilities.
- **FR-003**: Bind existing configuration first, apply explicit callbacks last, resolve relative paths against ContentRoot and freeze installed choices.
- **FR-004**: Use the selected shell consistently for activation, fallback resolution and discovery; validate its mandatory API feature before serving.
- **FR-005**: Activate before routing/authorization so contributed endpoint policies protect cold and warm requests equally.
- **FR-006**: Retain tracked shell scopes across asynchronous discovery, handle disposal races and honor request cancellation.
- **FR-007**: Preserve browser configuration allowlisting, existing response shapes, discovery priority, unknown API 404 and static revalidation.
- **FR-008**: Expose only a cancellation-aware asynchronous configuration mutation lease; writers cannot release or dispose the underlying coordinator.
- **FR-009**: Keep one configuration coordinator per running host and preserve whole read/revision/write/reload critical sections in existing writers.
- **FR-010**: Remove superseded Web implementations when the application adopts the extracted behavior; retain its distribution/module/Console Stream choices.

### Key Entities

- **Hosting options**: application-selected shell, optional shell file, fallback document and trusted query-token prefixes.
- **Composition builder**: registration-time access to root services, configuration, environment, framework customization and configuration coordination.
- **Configuration mutation lease**: exclusive borrowed access spanning a whole coordinated configuration operation.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A small consumer starts and serves discovery/runtime routes using the common entry points and its own feature assembly.
- **SC-002**: Cold and warm unauthorized requests both execute zero protected handlers; an authorized request succeeds.
- **SC-003**: Discovery overlapping replacement completes without prematurely disposed scoped dependencies, subject to the framework drain deadline.
- **SC-004**: All qualified static/discovery/runtime/fallback responses preserve their expected bodies, status codes and caching behavior.
- **SC-005**: No optional package administration/theme/reference-distribution dependency is mandatory in the base hosting closure.

## Assumptions

- The accepted hosting contract supplies package/API decisions; this work does not reopen those choices.
- #3358 separately extracts the Studio bearer/management bridge implementation. This leaf preserves existing Web authorization and proves generic contributed policies through the common pipeline.
- #3359/#3360 extract optional adapters, #3361 moves prebuilt frontend ownership, and #3362 completes the thin distribution host. Full package-only/browser/optional matrices remain #3363/#3364.
- Use released CShells lifecycle, resolver and service-sharing contracts; no new loader, settings or file-provider framework.
- Tests are risk-based; the derived constitution declares no mandatory test-first cadence. Preserve existing behavior and establish causal controls for changed behavior.
- No release, deployment, publisher activation or unrelated program work is included.
