# Feature Specification: Dashboard Widgets and Attention

**Feature Branch**: `[005-dashboard-attention]`

**Created**: 2026-07-13

**Status**: Draft

**Input**: User description: "Replace the low-value sample dashboard with a customizable module-contributed widget surface, add an independent Attention capability presented through a widget, and add useful Workflow portfolio and run-health widgets backed by complete statistics."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See What Requires Attention (Priority: P1)

As a signed-in Studio user, I can see a prioritized list of conditions I am authorized to inspect, so I know what needs investigation without visiting every module separately.

**Why this priority**: The dashboard provides its greatest value when it reduces the time required to discover failures, risks, and expiring resources across domains.

**Independent Test**: Seed workflow failures, expiring secrets, and module diagnostics for the current user, open Dashboard, and verify that the Attention widget shows a bounded, permission-aware, correlated queue with working destination links and snooze controls.

**Acceptance Scenarios**:

1. **Given** multiple authorized domains report attention conditions, **When** the user opens Dashboard, **Then** the Attention widget orders visible conditions by severity and recency and identifies their source domains.
2. **Given** multiple conditions share a known correlation identifier, **When** the widget renders them, **Then** they appear as one expandable cluster while retaining each source.
3. **Given** the user snoozes a condition, **When** Dashboard refreshes before the snooze expires, **Then** the condition is hidden from the active queue and remains available through the Snoozed filter.
4. **Given** a snoozed condition increases in severity or reports a materially new occurrence, **When** Dashboard refreshes, **Then** the condition returns to the active queue.
5. **Given** one attention source fails or times out, **When** other sources succeed, **Then** their items remain visible and the widget reports that attention data is incomplete without presenting the source failure as a domain incident.
6. **Given** no visible conditions require attention, **When** the widget finishes loading, **Then** it remains in the layout and shows a compact all-clear state.

---

### User Story 2 - Monitor Workflow Run Health (Priority: P2)

As a workflow operator, I can review complete workflow-run statistics for a selected date range, so I can understand execution volume, failures, incidents, and trends without inferring health from a partial run list.

**Why this priority**: Operational health is a primary dashboard concern and must remain trustworthy when a tenant has more runs than any one list page can return.

**Independent Test**: Seed more workflow runs than one normal page can display across successful, failed, cancelled, incident-bearing, running, and test-run states, select a date range, and verify that the widget reports complete totals and bucketed trends while excluding test runs by default.

**Acceptance Scenarios**:

1. **Given** production and transient test runs exist, **When** the user opens Workflow Run Health with default settings, **Then** only production runs started in the selected range contribute to range totals.
2. **Given** runs include successful, failed, cancelled, incomplete, and incident-bearing outcomes, **When** statistics load, **Then** run outcome and incident-bearing counts are presented as independent dimensions.
3. **Given** the user selects 24 hours, 7 days, or 30 days, **When** the widget reloads, **Then** totals and time buckets use the selected range and the user's calendar time zone.
4. **Given** more matching runs exist than a normal list page can return, **When** statistics load, **Then** every authorized matching run contributes to the aggregate without loading every run into the browser.
5. **Given** the user enables inclusion of test runs, **When** the widget reloads, **Then** its labels clearly state that test runs are included.

---

### User Story 3 - Understand the Workflow Portfolio (Priority: P3)

As a workflow builder or administrator, I can see the current size and readiness of the workflow portfolio, so I can distinguish active, published, unpublished, and invalid workflow definitions.

**Why this priority**: A structural summary helps teams understand their automation estate and identify authoring work that has not reached a publishable state.

**Independent Test**: Seed active and deleted definitions, definitions with live published references, definitions with newer drafts, and drafts with validation errors, then verify that the widget returns complete overlapping counters with clear definitions.

**Acceptance Scenarios**:

