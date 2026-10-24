# Feature Specification: Task-first Runs triage in Studio

**Feature Branch**: `agent/codex/3062-runs-golden-path`
**Created**: 2026-10-20
**Status**: Implemented
**Issue**: #3062
**Program**: Epic #3057 · Program #2559 · Phase 2
**Input**: User description from #3062: “Make Runs a task-first triage list with progressive filters.”

## User Scenarios & Testing

### User Story 1 - Find runs that need attention or are running (Priority: P1)

An operator opens Runs to find executions with active incidents or executions that are currently running. The primary choices answer these two different questions. The operator can also find runs by choosing the intended workflow from human-readable names.

**Why this priority**: Triage is the core day-to-day use of the Runs list. It must reveal relevant executions without changing incident meaning or lifecycle truth.

**Independent Test**: With blocking, active nonblocking, healthy, resolved-only, and suppressed-only runs on multiple pages, apply each primary choice alone and together. Verify the results, lifecycle labels, and workflow selection.

**Acceptance Scenarios**:

1. **Given** runs with active blocking and active nonblocking incidents across different lifecycle statuses, **When** the operator selects “Needs attention”, **Then** the list includes both kinds of active incidents and excludes healthy, resolved-only, and suppressed-only runs across pages.
2. **Given** healthy and active runs with Running lifecycle status, **When** the operator selects “Running”, **Then** the list includes both; incident health remains independently visible.
3. **Given** both primary choices are selected, **When** the list reloads, **Then** each result satisfies both criteria.
4. **Given** the operator searches for a workflow by its human name, **When** they choose a result, **Then** only runs for that exact workflow are shown. Duplicate names are distinguishable by stable identity or version.
5. **Given** the host does not support incident-health filtering, **When** the operator selects “Needs attention”, **Then** Studio explains that the filter is unavailable rather than presenting an empty result.

### User Story 2 - Reveal detailed filters only when needed (Priority: P2)

An operator starts with the primary triage choices and workflow-name discovery. They open More filters to enter exact execution, artifact, or correlation identities, detailed dates, and other supported criteria. Advanced edits are applied deliberately. Visible chips describe applied criteria, not draft input.

**Why this priority**: Routine triage should be quick to scan, while exact identifiers and date boundaries remain available for investigation.

**Independent Test**: Apply two advanced criteria, remove one applied chip, and verify the other remains active. Edit another field without applying, collapse and reopen More filters, and verify that draft remains.

**Acceptance Scenarios**:

1. **Given** an advanced filter draft, **When** the operator edits the field, **Then** the applied list and chips remain unchanged until Apply.
2. **Given** the operator applies advanced criteria, **When** the list reloads, **Then** the criteria are applied together and shown as removable chips.
3. **Given** multiple applied chips, **When** the operator removes one, **Then** only that criterion is removed and the list returns to its first page.
4. **Given** applied criteria and an unfinished draft, **When** More filters is collapsed and reopened, **Then** the applied summary remains visible and the unfinished draft is preserved.
5. **Given** the operator selects Reset or Clear all, **When** the list reloads, **Then** applied and draft criteria are cleared while the selected page size is preserved.
6. **Given** the operator changes a primary choice while advanced fields contain unapplied drafts, **When** the list updates, **Then** unrelated drafts remain intact and the current page and selection are reset.

### User Story 3 - Read a run row and take selection actions (Priority: P2)

An operator scans each run by its human workflow name first, then inspects its exact execution and artifact identities, workflow definition/version, lifecycle, incident health, and dates. Exact identifiers can be copied. Bulk actions appear only after selecting a run and remain subject to existing capability and permission checks.

**Why this priority**: Clear row identity reduces the effort of finding a workflow while preserving the exact values needed for diagnosis and support.

**Independent Test**: Render long and duplicate workflow names with exact run, artifact, definition, and version identities. Verify the name-first hierarchy, identifier copying, keyboard inspection, and gated selection actions.

**Acceptance Scenarios**:

1. **Given** a run with a resolved workflow name, **When** its row is displayed, **Then** the human name is the primary identity and the exact execution, artifact, definition, and version identifiers remain inspectable and copyable.
2. **Given** two definitions with the same display name, **When** their runs are displayed or selected in workflow search, **Then** their stable identities or versions distinguish them.
3. **Given** a keyboard user focuses a run row, **When** they activate its inspection gesture, **Then** the same run opens; row selection and per-run actions retain their distinct behavior.
4. **Given** no run is selected, **When** the list is ready, **Then** bulk controls are hidden. After selecting a run they appear only when existing capability and permission checks allow the action.
5. **Given** filters or page change, **When** the new list is applied, **Then** selection is cleared.

### User Story 4 - Inspect a run and return to the same list (Priority: P2)

An operator opens a run from a filtered or paged list and returns to the same query and page context. Direct detail links still have a safe default return path. Loading, no matches, unsupported filters, and request failures are visibly distinct.

**Why this priority**: An operator should be able to inspect a result and continue triage without reconstructing the list state.

**Independent Test**: Open a run present only on page two of an applied filter, use the in-app return action, and verify that the same query, page size, and cursor are restored. Repeat with a direct detail URL and confirm its safe default list destination.

**Acceptance Scenarios**:

1. **Given** an applied query on a non-first page, **When** the operator opens a run and uses Back to Runs, **Then** the same filters, page size, and cursor are restored.
2. **Given** a direct run-detail URL with no Runs return context, **When** the operator returns to Runs, **Then** Studio navigates to the default Runs list.
3. **Given** a return destination, **When** Studio validates it, **Then** it accepts only a local Runs-list path and never navigates to an external or unrelated destination.
4. **Given** a successful request returns no matching rows, **When** the list finishes loading, **Then** Studio shows an empty state. A pending request shows Loading, and a failed request shows Error/Retry rather than Empty.
5. **Given** a page request succeeds with an empty page, **When** the existing recovery runs, **Then** Studio returns to the first page while preserving the query and page size. A page request error remains an error; this feature does not imply recovery from an invalid or expired cursor.

## Edge Cases

- Active nonblocking incidents belong to Needs attention but must not be described as blocking or as a failed workflow solely because they are active.
- A Running run may be healthy; a Completed run may still have an active nonblocking incident. Status and incident health remain truthful and independent.
- Resolved or suppressed incidents may remain in history but are not current active attention.
- Workflow names can be missing, long, or duplicated. Keep stable identifiers visible and do not turn an unresolved name into an empty identity.
- Workflow search may return no matches, fail, or need to resolve an identity already present in the URL. Distinguish loading, empty, and error; retain the identity if its name cannot be resolved.
- Applying or removing criteria resets page position and selection but preserves page size.
- Unsupported incident-health filtering is not the same as zero matching runs.
- Successful empty-page recovery and invalid-page request errors are separate cases. Only the successful-empty behavior is covered by the existing contract.
- Long identifiers and workflow names must remain understandable at laptop widths and in light and dark themes. Keyboard users must be able to operate primary controls, workflow search, advanced filters, chips, selection, and run inspection.

## Requirements

### Functional Requirements

- **FR-001**: Runs MUST provide separate, composable choices for active-incident attention and Running lifecycle status.
- **FR-002**: Incident health MUST remain distinct from execution lifecycle and preserve the definitions in [Spec 192, Workflow Incident Troubleshooting](../192-incident-troubleshooting/spec.md) and its [incident contract](../192-incident-troubleshooting/contracts/incident-troubleshooting.md).
- **FR-003**: Operators MUST be able to find a workflow by its human-readable name and select the exact workflow when names are duplicated.
- **FR-004**: Exact execution, artifact, and correlation identifiers and detailed date criteria MUST be available under More filters.
- **FR-005**: Advanced criteria MUST be applied explicitly. Applied-filter chips MUST represent only active criteria, support removing one criterion without clearing the others, and provide a way to clear all criteria while preserving page size.
- **FR-006**: More filters MUST remain collapsible when criteria are applied; the visible summary MUST report the applied count truthfully, and collapsing MUST preserve unsubmitted drafts.
- **FR-007**: Changing a filter MUST reset page position and row selection while preserving page size. Changing a primary choice MUST preserve unrelated advanced drafts.
- **FR-008**: Run rows MUST lead with the human workflow name when available. Exact execution, artifact, definition, and version identifiers MUST remain inspectable and copyable.
- **FR-009**: Bulk actions MUST remain hidden until at least one run is selected and MUST obey existing capability and permission checks.
- **FR-010**: Opening a run MUST allow a safe return to the same local Runs query and page context. Direct detail links MUST retain a safe default return path.
- **FR-011**: Loading, successful empty results, unsupported filtering, and request errors MUST be distinguishable.
- **FR-012**: Filtering MUST cover the full authorized result set before pagination. Existing authorization, tenant scoping, and date behavior MUST remain unchanged.
- **FR-013**: The reusable list/filter convention established by this slice MUST be recorded in the existing Studio design specification.

## Key Entities

- **Run-list query**: The applied criteria, page size, and page position represented by the Runs list.
- **Workflow choice**: A human-readable workflow name paired with its stable identity and, when needed, version.
- **Applied filter chip**: The visible, removable representation of one applied criterion.
- **Run identity**: Human workflow name plus exact execution, artifact, definition, and version identifiers.
- **Return destination**: A validated local Runs-list location containing the query and page context from which a run was opened.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A multi-page acceptance dataset proves that Needs attention includes active blocking and nonblocking runs, excludes inactive controls, and composes independently with Running.
- **SC-002**: Workflow-name search distinguishes duplicate names and selects the exact intended workflow; run rows lead with the human name while preserving copyable exact identifiers.
- **SC-003**: Applied filter chips match the visible list state; removing one preserves the others and page size.
- **SC-004**: An operator can open a run from a later page and return to the same query/page context, with loading, empty, unsupported, and error states distinguishable.
- **SC-005**: Keyboard and light/dark layout checks at 1280px and 1440px verify the primary flow, advanced disclosure, selection actions, long names, and identifiers.

## Assumptions

- Existing supported workflow and run filtering is sufficient for this slice; no runtime workflow-name filtering is added.
- Incident-health meaning, authorization, tenant scoping, date behavior, and capability/permission checks remain governed by existing contracts.
- “Needs attention” names the active-incident filter; it does not imply that every result is blocking or requires a recovery action.
- A failed or expired page cursor does not become an empty result by assumption. This slice preserves only the existing successful-empty-page recovery.
- The feature changes the Studio list experience and its reusable list convention; it does not add runtime recovery actions, change incident policy, or introduce a generalized list framework.

## References

- [Issue #3062](https://github.com/elsa-workflows/elsa-foundation/issues/3062)
- [Spec 192: Workflow Incident Troubleshooting](../192-incident-troubleshooting/spec.md)
- [Spec 192 incident contract](../192-incident-troubleshooting/contracts/incident-troubleshooting.md)
