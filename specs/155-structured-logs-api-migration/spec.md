# Feature Specification: Structured Logs API Minimal API Migration

**Feature Branch**: `codex/1349-structured-logs-minimal-api`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "Migrate the complete Structured Logs REST and SSE surface to the first-party Minimal API pattern while preserving query, streaming, authorization, compatibility, coexistence, and dynamic-unload contracts."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect recent structured logs and sources (Priority: P1)

An authorized operator discovers the available log sources and queries recent structured log entries using the existing filters and configured routes, receiving the same safe JSON contracts and validation outcomes as before the migration.

**Why this priority**: Recent history and source discovery are the immediately useful diagnostics surface and establish contract, option-driven routing, serialization, and permission parity before the more complex live stream is changed.

**Independent Test**: Compare the current and replacement recent and sources operations across default and configured paths, valid and invalid filters, empty and populated stores, authorization states, ordering, bounds, and serialization with no unapproved observable differences.

**Acceptance Scenarios**:

1. **Given** structured entries from multiple levels, categories, and sources, **When** an authorized caller queries recent history with any supported filter combination, **Then** the same matching entries, order, bounds, JSON shape, status, and content type are returned.
2. **Given** an invalid minimum level or take value, **When** recent history is requested, **Then** the current validation status and safe error text are preserved without querying a broader result set.
3. **Given** known structured-log sources, **When** an authorized caller requests source discovery, **Then** the current source collection and JSON representation are returned.
4. **Given** custom configured recent and source paths, **When** the feature is activated, **Then** only those reviewed paths are registered and each has one module owner.

---

### User Story 2 - Tail and resume the live log stream (Priority: P2)

An authorized diagnostics client opens the live structured-log stream, receives committed entries in durable cursor order, survives idle periods through heartbeats, and reconnects from its last event identifier without gaps or duplicate contract changes.

**Why this priority**: The SSE route is the representative streaming proof for the REST consolidation program and carries stricter lifecycle, ordering, cancellation, and backpressure obligations than ordinary request/response routes.

**Independent Test**: Run the current and replacement stream through a real HTTP host while appending local and externally committed entries, exercising initial handoff, resume, invalid and unavailable cursors, idle heartbeat, filtering, polling fallback, cancellation, slow consumers, feed failures, and repeated connect/disconnect cleanup.

**Acceptance Scenarios**:

1. **Given** a new subscriber and entries committed around subscription, **When** the stream starts, **Then** every matching committed entry after the captured durable boundary is emitted once in provider cursor order.
2. **Given** a valid `Last-Event-ID`, **When** a client reconnects, **Then** replay begins strictly after that cursor and each entry frame retains its committed cursor as the SSE `id`.
3. **Given** a malformed, stale, or wrong-binding replay cursor, **When** a stream is requested, **Then** the existing conflict response is returned before an SSE response starts.
4. **Given** an idle stream, **When** no entry arrives during the reviewed heartbeat interval, **Then** the connection receives the existing SSE comment heartbeat without changing the event contract.
5. **Given** a disconnected or slow client, local feed failure, or cancellation during a pending wake, **When** the stream ends or falls back to durable polling, **Then** the request terminates within bounded time, subscriptions and enumerators are released, and the host remains responsive.
6. **Given** custom configured stream and polling settings, **When** the feature is activated and tailed, **Then** the configured route is uniquely registered and non-positive polling values retain their current lower-bound behavior.

---

### User Story 3 - Operate Structured Logs in transitional and dynamic hosts (Priority: P3)

An operator enables the migrated Structured Logs module in a host that still contains legacy-authored Elsa endpoints and observes one explicit owner for all three routes, one shared authorization authority, stable API discovery, and honest unload evidence for every materialized endpoint stage.

**Why this priority**: This work unit is both the streaming proof and the first option-driven migration. It must show that the program pattern remains safe in mixed hosts and cannot claim dynamic-shell readiness while documentation infrastructure retains a collectible assembly.

**Independent Test**: Compose Structured Logs with a representative unmigrated route, inspect route and permission inventories, exercise both authorization paths, generate the actual API document, release route/service/stream/documentation owners, and run bounded weak-reference verification with retaining-root diagnostics.

**Acceptance Scenarios**:

1. **Given** a mixed host, **When** routes and API descriptions are produced, **Then** each Structured Logs route appears exactly once with its module owner, authoring disposition, configured path, and required permission.
2. **Given** exact diagnostics permission, retained administrative wildcard, missing permission, an anonymous caller, or an untrusted principal, **When** callers use replacement and unmigrated routes, **Then** both surfaces use the same Foundation authorization services and produce the required challenge or forbidden outcomes.
3. **Given** endpoint, service, stream, serializer, and API-document owners have been released, **When** collectible verification runs repeatedly, **Then** the module context is collected or the retaining stage and root are identified without a false unload-safety claim.
4. **Given** actual API-document generation retains the collectible module assembly, **When** the migration is evaluated, **Then** the retaining root is mitigated or documentation generation is kept outside the collectible shell boundary before unload safety is reported as satisfied.
5. **Given** a new Structured Logs permission-protected route without unique route, permission, or security ownership, **When** repository gates run, **Then** the change fails with an owner-readable diagnostic.