1. **Given** active definitions include both published and never-published workflows, **When** Workflow Portfolio loads, **Then** it shows the total active count and the count with at least one live published reference.
2. **Given** a published definition also has a newer draft, **When** counters render, **Then** it contributes to both published and unpublished-draft counters rather than being forced into one mutually exclusive category.
3. **Given** current drafts contain validation errors, **When** counters render, **Then** the widget shows how many unpublished drafts are invalid without counting individual validation messages as definitions.
4. **Given** deleted definitions exist, **When** the widget uses default settings, **Then** they are excluded from active portfolio counters.
5. **Given** the portfolio exceeds one normal list page, **When** statistics load, **Then** counters represent the complete authorized portfolio without loading every definition into the browser.

---

### User Story 4 - Personalize a Reliable Dashboard (Priority: P4)

As a Studio user, I can arrange, resize, hide, configure, and refresh available widgets, so Dashboard reflects my responsibilities while remaining consistent and accessible.

**Why this priority**: Different users need different summaries, but personalization must not weaken permissions, reliability, or native Studio interaction patterns.

**Independent Test**: Reorder and resize the initial widgets, hide and restore one, change the refresh interval and widget settings, reload and sign in from another session, and verify that authorized preferences persist while unavailable widgets do not leak data.

**Acceptance Scenarios**:

1. **Given** several widgets are available, **When** the user reorders, resizes, or hides them, **Then** the personalized layout persists across reloads and authenticated sessions.
2. **Given** a widget supports only selected semantic sizes, **When** the user changes its size, **Then** Dashboard offers only supported size presets and preserves a usable responsive layout.
3. **Given** the user selects a global refresh interval, **When** a widget declares a slower minimum interval, **Then** the widget uses the slower effective interval and exposes that effective timing.
4. **Given** a widget load fails, times out, or is superseded by a new load, **When** Dashboard handles the result, **Then** only that widget shows an error or retry state and obsolete work is cancelled.
5. **Given** a module is disabled or removed, **When** Dashboard refreshes its available widgets, **Then** the module's widgets and data disappear without leaving layout gaps, while preferences remain recoverable if the module returns.
6. **Given** the user switches tenant or host, **When** Dashboard begins loading the new scope, **Then** no data or preference state from the previous scope is shown, even briefly.

### Edge Cases

- An Attention contributor is unauthorized, unavailable, slow, malformed, or removed while aggregation is running.
- An unfiltered Attention request includes contributors the caller is not authorized to discover.
- More than five conditions are reported by one contributor or more than twenty are visible globally.
- A correlated cluster contains conditions with different severities, destinations, or sensitivity classifications.
- A snooze expires, the underlying condition resolves, or the same stable condition returns with a new generation.
- The canonical preference service is unavailable, authentication is disabled, or local device storage is unavailable.
- Two sessions update the same preference document concurrently.
- Stored widget settings use an older schema version, fail migration, or are no longer valid.
- A widget has no loader, declares an unsupported size, exceeds its timeout, or unloads while loading.
- A new widget appears after the user has personalized Dashboard; an unknown stored widget ID has no currently loaded module.
- Workflow statistics span a daylight-saving transition or use a date range with no matching runs.
- Workflow statistics include retained historical data whose source definitions have since been deleted.
- A third-party persistence provider has no Dashboard aggregate adapter, while every officially supported provider must have one before the feature is considered supported on that provider.

## Requirements *(mandatory)*

### Functional Requirements

#### Dashboard Widgets

