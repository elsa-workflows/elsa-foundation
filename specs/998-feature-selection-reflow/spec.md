# Feature Specification: Feature Selection Reflow

**Feature Branch**: `agent/codex/3175-feature-selection-reflow`

**Created**: 2026-10-21

**Status**: Implemented — terminal transition takes effect when the implementation PR merges; root qualification gates below remain pending.

**Input**: Issue #3175, “Studio Features: keep narrow selection counts and bulk actions readable.”

## User Scenarios & Testing

### User Story 1 - Read and use feature selection controls in a narrow list (Priority: P2)

As a Studio administrator reviewing Features on a narrow screen or module pane, I can read the visible and selected counts and reach each bulk-action button without controls overlapping or content being clipped.

**Why this priority**: The current selection row overlaps its visible count with Enable at 320px, and the selected count collapses at 320px and nearly disappears at 360px.

**Independent Test**: Render the real Feature Management page in the Studio browser harness with empty and populated selection, then measure the count and action rectangles at 320px, 360px, and an equivalent narrow module-container width. Compare with a 1280px reference.

**Acceptance Scenarios**:

1. **Given** an empty selection and 114 visible features, **When** the page renders in a 320px or 360px viewport, **Then** `0/114 visible` and `0 selected` are legible, do not overlap any bulk-action button, and all buttons remain inside the row.
2. **Given** the visible features are selected, **When** the page renders in a narrow viewport or a 286px module container, **Then** `114/114 visible` and `114 selected` remain fully legible, and the Enable, Disable, and Clear buttons do not overlap the counts or each other.
3. **Given** the Feature Management page renders in a container at least 480px wide, **When** the user reviews the selection row, **Then** the existing single-row arrangement remains intact.
4. **Given** the user switches between Studio and Server, **When** the selection row reflows, **Then** each host retains its existing selection and controls keep their current keyboard and disabled states.

## Edge Cases

- Zero selected features, with selection actions disabled.
- More than 100 visible and selected features, so both count labels use three-digit numbers.
- A narrow module container inside a wider browser viewport.
- Wide-container presentation at 1280px.

## Requirements

### Functional Requirements

- **FR-001**: The bulk-selection row MUST keep visible and selected count text fully readable at 320px and 360px viewport widths and in an equivalent narrow module container.
- **FR-002**: The visible count and selected count MUST NOT overlap each other or Enable, Disable, or Clear.
- **FR-003**: Every bulk-action button MUST remain fully within the row, with no clipped text and no horizontal page scrolling.
- **FR-004**: Containers at least 480px wide MUST retain the current single-row arrangement.
- **FR-005**: Selection, host switching, keyboard order, disabled/read-only behavior, category filtering, inspector content, and review/apply semantics MUST remain unchanged.
- **FR-006**: Reflow MUST NOT activate a feature action or change configuration by itself.

## Key Entities

No data entities are introduced. The layout presents the existing visible and selected feature counts.

## Success Criteria

### Measurable Outcomes

- **SC-001**: At 320px, 360px, and 286px module-container widths, both count spans have non-zero layout width, their rendered text is not truncated, and neither overlaps a bulk-action button.
- **SC-002**: Each bulk-action button's rectangle is contained by the selection row, with no pair of button rectangles overlapping and no horizontal document overflow.
- **SC-003**: At 1280px, count and button rectangles remain on the existing single row.
- **SC-004**: The browser regression covers empty and 114-selected states while preserving the existing host-specific selection behavior.

## Assumptions

- The feature-management module continues to own its responsive styles and existing markup.
- A container query is the appropriate breakpoint source because the page can occupy a narrow module pane within a wider viewport.
- Existing Studio module tests remain the behavior regression surface for category, inspector, permissions, and review/apply flows.

## Integration gates

The implementation PR must pass root normal-host verification, independent review, and exact-head CI before merge. Resulting-main checks complete public issue qualification afterward. The status above follows the repository rule that the terminal transition belongs in the implementation PR; it is not evidence that any pending gate already passed.