### Edge Cases

- Route paths are option-driven; empty, duplicate, equivalent, or conflicting configured templates must not silently publish ambiguous endpoints.
- Query values can be missing, blank, repeated, differently cased, negative, non-numeric, or outside the defined log-level domain.
- `take = 0` is valid and distinct from an omitted value; configured store bounds remain authoritative.
- A durable store can return an invalid tail cursor, page cursor, entry cursor, or inconsistent `HasMore` page; the stream must fail visibly rather than emit an invalid resume contract.
- A commit can race before or after local feed subscription, arrive from another process with no local wake, or be observed through wake hints out of completion order.
- Local feed enumeration can fail synchronously, fault asynchronously, complete, or remain pending while cancellation and cleanup run.
- The response must not start as `text/event-stream` until query and replay cursor validation and the first durable read succeed.
- Heartbeat, entry, and dropped-event frames must retain exact line endings, blank-line termination, event names, identifiers, and JSON payload representation.
- The existing formatter can represent dropped-entry events, but the durable-tail endpoint currently treats live-feed items only as wake hints and does not emit those drop frames; migration must not accidentally expand the public SSE event set.
- Repeated connect/disconnect and slow-reader cycles must not leak subscriptions, background operations, response writers, or collectible module owners.
- OpenAPI generation may not describe an infinite SSE body precisely; the reviewed document contract and any approved limitation must be explicit and deterministic.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST retain exactly the existing recent, sources, and stream operations, HTTP methods, configurable route behavior, and public route defaults.
- **FR-002**: Recent queries MUST preserve minimum-level, category, source, and take binding, including case handling, blank values, validation, and store-enforced bounds.
- **FR-003**: Recent responses MUST preserve entry ordering, filtering, status, JSON content type, property naming, enum representation, null handling, exception representation, scopes, properties, and committed replay cursors.
- **FR-004**: Source discovery MUST preserve its response status, content type, ordering, and source model representation.
- **FR-005**: Invalid recent or stream filters MUST retain the current status and safe error text and MUST be rejected before the store or live stream performs broader work.
- **FR-006**: The stream MUST preserve `text/event-stream` behavior, response headers, initial flush, heartbeat framing, event names, event identifiers, JSON payloads, and frame termination.
- **FR-007**: A new stream MUST capture a durable tail boundary before relying on the process-local wake subscription and MUST emit payloads only from bounded durable read-after pages.
- **FR-008**: A resumed stream MUST accept the existing opaque `Last-Event-ID` contract and begin strictly after a valid committed cursor.
- **FR-009**: Malformed, unavailable, stale, or wrong-binding replay cursors MUST preserve the existing pre-stream conflict behavior.
- **FR-010**: Stream filtering MUST apply consistently to replayed, polled, and locally signaled activity without using wake-hint payloads as authoritative events.
- **FR-011**: The stream MUST continue polling durable storage when local wake hints fail or complete and MUST observe commits made by other processes within the configured polling interval.
- **FR-012**: Stream cancellation, client disconnect, feed failure, pending wake, and response failure MUST release owned subscriptions, enumerators, timers, and linked cancellation resources within a bounded interval.
- **FR-013**: Existing slow-subscriber and dropped-entry signaling semantics MUST remain unchanged, and the replacement endpoint MUST NOT begin emitting process-local drop frames that the current durable-tail endpoint does not expose.
- **FR-014**: All three routes MUST require the stable `Diagnostics:StructuredLogs` permission and retain the administrative wildcard grant through the shared Foundation permission evaluator.
- **FR-015**: The Structured Logs module MUST uniquely contribute its stable permission to the active permission catalog with stable owner and contributor provenance and no unreviewed implication.
- **FR-016**: Anonymous callers MUST receive an authentication challenge; authenticated callers without permission and untrusted normalized principals MUST be forbidden before any query or stream starts.
- **FR-017**: The module MUST own one explicit registration for each configured Structured Logs route and MUST NOT retain a second legacy registration for any migrated operation.
- **FR-018**: The replacement routes MUST coexist with representative unmigrated first-party routes in one host without changing either surface's observable contract or authorization semantics.
- **FR-019**: Before-and-after route manifests, ordinary HTTP observations, bounded SSE observations, and consumed API descriptions MUST fail on every unapproved route, method, binding, payload, status, header, error, authorization, framing, timing-bound, or documentation difference.
- **FR-020**: Repeated unchanged-surface captures MUST produce byte-stable compatibility evidence after normalizing only reviewed volatile values and separately requiring those values to remain present and valid.
- **FR-021**: Collectibility verification MUST materialize routes, ordinary requests, a live SSE exchange, serialization, and the actual API document before releasing all generation-owned references and performing bounded weak-reference checks.
- **FR-022**: If API-document generation retains the collectible module assembly, the work unit MUST identify the retaining root and either mitigate it or define and prove an API-document boundary that does not retain the collectible shell context.
- **FR-023**: Existing Structured Logs capture, store, replay, persistence-provider, feature-composition, and streaming behavior tests MUST remain green.
- **FR-024**: The production Structured Logs module MUST no longer register legacy-framework endpoints or depend on the legacy endpoint framework or its SSE helper after migration.
- **FR-025**: This work unit MUST remain limited to the Structured Logs API and shared migration evidence required to prove it; OpenTelemetry, other Elsa modules, diagnostics UI, storage redesign, and broad legacy-framework retirement remain separate units.