- **FR-001**: Dashboard MUST remain a module-contributed widget surface in which a module may contribute zero, one, or multiple widgets.
- **FR-002**: Dashboard MUST NOT reserve placement, cardinality, or screen space based on module identity.
- **FR-003**: Dashboard MUST own each widget's frame, title, accessibility semantics, loading, empty, error, timeout, retry, refresh, and layout controls.
- **FR-004**: A Dashboard Widget MUST provide a stable module-prefixed identity, title, default order, default visibility, default semantic size, and supported semantic sizes.
- **FR-005**: Supported widget sizes MUST use host-defined semantic presets equivalent to small, medium, wide, and full rather than fixed pixel dimensions.
- **FR-006**: A Dashboard Widget MAY provide a cancellable data loader; widgets without loaders MUST remain valid and MUST NOT participate in managed refresh behavior.
- **FR-007**: Dashboard MUST load visible data-backed widgets independently so one failure or timeout does not block other widgets, and every widget endpoint MUST independently enforce authorization and tenant scope.
- **FR-008**: Dashboard MUST provide global refresh and per-widget refresh or retry controls and MUST cancel superseded loads.
- **FR-009**: Users MUST be able to select Off, 1 minute, 5 minutes, 15 minutes, or 30 minutes as the global refresh interval.
- **FR-010**: The default global refresh interval MUST be 5 minutes and MUST be host-configurable.
- **FR-011**: A widget MAY declare a minimum refresh interval and cache lifetime; Dashboard MUST use the slower of the user-selected interval and the widget minimum and MUST expose the effective timing.
- **FR-012**: The initial Attention and Workflow Run Health widgets MUST allow refresh no faster than once per minute, and Workflow Portfolio MUST allow refresh no faster than once per 5 minutes.
- **FR-013**: Widget loads MUST use a host-configurable timeout defaulting to 10 seconds, and failure MUST remain isolated and retryable.
- **FR-014**: Users MUST be able to reorder, hide, restore, and resize widgets within each widget's supported sizes.
- **FR-015**: Dashboard MUST retain preferences for temporarily unknown widget identities and restore them if the widget returns.
- **FR-016**: Newly discovered widgets MUST use their declared default placement unless the user has chosen not to add new widgets automatically.
- **FR-017**: A configurable widget MUST declare versioned settings, defaults, validation behavior, and an optional migration; failed or unavailable migration MUST reset only that widget to defaults, show a one-time notice, and prevent invalid settings from reaching its loader.
- **FR-018**: Widget settings controls MUST reuse native Studio setting descriptors and editors while persisting through Dashboard Preferences rather than administrative domain-setting mutation paths; a custom setting editor MAY be used only when shared editors cannot express the setting.
- **FR-019**: Dashboard MUST omit widgets the user is not authorized to view before invoking their loaders, and stored layout or setting state MUST NOT reveal unauthorized widget identities.
- **FR-020**: The current self-framed Dashboard Widget contribution shape MUST be replaced without a legacy compatibility path.
- **FR-021**: The existing sample Dashboard module and its hard-coded demonstration widgets MUST be removed.

#### Attention

- **FR-022**: Attention MUST be an independent capability whose normalized items and contributor model do not depend on Dashboard.
- **FR-023**: Domains MUST contribute Attention behavior through dedicated integration modules rather than embedding Attention dependencies in domain modules.
- **FR-024**: An Attention contributor MUST evaluate its complete authorized dataset before claiming that no condition requires attention.
- **FR-025**: Attention aggregation MUST support querying all authorized contributors or selected contributor identities through a consistent contract.
- **FR-026**: Contributors selected for one query MUST execute independently and return one explicit result envelope per selected contributor, including empty success, forbidden, unavailable, timed-out, and failed outcomes.
- **FR-027**: An unfiltered Attention query MUST omit contributors the caller is not authorized to discover; an explicitly requested unauthorized contributor MAY return a forbidden result without domain data.
- **FR-028**: Each Attention Item MUST include a stable contributor-scoped identity, occurrence generation, normalized severity, concise title, optional sanitized summary, occurrence and observation times, count, destination, typed correlation identifiers, and sensitivity classification.
- **FR-029**: Attention Items MUST NOT contain secrets, workflow payloads, raw log properties, stack traces, telemetry attributes, credentials, or domain mutation commands.
- **FR-030**: Each contributor MUST return no more than five items and MUST provide aggregate count and truncation information when additional conditions exist.
- **FR-031**: The Attention widget MUST display no more than five active member items in wide mode and no more than twenty active member items in full mode before presentation grouping; correlation clusters MUST NOT bypass these bounds.
- **FR-032**: Attention MUST order active items by normalized severity and then recency and MUST group items only when typed correlation identifiers match.
- **FR-033**: Users MUST be able to snooze a stable condition for 1 hour, 1 day, or 1 week and inspect snoozed conditions.
- **FR-034**: A severity increase or materially new occurrence generation MUST override an active snooze; snoozing a correlation cluster MUST apply to its current member conditions.
- **FR-035**: The Attention widget MUST support inspect/navigation, refresh, filtering, and snoozing but MUST NOT perform domain mutations.
- **FR-036**: The initial Attention contributors MUST cover workflow runtime failures or incidents; expired, revoked, or soon-expiring secrets according to host-configured horizons; and failed, incompatible, or diagnostic-bearing modules.
- **FR-037**: The Attention widget MUST remain an ordinary Dashboard Widget that users can move, resize, or hide unless Host Policy explicitly pins it.
- **FR-038**: When no visible active condition exists, the Attention widget MUST remain visible by default and show a compact all-clear state.

