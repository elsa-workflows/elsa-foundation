# Feature Specification: React Admin Module Host

**Feature Branch**: `codex/react-admin-module-host`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Build a React-based Elsa admin shell that discovers installed admin modules from the ASP.NET host, loads their built React assets at runtime, and lets first-party and third-party modules register routes, navigation, dashboard widgets, diagnostics, and future designer extension points through a stable admin SDK. Preserve the current Monday demo app at `/` and expose it at `/demo`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open The Modular Admin Shell (Priority: P1)

An ASP.NET host user can open the admin shell separately from the temporary Monday demo app.

**Why this priority**: The admin host must exist before contributed modules can prove the extension model.

**Independent Test**: Start `Elsa.Server`, open `/admin`, and confirm the admin shell loads while `/` still opens the demo app.

**Acceptance Scenarios**:

1. **Given** the host references the admin packages, **When** a user opens `/admin`, **Then** the admin shell loads from the admin static assets.
2. **Given** the current demo app exists, **When** a user opens `/` or `/demo`, **Then** the demo app still loads.

---

### User Story 2 - Load Installed Admin Modules (Priority: P1)

The admin shell discovers installed admin modules from the server and activates compatible modules without rebuilding the shell.

**Why this priority**: Runtime module discovery is the core feature.

**Independent Test**: Reference sample module packages, call the manifest endpoint, and confirm the shell loads the contributed routes and navigation.

**Acceptance Scenarios**:

1. **Given** dashboard and weather sample modules are installed, **When** the shell starts, **Then** it imports both compatible module entries and displays their navigation items.
2. **Given** a module declares an incompatible host or SDK version, **When** the shell starts, **Then** the module is skipped and diagnostics explain why.
3. **Given** one module entry fails to import, **When** later modules are compatible, **Then** later modules still activate.

---

### User Story 3 - Prove Frontend-Only And Server-Backed Modules (Priority: P2)

Developers can inspect two simple sample modules that prove both frontend-only and server-backed extension shapes.

**Why this priority**: The first slice must show a realistic third-party module path without pulling in workflow designer complexity.

**Independent Test**: Open `/admin/dashboard` and `/admin/weather`; the dashboard route renders local widgets, and the weather route fetches deterministic data from its sample endpoint.

**Acceptance Scenarios**:

1. **Given** the dashboard sample module is installed, **When** a user opens `/admin/dashboard`, **Then** the module renders dashboard widgets without a custom backend endpoint.
2. **Given** the weather sample module is installed, **When** a user opens `/admin/weather`, **Then** the module fetches and renders deterministic forecast data from `/_elsa/samples/weather-forecast`.

### Edge Cases

- A disabled module is omitted from the active manifest list and appears in diagnostics.
- A module entry URL cannot be imported.
- A module stylesheet fails to load.
- The manifest endpoint returns no modules.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The admin shell MUST fetch installed admin module manifests from the ASP.NET host before activating contributed module UI.
- **FR-002**: The server MUST expose `GET /_elsa/admin/modules` with host version, SDK version, module manifests, and server-side diagnostics.
- **FR-003**: Server-side Elsa features MUST be able to contribute admin module manifests through a named host-owned event contribution lifecycle.
- **FR-004**: The admin shell MUST dynamically import compatible module entries without requiring a shell rebuild.
- **FR-005**: Admin modules MUST register contributions through a stable TypeScript admin SDK instead of private shell internals.
- **FR-006**: The first SDK surface MUST support routes, navigation items, dashboard widgets, panels, toolbar actions, activity editors, property editors, workflow designer placeholders, host HTTP access, and diagnostics.
- **FR-007**: The admin shell MUST check host and SDK version compatibility before activating a module.
- **FR-008**: The admin shell MUST isolate module activation failures so one failed optional module does not prevent compatible modules from loading.
- **FR-009**: Module assets MUST be served from same-origin application paths by default.
- **FR-010**: The first implementation MUST support modules available at ASP.NET application startup.
- **FR-011**: The admin shell MUST provide diagnostics for loaded, skipped, incompatible, and failed modules.
- **FR-012**: The current demo React app MUST remain reachable at `/` and `/demo`.

### Key Entities

- **Admin Module Manifest**: Server-provided description of one installed admin module, including id, display name, version, entry URL, styles, host compatibility, SDK compatibility, and capabilities.
- **Admin Module Diagnostic**: Load or availability status for a module.
- **Admin Module Registry**: Browser-side collection of routes, navigation items, dashboard widgets, extension placeholders, and diagnostics contributed by modules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host with no contributed modules can load `/admin` without errors.
- **SC-002**: Dashboard and weather sample modules can be added without rebuilding the admin shell source.
- **SC-003**: A failed or incompatible module does not prevent another compatible module from loading.
- **SC-004**: Server and frontend tests prove manifest collection, compatibility rejection, activation failure isolation, and sample module rendering.

## Assumptions

- Modules are trusted installed code served from same-origin paths.
- Hot install, hot unload, remote module origins, sandboxing, Flowchart designer, auth replacement, and theme replacement are out of scope.
- React 19, Vite, npm, static web assets, and import maps are acceptable for this first slice.