### Key Entities

- **Structured Log Entry**: A captured diagnostic record with timestamp, level, message, category, source, structured properties, scopes, optional exception information, display sequence, and a committed opaque replay cursor.
- **Structured Log Filter**: The optional minimum level, category, source, and recent-result bound applied consistently to queries and streams.
- **Log Source**: Safe discovery metadata identifying a producer of structured log entries.
- **Replay Cursor**: An opaque, provider-qualified position that identifies a committed entry and can round-trip through SSE `id` and `Last-Event-ID`.
- **Durable Read Page**: A bounded ordered page of committed entries plus its next cursor and continuation state.
- **Stream Frame**: An SSE entry event, dropped-entry signal, or heartbeat comment with exact framing and payload rules.
- **Permission Ownership Record**: The module-owned catalog entry for the stable Structured Logs permission and its explicit implication set.
- **Compatibility Evidence**: The reviewed route inventory, HTTP and bounded-stream observations, consumed API description, and exact approved differences used to prove contract preservation.
- **Unload Evidence**: Weak-reference and retaining-root observations for materialized routes, services, streams, serialization, and API-document generation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All three operations complete before-and-after route, HTTP or bounded-SSE, and consumed API-description comparison with zero unapproved differences.
- **SC-002**: The recent-query matrix passes for omitted, blank, valid, invalid, repeated, zero, negative, mixed-case, and bounded inputs with the same result or error contract.
- **SC-003**: Ten consecutive initial-handoff and resume runs emit the expected committed entry set exactly once and in cursor order, including a commit racing subscription and a commit from a second store writer.
- **SC-004**: Idle streams emit the reviewed heartbeat within its allowed timing window, and cancellation or disconnect releases the subscription and request within a bounded test interval on every repeated cycle.
- **SC-005**: The authorization matrix passes for anonymous, missing permission, exact permission, retained wildcard, adjacent permission, and untrusted principal on all three routes, using the same evaluator as the representative unmigrated route.
- **SC-006**: The stable Structured Logs permission appears exactly once in the active catalog with the Structured Logs module as owner and no unreviewed implication.
- **SC-007**: A mixed host exposes each reviewed Structured Logs and representative unmigrated route exactly once with one owner and one security disposition.
- **SC-008**: Ten consecutive unchanged-surface evidence captures are byte-identical after reviewed volatility normalization.
- **SC-009**: Repeated materialized-route, live-stream, service, serialization, and actual API-document release verification collects the module context or identifies and resolves/bounds the exact retention root before unload safety is claimed.
- **SC-010**: Every existing Structured Logs capture, replay, persistence, composition, and stream test remains green after migration.
- **SC-011**: The final report records compatibility, SSE lifecycle, authorization/catalog, coexistence, collectibility/OpenAPI, and remaining-risk evidence plus a reviewable proceed, revise, or stop recommendation for subsequent migration waves.

## Assumptions

- Current production route registrations and observed wire behavior are authoritative where older design documentation differs.
- The existing opaque replay cursor, durable read-after contract, local wake feed, and storage implementations are not redesigned by this transport migration.
- `Diagnostics:StructuredLogs` is the stable public permission name for this work unit; the administrative wildcard remains a grant only and is not cataloged as a module permission.
- Existing authentication and external-provider claim normalization remain outside the Structured Logs module.
- The process-local feed is a wake optimization only; durable storage is authoritative for streamed payloads and ordering.
- Compatibility differences are rejected by default; intentional protocol redesign requires separate approval and is not inferred from the endpoint-authoring migration.
- The actual API-document retention observed during the Secrets migration is a known dynamic-host risk, not an acceptable test exclusion.
- Structured Logs domain behavior, persistence, diagnostics UI, OpenTelemetry migration, collision publication, and shared stream abstraction design remain out of scope.
