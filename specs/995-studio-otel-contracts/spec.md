# Feature Specification: Studio OpenTelemetry Trace Inspection

**Feature Branch**: `agent/codex/3131-otel-contracts`

**Created**: 2026-10-21

**Status**: Approved

**Input**: Issue #3131 — Studio diagnostics: align trace reads with the existing OpenTelemetry API

## User Scenarios & Testing

### User Story 1 - Find and inspect a workflow trace (Priority: P1)

An operator with existing diagnostics read access opens Diagnostics → OpenTelemetry, finds a workflow trace using the supported filters, and opens it to inspect its spans and service identities.

**Why this priority**: The trace page currently cannot read the data available from the normal Workbench, so its primary troubleshooting task is blocked.

**Independent Test**: Validate trace summaries, nested spans, resource relationships, status values, durations, and filters against representative data matching the host's actual shape. Then execute a disposable workflow in the normal Workbench and use Studio to find and inspect its actual trace.

**Acceptance Scenarios**:

1. **Given** a disposable workflow has produced a trace in the normal Workbench and the operator has diagnostics read access, **When** the operator opens the trace view, **Then** the trace appears with its actual identifier, name when supplied, start time, duration, span count, status, and associated service identity.
2. **Given** the trace is listed, **When** the operator selects it, **Then** the detail view shows the trace and its spans with parent-child relationships, timing, status, available attributes and events, and service metadata resolved through the resources associated with the trace.
3. **Given** the operator filters by trace ID, service, status, result limit, or the Trace name or ID filter, **When** matching data exists, **Then** each returned row matches the selected filters and the result limit is respected. Status choices reflect Unset, Ok, and Error.
4. **Given** a trace refers to multiple resources or a resource cannot be resolved, **When** the trace or a span is displayed, **Then** the page makes multiple service identities available or explicitly identifies the service as unknown instead of guessing.

---

### User Story 2 - Understand the stored diagnostic data (Priority: P2)

An operator reviews the OpenTelemetry overview to understand which diagnostic resources and spans are currently stored and which services reported them.

**Why this priority**: The overview helps an operator understand the data available for trace investigation, but it is secondary to opening a real trace.

**Independent Test**: Provide stored resource and span data, open the overview, and compare every displayed count and service detail with the values reported by the host.

**Acceptance Scenarios**:

1. **Given** the host reports stored resources and spans, **When** the operator opens the overview, **Then** the page labels those values as stored data and lists only resource details that the host actually provides.
2. **Given** a trace search is limited or the host reports dropped data, **When** results are displayed, **Then** the returned rows or dropped-data information are not presented as a total count of all traces produced.
3. **Given** the host cannot provide an overall trace total with consistent meaning, **When** the overview is displayed, **Then** it omits an unqualified trace-total card rather than presenting a provider-dependent value as a universal total or health measure.

---

### User Story 3 - Distinguish empty, denied, and unavailable states (Priority: P2)

An operator can tell whether trace data is still loading, the search completed with no matches, access was denied, the diagnostics surface is missing, or a request failed.

**Why this priority**: Clear states prevent an operator from treating missing access or a service problem as an empty store or a healthy system.

**Independent Test**: Open the page with separate successful-empty, denied, missing-surface, failing, and partial-overview outcomes; verify that each has its own explanation and does not erase independently available data. Test denial with a principal that lacks diagnostics read access.

**Acceptance Scenarios**:

1. **Given** a trace request is in progress, **When** results have not arrived, **Then** the page shows a loading state rather than an empty result.
2. **Given** a trace request succeeds with no matching items, **When** the response is rendered, **Then** the page explains that no traces match the current filters.
3. **Given** the operator lacks diagnostics read access, **When** the page requests trace or overview data, **Then** it reports permission denial and does not present the result as empty or healthy.
4. **Given** the diagnostics surface is not available, **When** the page cannot load it, **Then** the page identifies that capability as unavailable; other request failures have a separate failure state.
5. **Given** one overview source succeeds while the other fails, **When** the overview is rendered, **Then** the available portion remains visible and the failed portion reports its own state.
6. **Given** Metrics and OpenTelemetry Logs are unsupported in this experience, **When** an operator enters or uses the OpenTelemetry area, **Then** those limits remain explicit and the existing neutral handoff to Diagnostics still works.

## Edge Cases

- A successful result can contain no matching items. It remains distinct from loading, denial, unavailable diagnostics, and request failure.
- A selected trace can disappear before its detail is opened. The page identifies the missing trace without describing the entire diagnostics surface as unavailable.
- Trace names and resource service names can be absent. The page uses the trace ID as an available identifier and marks an unresolved service as unknown; it does not invent an operation name, environment, or service label.
- A trace can span multiple resources and services. Service identity remains associated with the resource IDs supplied for the trace and its spans.
- Trace status describes the trace summary and is not an error-span count. A missing error count is not replaced with a guessed value.
- Trace and span durations may be represented differently by the host. The displayed elapsed values remain consistent across the list and detail views.
- Storage and resource data can fail independently. A failed read is not converted into a zero count or a no-resources message.
- A result limit or dropped-data count can make a displayed result incomplete. A bounded result is not labelled as a total.

