# Feature Specification: OpenTelemetry API Minimal API Migration

**Feature Branch**: `codex/1371-wave5-opentelemetry-minimal-apis`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: Migrate the eleven Elsa.Diagnostics.OpenTelemetry shell HTTP registrations to owner-local Minimal API mappings while retaining the three root OTLP routes and preserving HTTP, OpenAPI, SSE, protobuf, and authentication behavior.

## User Scenarios & Testing

### User Story 1 - Query and live telemetry access (Priority: P1)

Operators can search resources, traces, metrics, and logs, inspect storage and trace details, and subscribe to the live stream through the same routes and permission behavior.

**Why this priority**: These are the primary Studio-facing diagnostics workflows.

**Independent Test**: A real host exercises every query route and SSE stream with anonymous, exact, implied, wildcard, tenant, and resource authorization cases and compares the frozen FE HTTP/OpenAPI observations.

**Acceptance Scenarios**:

1. **Given** a valid query and an authorized principal, **When** a query route is called, **Then** the same status, JSON shape, headers, and result are returned as before.
2. **Given** a stream request and cancellation, **When** the client connects and disconnects, **Then** SSE framing, filtering, completion, and cleanup remain stable.

### User Story 2 - OTLP ingestion (Priority: P1)

Collectors can submit trace, metric, and log protobuf payloads to the existing OTLP paths with authentication evaluated before any body read.

**Why this priority**: Ingestion is the data path that feeds every query and stream workflow.

**Independent Test**: A real host sends valid, malformed, oversized, compressed, API-key, and loopback requests and verifies the exact result and that rejected requests do not read the body.

**Acceptance Scenarios**:

1. **Given** a valid protobuf request and accepted transport credentials, **When** it is posted to a signal route, **Then** the handler ingests it and returns the established success status.
2. **Given** missing or invalid credentials, **When** a request with a sentinel body is posted, **Then** the request is rejected before the sentinel body is consumed.

### User Story 3 - Host composition and unloadability (Priority: P2)

Hosts can compose the OpenTelemetry feature once without duplicate OTLP routes, retain owner metadata and permission ownership, and unload the feature after repeated real endpoint use.

**Why this priority**: Module isolation and predictable composition prevent security and deployment regressions during the migration.

**Independent Test**: Repeated collectible-context cycles map the feature, materialize DI/auth/provider/serializer state, execute query/SSE/OTLP delegates, dispose services, and verify weak references collect.

**Acceptance Scenarios**:

1. **Given** a shell host that maps the feature and a root host that maps the receiver, **When** composition starts, **Then** the three OTLP routes exist exactly once and all eleven shell routes carry stable owner, authoring, operation, tag, and policy metadata.
2. **Given** a completed request cycle, **When** the host and route table are disposed, **Then** the owner assembly, delegates, metadata, providers, and serializers become collectible.

## Edge Cases

- Invalid query values return the established binding error without invoking the provider.
- Stream cancellation and subscriber disposal do not leave a live-feed subscription behind.
- API-key and loopback authentication reject invalid requests before decompression, size checks, or protobuf parsing.
- Tenant/resource claims cannot broaden the module-owned permission beyond the shared evaluator's exact, implied, or wildcard decisions.
- A host cannot accidentally map both the shell OTLP adapter and the explicit root OTLP mapper.

## Requirements

### Functional Requirements

- **FR-001**: System MUST replace exactly eleven OpenTelemetry shell FastEndpoints registrations with one owner-local Minimal API mapper.
- **FR-002**: System MUST retain exactly three root OTLP signal routes without double mapping.
- **FR-003**: System MUST preserve all existing query, trace-detail, storage, collector-configuration, SSE, and OTLP HTTP statuses, bodies, headers, content types, binding errors, and cancellation behavior.
- **FR-004**: System MUST preserve OTLP API-key/loopback authentication before body read and keep it separate from Foundation Identity principal permissions.
- **FR-005**: System MUST contribute the query/stream permission from the OpenTelemetry owner and consume it through the shared evaluator, proving exact, implied, wildcard, tenant, resource, and mixed-host behavior.
- **FR-006**: System MUST publish stable owner, Minimal API authoring, route, operation ID, tag, and public/policy metadata for every migrated route.
- **FR-007**: System MUST retain protobuf-safe ingestion and the established JSON/SSE wire contract without introducing a reflection-retaining serializer dependency.
- **FR-008**: System MUST prove repeated collectibility after executing real query binder, typed serialization, configured authentication/provider delegates, SSE completion/cancellation, OTLP authentication, protobuf parsing, DI disposal, and route cleanup.
- **FR-009**: System MUST remove only the eleven transition registrations and owner-only unused FastEndpoints dependencies, update generated maps and migration evidence, and leave unrelated transitional owners unchanged.

## Key Entities

- **Telemetry query**: A bounded filter for resources, traces, metrics, or logs.
- **Live stream item**: A resource, trace, metric, log, or dropped-item payload formatted as an SSE event.
- **OTLP signal batch**: A protobuf-encoded trace, metric, or log batch authenticated and ingested by the shared handler.
- **Permission disposition**: The OpenTelemetry owner permission evaluated with Foundation Identity's exact, implied, wildcard, tenant, and resource context.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All eleven published OpenTelemetry routes (eight query/SSE routes plus three OTLP signal routes) have one-to-one route/method parity with the immutable before evidence, with only explicitly reviewed metadata differences.
- **SC-002**: The complete HTTP/OpenAPI/SSE/protobuf compatibility suite passes, including invalid input and pre-body-read authentication cases.
- **SC-003**: Three consecutive collectible-context cycles pass for the OpenTelemetry owner with no retained route, delegate, provider, serializer, or DI state.
- **SC-004**: Generated maps, architecture checks, the full OpenTelemetry test suite, affected diagnostics E2E, and the full solution build pass.

## Assumptions

- The existing `OtlpHttpIngestionHandler`, protobuf parser, provider contracts, and SSE formatter remain the protocol authorities.
- Foundation Identity remains the only evaluator authority for query/stream permissions; OTLP transport credentials remain owner-specific.
- The frozen FastEndpoints host and its consumed OpenAPI document are available before deletion.
