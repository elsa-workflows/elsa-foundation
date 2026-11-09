# Feature Specification: Honest Secrets inventory coverage

**Feature Branch**: `agent/codex/3069-secrets-inventory`

**Created**: 2026-10-22

**Status**: Implemented — source complete; independent review, normal-host proof and canonical acceptance remain gated

**Input**: #3069 / Studio UX epic #3057 / program #2559. Make inventory limits and loaded-scope coverage clear, find a known secret beyond the initial100 through supported server search, and retain search/filter context through the existing detail dialog. Winning claim6331801398; qualified base5d1df7097db3a731330735a94c2b6a5fb7f9a476; prerequisite3062 accepted.

## User Scenarios & Testing

### User Story 1 — Understand what the inventory covers (Priority: P1)

An operator opens Secrets and can tell whether the displayed records are the complete matching result or a bounded part of it. Counts never imply that local filters or loaded scope choices cover unseen records.

**Why this priority**: Operators must not mistake an absent row for an absent secret or a partial scope menu for the complete inventory.

**Independent Test**: A representative inventory with more than100 records clearly distinguishes loaded records, displayed records and the authoritative matching total, including a local facet that hides some loaded records.

**Acceptance Scenarios**:

1. **Given** more than100 matching secrets and an initial bounded result, **When** the list is ready, **Then** it states the actual loaded coverage and offers a useful narrowing action without claiming every secret is shown.
2. **Given** a complete matching result, **When** it is shown, **Then** the coverage text does not falsely claim missing records.
3. **Given** scope options derived from currently loaded records, **When** the operator opens the scope filter, **Then** its coverage is described as loaded results and an unseen custom scope is not presented as nonexistent.
4. **Given** an applied criterion hides loaded records, **When** counts are displayed, **Then** any authoritative total is attributed to its actual search/filter scope and cannot be read as a fabricated count of the displayed subset.

### User Story 2 — Find a known secret beyond the initial result (Priority: P2)

An operator searches for a known secret by name or display name and finds it even when it was not in the first loaded result.

**Why this priority**: Honest limit text must lead to a usable daily search path.

**Independent Test**: Seed more than100 representative metadata records, identify a record outside the initial result, narrow the supported search and verify that the exact record appears.

**Acceptance Scenarios**:

1. **Given** a known record outside the initial bounded result, **When** the operator narrows the search to its name/display name, **Then** the request searches the inventory through the supported server contract and returns that exact record.
2. **Given** a pending or failed search, **When** the state is shown, **Then** it is distinct from a complete empty result; stale results are not attributed to the new criteria.
3. **Given** no matching result, **When** the search completes, **Then** a clear no-matches state and usable reset/narrowing controls remain available.

### User Story 3 — Inspect a result without losing the search (Priority: P3)

An operator opens a result in the existing detail dialog and closes it to continue with the same applied search and filter context.

**Why this priority**: Investigation should not require rebuilding a search after every inspection.

**Independent Test**: Search/filter, open the exact result, close the detail by its normal control and by keyboard, and verify retained criteria/results.

**Acceptance Scenarios**:

1. **Given** applied search/filter criteria, **When** a matching detail dialog opens and closes, **Then** the applied criteria and corresponding result context remain.
2. **Given** a metadata-only inventory response, **When** rows/counts/filter labels are rendered, **Then** secret values and configuration keys are neither required nor exposed.
3. **Given** a selected local scope is absent from a later search result, **When** the result arrives, **Then** the control still identifies that active scope, qualified as absent from loaded results; clearing it reveals the matching loaded rows.
4. **Given** an applied query and different unsubmitted text, **When** a detail mutation reloads the list or the operator uses Refresh, **Then** the applied query remains; Search/Enter explicitly applies the draft. Retry repeats its failed query rather than a later draft.

### Edge Cases