## Requirements

### Functional Requirements

- **FR-001**: The OpenTelemetry trace view MUST let a signed-in operator with existing diagnostics read access search available traces and inspect a selected trace.
- **FR-002**: The trace view MUST support filtering by trace ID, service, status, and result limit, plus a text field labelled “Trace name or ID” that searches the trace name or ID.
- **FR-003**: The trace view MUST present the supported status values Unset, Ok, and Error and MUST NOT infer an error count from the trace summary status.
- **FR-004**: Trace rows MUST present the trace identifier, name when provided, start time, elapsed duration, span count, status, and service identity when it can be resolved from associated resources.
- **FR-005**: Trace detail MUST present the trace summary and its spans, including available span identifiers, parent-child relationships, timing, status, attributes, events, and the service/resource relationship.
- **FR-006**: When a trace is associated with multiple services, the page MUST preserve those identities and resolve each span's service through that span's resource relationship; when a service resource cannot be resolved, the page MUST identify that service as unknown.
- **FR-007**: Trace and span durations MUST represent the elapsed time supplied by the host and be formatted consistently in the trace list, detail header, and span view, using the existing millisecond and second presentation.
- **FR-008**: The overview MUST label host-reported resource and span values as stored-data counts. It MUST omit an unqualified trace-total count and MUST NOT present stored counts as service health.
- **FR-009**: The overview MUST show resource service details only when supplied by the host; absent optional metadata MUST remain absent or explicitly unknown. If resource status is shown, its values Unknown, Active, and Stale MUST be identified as resource status rather than overall system health.
- **FR-010**: Trace and overview retrieval MUST distinguish loading, successful empty results, permission denial, unavailable diagnostics, and other request failures.
- **FR-011**: Storage-count and resource-list outcomes MUST be represented independently so one successful read remains usable when the other fails.
- **FR-012**: Unsupported Metrics and OpenTelemetry Logs MUST be clearly identified as unsupported, and the existing Diagnostics handoff MUST remain available.
- **FR-013**: The feature MUST use the existing sign-in and diagnostics read-access rules without broadening access or adding a new path for credentials or data collection.

### Key Entities

- **Trace**: A distributed operation identified by a trace ID, with an optional name, start and end times, duration, status, span count, and relationships to workflow executions and resources.
- **Span**: A timed operation within a trace, with its own identity, optional parent, status, attributes, events, and resource relationship.
- **Resource**: Service identity and optional metadata associated with traces and spans by resource ID. A trace can refer to more than one resource.
- **Storage summary**: A host-reported view of currently stored diagnostic data and any reported dropped data. It describes stored data, not service health or a universal total of produced traces.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the normal Workbench, an operator with existing diagnostics read access can find a disposable workflow's actual trace and inspect its returned spans in one Studio visit.
- **SC-002**: For every supported trace filter, the visible rows match the filter and stay within the chosen result limit; the page never labels the limited rows as a total.
- **SC-003**: For representative traces containing Unset, Ok, and Error statuses, resources with Unknown, Active, and Stale statuses when shown, and multiple resources, displayed identifiers, status, service identity, and duration agree with the host data to the precision shown.
- **SC-004**: Loading, successful empty results, permission denial, unavailable diagnostics, request failure, and independent partial-overview failure each have a distinct visible state.
- **SC-005**: No failed or unavailable summary is shown as zero or healthy, and no trace error count is fabricated from summary status.
- **SC-006**: Metrics and OpenTelemetry Logs are identified as unsupported, and the Diagnostics handoff remains reachable.

## Assumptions

- The normal Workbench already produces workflow traces through its existing composition, and its trace, resource, and storage information is sufficient for this correction. No new diagnostic capability or telemetry collection is in scope.
- Operators continue to use the existing sign-in and diagnostics read-access rules.
- Trace data may become available after a workflow completes. A successful empty result means no matching data is currently available and is not itself a failure.
- Resource metadata can be incomplete or span several services, so the page must report only what is available.
- Metrics and OpenTelemetry Logs remain outside the supported Studio experience even when the host stores those signal types.
- No new timing, throughput, or performance target is introduced by this correction.

## Root Specification Review

Root reviewed the full specification against issue #3131 and its existing-contract readiness decisions. All three issue acceptance criteria remain covered, including actual engine-produced workflow trace inspection, independent state/permission handling, and preservation of the existing sign-in/read-access boundary. FR-006 was clarified to preserve multiple services at trace level while resolving each span through its own resource. The 16-item requirements checklist passes with no clarification markers. Approved for implementation planning only; product edits require a separately reviewed plan and task checkpoint. No runtime or delivery acceptance is implied.
