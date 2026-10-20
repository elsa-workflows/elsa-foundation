# Feature Specification: Authenticated Structured Logs Streaming

**Feature Branch**: `agent/codex/3132-stream-credentials`

**Created**: 2026-10-20

**Status**: Implemented

**Input**: Deliver the remaining Studio UX epic #3057. Correct reproduced defect #3132: the standard signed-in Structured Logs page can read recent entries but cannot open its live stream when Studio and Workbench use the existing separate origins.

## User Scenarios & Testing

### User Story 1 — Open authorized live logs (Priority: P1)

An operator signed in to the normal Studio app opens Structured logs and sees its actual live connection state without another sign-in or manual browser workaround.

**Why this priority**: The current everyday task fails even though the same operator can read recent entries. A valid empty log inventory must not be confused with authentication failure.

**Independent Test**: Use the existing normal app with a disposable operator session and separate Studio/Workbench origins. The actual product stream opens successfully and the page reaches Live. Recent/source reads remain successful, including when no entries are present.

**Acceptance Scenarios**:

1. **Given** an operator authorized for Structured logs and signed in to the normal app, **when** they open its page across the existing separate origins, **then** the live stream uses that browser session and reaches Live without exposing credentials.
2. **Given** the same authorized composition has no entries, **when** the stream opens, **then** the page can show a live, empty inventory without reporting an authentication failure.
3. **Given** an anonymous or insufficiently authorized principal in an authenticated deployment, **when** they request the stream, **then** the existing denial remains and no log events are disclosed.

### User Story 2 — Keep the existing log-reading workflow (Priority: P2)

An operator changes filters, pauses and resumes display, reconnects after a transient interruption, and leaves the page with the same established behavior.

**Why this priority**: Correcting authentication must preserve the usable streaming controls and resource lifecycle.

**Independent Test**: Exercise existing page behavior with deterministic log events and connection transitions; verify filtering, pause/buffer behavior, retry state and teardown. Retain existing server authorization/filter/replay tests as separate evidence.

**Acceptance Scenarios**:

1. **Given** an active stream, **when** level/source/category filters change, **then** the prior subscription closes and the replacement retains both the selected filters and the authorized browser session.
2. **Given** a transient interruption, **when** the existing reconnect behavior resumes, **then** the replacement request remains authenticated without introducing duplicate client subscriptions.
3. **Given** the page is paused or is being left, **when** new entries arrive or the page unmounts, **then** its existing buffer/display and subscription cleanup behavior remains unchanged.

### Edge Cases

- Same-origin and separate-origin backend addresses retain their existing URL/filter semantics.
- An empty successful stream remains distinct from permission denial, setup unavailability, connection error and loading.
- Changing filters or leaving the page ends its previous stream; retrying after an interruption must not leave duplicate streams active.
- Credentials must never be added to a stream URL, recorded request summary or persisted evidence.
- Anonymous/demo compositions preserve their existing access policy; browser credential handling does not grant a missing permission.

## Requirements

### Functional Requirements

- **FR-001**: The existing Structured Logs page MUST use its authorized browser session when opening the live stream across the normal Studio/Workbench origins.
- **FR-002**: The page MUST reach its existing Live state after a successful stream connection, including an empty inventory, without changing recent/source reads or their state classification.
- **FR-003**: Existing backend permission and cross-origin access decisions MUST remain authoritative; denied principals MUST remain denied and receive no stream data.
- **FR-004**: Existing filter serialization, pause/buffer, error/reconnect and subscription cleanup behavior MUST remain unchanged.
- **FR-005**: Stream authentication MUST NOT put a token or secret into a URL, new persisted setting or diagnostic evidence.
- **FR-006**: Qualification MUST retain the authenticated before failure, establish a regression that fails without the correction, and prove the rebuilt actual product stream succeeds under the same normal-host authorization context. A constructor assertion alone is insufficient.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The previously failing normal signed-in operator journey reaches Live through the product page in the existing separate-origin composition; its recent/source reads still succeed.
- **SC-002**: Existing denied-principal cases disclose zero log events and preserve their denial outcomes.
- **SC-003**: All affected filter, pause/buffer, reconnect and cleanup regressions pass; filter replacement and unmount leave no extra page-owned subscription.
- **SC-004**: The reviewed request summaries contain no credential values; required review, exact-revision checks and resulting-main qualification complete before the defect is accepted.

## Assumptions

- Scope is the browser-managed cookie session already used by the reproduced normal Development host. This work does not promise bearer-only stream support or change the authentication model.
- The existing stream endpoint, permission and cross-origin policies support the normal signed-in session, as demonstrated by the retained credentialed browser probe. Their correction is not part of this client defect.
- Existing anonymous/demo behavior is preserved; no additional provider, package or module is enabled.
- Related backend permission cleanup #2827 and trace API alignment #3131 remain separate. PR3133's Diagnostics copy/index work is integrated before final combined qualification; this issue does not duplicate it.
- Use fresh disposable runtime databases and browser contexts. Production deployment, telemetry ingestion changes and a new streaming transport are excluded.
