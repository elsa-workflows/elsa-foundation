# Feature Specification: Outlined shared Studio fields

**Feature Branch**: `agent/codex/4219-outlined-shared-fields`
**Created**: 2026-11-12
**Status**: Implemented (delivery gates tracked by T014 and PR #4244)
**Input**: “Let's roll the Outlined style out across our shared components and themes. Deliver it end to end.”
**Issue / program**: #4219 / #2559. Reference: accepted Outlined Secrets production #4076 and #3878.

## User Scenarios & Testing

### User Story 1 — Recognise a field everywhere (P1)
A Studio user sees the same persistent border label when editing labeled single-value fields in host forms, module forms, settings and workflow properties. Existing content and keyboard behaviour survive the rollout.

**Independent test**: edit, validate, correct and save native text, number, password, date/time, select and multiline fields; observe stable labels and unchanged values, events and focus.

**Acceptance scenarios**:
1. Given an empty or populated field, when it receives focus, autofill or a value, then its label remains readable on its top border and does not cover the content.
2. Given help, required metadata or an error, when validation changes, then the control retains its accessible name, unique ID and valid associated descriptions; recovery removes stale errors.
3. Given a pending or read-only form, when its state changes, then native disabled/read-only behaviour remains correct and a failed write retains the draft.
4. Given a password or composite field, when its adjacent action is used, then focus and values remain intact and no nested label or duplicate action is introduced.

### User Story 2 — Keep my chosen theme (P1)
Every built-in theme and supported appearance presents the new fields using its own colours, density and rounding, with an unbroken, readable label notch and distinct focus, invalid and disabled states.

**Independent test**: inspect a real shared-field state gallery for every theme/appearance and affected real composed pages in light/dark.

**Acceptance scenarios**:
1. Given any built-in appearance, when fields render against page, card, dialog and inspector surfaces, then label, value, edge and focus remain readable.
2. Given a narrow viewport or enlarged text, when labels wrap or multiline values scroll, then labels remain outside the scroller and fields/actions remain reachable without page overflow.
3. Given RTL, forced colours or reduced motion, when fields are used, then logical placement, keyboard focus and state distinctions remain usable.

### User Story 3 — Build a consistent module (P2)
A module author uses the shared field contract and semantic theme values, with examples matching the real controls. Optional modules receive the same treatment without importing another module's private implementation.

**Independent test**: build host and independently bundled consumers and inspect representative optional-module forms.

**Acceptance scenarios**:
1. Given an existing labeled native editor, when migrated, then it uses the shared treatment and preserves parsing, required state, validation, references and events.
2. Given checkbox/radio groups, search boxes, rich editors or table cells, when inventoried, then their appropriate semantics and density are retained, with an explicit recorded treatment rather than a silent omission.

### Edge Cases
Long/localised labels; IDs and descriptions supplied by callers; multiple field instances; disabled field with an enabled adjacent action; nested custom controls; invalid JSON; native date/number affordances; external imports; validation banners; hidden optional sections; theme changes without remount; custom theme surfaces; 320/360px and 200% text.

## Requirements

- **FR-001** Shared labeled single-value fields MUST default to Outlined, with no style preference or library migration.
- **FR-002** Host, settings, workflow and optional-module adoption MUST be inventoried and completed; every exception MUST identify its semantic reason.
- **FR-003** Labels, hints/errors, required state, IDs and adjacent actions MUST remain accessible and correctly associated.
- **FR-004** All built-in themes/appearances MUST retain their identity and readable field states through semantic theme values.
- **FR-005** Typing, keyboard navigation, autofill, masking, focus, draft retention, selection and read-only/pending rules MUST remain unchanged. No persistence, backend, validation or serialisation contract changes.
- **FR-006** Narrow/enlarged-text, RTL, forced-colour and reduced-motion use MUST remain usable.
- **FR-007** Secret fields MUST use the shared contract; duplicate Secret-specific field presentation MUST be removed without weakening its behaviour.
- **FR-008** Module guidance and actual examples MUST describe the shared contract.
- **FR-009** Current-commit behavioural and rendered evidence, independent acceptance, required CI, protected merge and containing-main verification MUST substantiate completion. Failures/skips and historical evidence retain their original status.

## Success Criteria

- **SC-001** Every inventoried labeled single-value production editor uses the shared Outlined treatment or has a documented semantic exception.
- **SC-002** Every built-in appearance passes the named field-state checks once and retains a reviewable capture; no missing/retried case counts as success.
- **SC-003** Users can complete representative host/module create/edit/validate/recover/save journeys in light/dark and narrow layouts with unchanged stored values and coherent focus.
- **SC-004** Independent review finds no unresolved actionable usability defect; exact-head required checks and a containing-main run pass.

## Assumptions and Boundaries
The approved production Outlined appearance is the reference. Preserve checkbox/radio, compact table-cell, search and rich-editor semantics rather than applying a text-input frame blindly. Preserve theme names and user preferences. No new component library, theme-style switch, backend API, provider, package identity or unrelated interaction redesign. The owner has authorised implementation and delivery; ordinary design/implementation choices do not require further approval.