- A full response at the request limit can still be complete when the authoritative total equals the loaded count.
- Local filters can hide all loaded rows even though additional matching records remain unseen; this cannot become an unqualified inventory-empty claim.
- Unknown custom scopes may exist outside loaded records. Registered scope suggestions are not an inventory catalog.
- Search terms, criteria or endpoint/tenant context change while a request is pending; late results cannot relabel another query's coverage.
- A failed initial or later request remains distinguishable from no secrets/no matches; normal retry remains available.
- An authoritative zero-result search takes precedence over local-facet empty wording, even when a facet remains selected.
- The existing very-large-inventory substring-search boundary is not removed by this leaf. A server refusal remains an honest error, not a fabricated zero result.
- Long secret names/scope labels, narrow viewports, both themes, keyboard access and read-only callers retain usable controls and metadata-only disclosure.

## Requirements

### Functional Requirements

- **FR-001**: The ready list MUST distinguish records displayed, records actually loaded and any authoritative matching total when these differ.
- **FR-002**: A bounded result MUST clearly say that additional matching records may remain and provide the supported search/narrowing path.
- **FR-003**: Complete and empty results MUST NOT carry a false partial-result warning. A count equal to the request limit alone does not prove incompleteness.
- **FR-004**: Counts MUST identify their actual criterion scope. Local filtering MUST NOT turn a server search total into a claimed filtered-inventory total.
- **FR-005**: Users MUST be able to find an exact known name/display-name match outside the initial100 using supported server-backed search, without loading secret values.
- **FR-006**: Scope options based on loaded metadata MUST retain an accessible loaded-results coverage explanation. Scope descriptors MUST NOT be substituted as a complete persisted-secret facet.
- **FR-007**: Loading, initial empty, no matches, partial results and request failure MUST remain distinct and actionable; a failure cannot be displayed as absence.
- **FR-008**: Search/filter/detail interactions MUST retain applied list context on closing the existing dialog, including keyboard dismissal; exact identity and focus conventions remain intact.
- **FR-009**: Query/context changes MUST invalidate obsolete observations so late results cannot misstate the current inventory or authority.
- **FR-010**: List/search/filter/count/help presentation MUST use metadata only and preserve current tenant isolation, server authorization and operation-specific permissions.
- **FR-011**: New guidance MUST follow the existing Studio semantic tokens and shared control/state conventions, remain readable in both themes and narrow layouts, and avoid adding another competing list pattern.
- **FR-012**: This leaf MUST preserve the current supported backend contract, dialog model and value-disclosure boundary. Missing capabilities, if demonstrated, MUST be separately scoped before claiming completion.

### Key Entities

- **Inventory observation**: exact applied query/context, loaded metadata, authoritative matching count and request state.
- **Displayed subset**: records visible after the current applied criteria, with honest coverage relative to the inventory observation.
- **Loaded scope choice**: a scope found in the loaded metadata, distinct from all persisted scopes or registered suggestions.
- **Detail context**: the exact selected secret metadata identity plus retained list criteria and normal dialog/focus lifecycle.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A fixture exceeding100 records exposes truthful complete/partial coverage in every exercised initial and filtered state, with zero invented inventory totals.
- **SC-002**: A known record proven absent from the initial result is found through the normal supported name/display-name search and verified by exact identity against the actual endpoint.
- **SC-003**: Every exercised detail open/close retains the applied search/filter context and corresponding visible results.
- **SC-004**: Initial empty, no matches, loading and failure are independently verified without treating failed or partial evidence as a complete empty inventory.
- **SC-005**: The exercised inventory/search/detail-return flows expose zero secret values/configuration keys and remain usable in both themes and narrow layouts.

## Assumptions

- The existing server list contract already supports search, paging, metadata facets and a filtered total; source and actual-endpoint proof must confirm the chosen path.
- The current first request is bounded at100. This leaf may use existing query support as needed, but does not require a broad paging API redesign or a new list framework.
- Broader progressive advanced filters, draft Apply/removable chips and cross-module list-layout adoption belong3071 after3070; they are not silently absorbed here.
- Detail navigation stays in the current dialog; a new deep-linkable detail route/history model is outside this issue.
- The representative proof exceeds100 but stays below the separate very-large-inventory substring-search guard; no claim is made that the latter is solved.
- Existing caller permissions and normal host composition are reused. No deployment, release, external service or optional agent setup is needed.