#### Studio Preferences

- **FR-039**: Studio MUST provide a governed preference capability shared by Dashboard, Attention, and future Studio modules.
- **FR-040**: Preference documents MUST belong to registered namespaces and MUST be bounded, versioned, and scoped by authenticated user, tenant, and Studio host identity.
- **FR-041**: Preference owners MUST define defaults, validation, migration, and semantics; the preference capability MUST own persistence, quotas, revision handling, and timestamps.
- **FR-042**: Concurrent preference updates MUST use optimistic concurrency so one session cannot silently overwrite another session's newer document.
- **FR-043**: The backend MUST be the canonical durable preference authority for authenticated users.
- **FR-044**: When canonical preferences are unavailable or authentication is disabled, Dashboard MAY use device-local fallback and MUST treat it as device-local rather than synchronized state.
- **FR-045**: Preference documents MUST NOT store secrets, credentials, workflow payloads, or other protected domain data.
- **FR-046**: Dashboard layout, visibility, sizes, refresh interval, and widget settings MUST use a Dashboard preference namespace; Attention snoozes MUST use an Attention preference namespace.

#### Initial Workflow Widgets

- **FR-047**: Workflow Portfolio MUST report complete authorized counts for active definitions, definitions with at least one live published reference, definitions with unpublished drafts, and current drafts with validation errors.
- **FR-048**: Workflow Portfolio counters MUST be independent and MUST NOT imply that published, unpublished-draft, and invalid-draft states are mutually exclusive.
- **FR-049**: Deleted definitions MUST be excluded from Workflow Portfolio counters by default.
- **FR-050**: Workflow Run Health MUST support 24-hour, 7-day, and 30-day ranges and default to 7 days.
- **FR-051**: Workflow Run Health MUST filter from an inclusive UTC start instant to an exclusive UTC end instant and MUST calculate requested hourly or daily bucket boundaries in the supplied user calendar time zone, including daylight-saving transitions.
- **FR-052**: Workflow Run Health MUST report runs started in the selected range, normalized outcomes, incident-bearing runs, total incidents, and currently running workflows.
- **FR-053**: Run outcome and incident-bearing status MUST remain independent so a recovered successful run may still be counted as incident-bearing.
- **FR-054**: Transient test runs MUST be excluded by default and MAY be included through an explicit widget setting with visible labeling.
- **FR-055**: Workflow statistics MUST be computed over every authorized matching record and MUST NOT infer totals or all-clear state from a paginated sample.
- **FR-056**: Workflow statistics MUST be available consistently across every officially supported persistence provider without loading the full matching dataset into browser or application memory.
- **FR-057**: A third-party or otherwise unsupported persistence provider without a complete aggregate adapter MUST report explicit unavailability rather than return sampled or partial statistics as complete; officially supported providers MUST supply the adapter.

#### Cross-Cutting Behavior

- **FR-058**: Each Attention contributor MUST define documented attention thresholds, and Host Policy MAY override exposed thresholds; individual users MUST control refresh and snooze but MUST NOT redefine domain detection rules in the first version.
- **FR-059**: Attention contributor evaluation MUST use a host-configurable timeout defaulting to 5 seconds and MUST cancel work that is superseded, deselected, unloaded, or scoped to a previous tenant or host.
- **FR-060**: The Attention widget MUST provide All, Critical, and Snoozed filters.
- **FR-061**: Dynamically registering an authorized Attention contributor with a stable module-prefixed identity MUST make it available on the next refresh; unloading it MUST cancel its work and remove its visible items while retaining applicable snooze preferences if it returns.
- **FR-062**: Each data-backed widget frame MUST expose when its snapshot was last updated and when its next automatic refresh is expected when automatic refresh is enabled.
- **FR-063**: The initial default layout MUST place Attention first at wide size, Workflow Portfolio second at small size, and Workflow Run Health third at wide size; Attention MUST support wide and full, Portfolio small and medium, and Run Health medium, wide, and full.
- **FR-064**: Workflow Run Health MUST show failure and incident-bearing percentages plus a time-series breakdown and the highest-failure workflow definitions for the selected range.
- **FR-065**: The first version MUST ship exactly three first-party Dashboard Widgets: Attention, Workflow Portfolio, and Workflow Run Health; demonstration weather and module-mechanics widgets are out of scope.
- **FR-066**: Attention contributors MUST use bounded aggregate queries, MUST NOT fan out one request per entity, and MUST perform no more than two downstream service requests per contributor refresh unless Host Policy explicitly grants a higher budget.
- **FR-067**: Backend authorization and tenant isolation MUST remain authoritative for Attention contributors, and Host Policy MUST be able to suppress restricted summaries while preserving safe metadata-only states.
- **FR-068**: An Attention aggregation request MUST succeed as a partial result when individual contributors fail; request authentication failures, malformed filters, or failure of aggregation itself MUST remain request-level errors.
- **FR-069**: Dashboard MUST NOT create hidden polling, live streams, or automatic refresh while its surface is inactive; manual refresh MUST remain available regardless of minimum automatic intervals.
- **FR-070**: Host Policy MAY force any widget visible or pinned and MUST override personal hiding; modules themselves MUST NOT force visibility.
- **FR-071**: The shared Attention aggregation behavior MUST be independently hostable by Studio and backend hosts, and Studio MUST NOT become a semantic proxy that owns backend-domain detection rules.
- **FR-072**: Workflow Portfolio and Workflow Run Health MUST use independently loadable snapshot resources so cost, permission, refresh, and failure remain isolated.
- **FR-073**: A resolved Attention condition MUST disappear naturally; a returning condition with the same identity MUST remain snoozed only when neither its severity nor occurrence generation invalidates the snooze.
- **FR-074**: Incomplete Attention data MUST produce one compact warning naming failed contributors, with retry and optional diagnostic destinations, and MUST NOT create synthetic domain incidents.

## Delivery Constraints

- The feature is delivered as three independently testable slices: Dashboard Widgets and Studio Preferences; Attention and its initial integrations; and Workflow Dashboard statistics with the two Workflow widgets.
- Deferred work is recorded as separate issue seeds with data source, attention or widget criteria, backend gaps, permissions, correlation needs, and acceptance criteria.

### Key Entities

- **Dashboard**: The Studio surface that discovers and arranges available Dashboard Widgets and owns their shared framing and lifecycle.
- **Dashboard Widget**: A module contribution containing stable metadata, supported sizes, optional managed loading, optional versioned settings, and a body presentation.
- **Widget Snapshot**: The bounded result of a managed widget load for the current user, tenant, host, and settings.
- **Widget Preference**: Personal layout, visibility, size, refresh, or validated widget-setting state stored under the Dashboard namespace.
- **Attention Item**: A normalized metadata-only description of one condition that may require investigation.
- **Attention Contributor**: A domain integration that evaluates complete authorized state and produces bounded Attention Items.
- **Attention Contributor Result**: A success or failure envelope for one contributor evaluation, preserving partial success across contributors.
- **Correlation Identifier**: A typed identifier that permits related Attention Items from different domains to be grouped without guessing.
- **Snooze**: A user-scoped suppression of one stable Attention condition or current correlation cluster until a time, severity increase, or new occurrence generation breaks it.
- **Preference Document**: A bounded versioned document in a registered namespace with scope, revision, and update time.
- **Workflow Portfolio Snapshot**: Complete overlapping counters describing active, published, unpublished-draft, and invalid-draft workflow definitions.
- **Workflow Run Health Snapshot**: Complete time-bounded outcomes, incidents, running state, and trend buckets for authorized workflow runs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can identify the highest-severity visible attention condition and open its owning domain surface in no more than two interactions from Dashboard.
- **SC-002**: When one Attention contributor or Dashboard Widget fails, 100% of successful independent contributors and widgets remain usable.
- **SC-003**: Automated scenarios with matching records beyond the first normal list page produce exact Attention and Workflow aggregate totals with no sampled all-clear results.
- **SC-004**: 100% of tested unauthorized contributors and widgets issue no domain-data request and reveal no protected summary data.
- **SC-005**: Reordering, hiding, resizing, refresh selection, widget settings, and snoozes persist across reloads and a second authenticated session without cross-user, cross-tenant, or cross-host leakage.
- **SC-006**: A severity increase or materially new occurrence makes a snoozed condition active on the next successful refresh in 100% of escalation tests.
- **SC-007**: Visible widget refresh, timeout, cancellation, loading, empty, error, retry, and effective-interval states are keyboard operable and screen-reader identifiable.
- **SC-008**: Workflow Portfolio and Run Health remain accurate for every supported persistence provider in seeded acceptance datasets.
- **SC-009**: Users can configure and understand the three initial widgets at their supported small, medium, wide, or full responsive layouts without clipped controls or overlapping content at supported viewport sizes.
- **SC-010**: The shipped Dashboard contains no hard-coded module-load, route-ownership, endpoint-count, UI-kit, or weather demonstration widget.

## Assumptions

- Existing Studio authentication, tenant resolution, endpoint contexts, permissions, Host Policy, setting editors, and module lifecycle are reused.
- Dashboard is a dedicated Studio capability composed by the host rather than remaining an implementation detail of the web shell.
- Attention and Workflow aggregate work requires coordinated backend changes outside this Studio repository.
- A live published reference is the authority for whether a workflow definition is published; historical versions alone do not make it published.
- Current draft validation errors are the authority for whether an unpublished draft is invalid.
- Run-range totals use run start time; currently running workflows are shown separately from completed range outcomes.
- Device-local preference fallback is best-effort and is not expected to synchronize across devices or browsers.
- Shared chart primitives and mandatory generated/runtime client validation are not required for the first version.
- Attention has no dedicated routed page and no global navigation badge in the first version.

## Deferred Follow-Up Issue Seeds

- Add a Structured Logs Attention integration for recent Error/Critical conditions without opening a dashboard-only live stream.
- Add an OpenTelemetry Attention integration for failing or stale traces and instrumentation coverage.
- Add a Console Attention integration for unhealthy or stale console sources.
- Add a Feature Management Attention integration for manifest errors and actionable feature posture problems.
- Add an Extension Builder Attention integration for failed builds, failed reconciliation, and repositories needing action.
- Add a Workflow Performance widget for median and high-percentile duration plus the slowest workflow definitions.
- Add a Secrets lifecycle and inventory Dashboard Widget distinct from actionable secret Attention Items.
- Add a Feature posture and Studio/Server drift Dashboard Widget.
- Add a Module and runtime health Dashboard Widget distinct from actionable module Attention Items.
- Add OpenTelemetry error and latency trend widgets.
- Add a Structured Logs error-trend widget.
- Add a Console source-health Dashboard Widget.
- Add an Extension Builder repository-health Dashboard Widget.
- Add shared token-aware and accessible chart primitives after first-party widget needs are proven.
- Evaluate shared runtime contract validation or generated clients to reduce repeated cross-language boundary parsing.
- Add an optional global Attention navigation badge only when a trustworthy background aggregate is available without hidden dashboard polling.
- Add a dedicated Attention route only if queue management outgrows wide and full widget modes.
