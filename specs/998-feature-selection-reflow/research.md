# Research: Feature Selection Reflow

## Decision

Use the existing `@container feature-management (width < 480px)` rule to switch only `.feature-management-bulk-actions` to a three-column grid. Let the current inner action wrapper's children join that grid at narrow sizes. Keep the label across the first two columns, selected count in the third, and each action button in one cell on the next row.

## Rationale

The page already measures its own inline size with the named `feature-management` container. The current narrow query reflows the search/category toolbar but leaves the selection row in one flex line. The root's rendered baseline reproduces overlap at 320px and a zero-width selected count, while the wider row fits at 640px and 1280px. A container query also handles a narrow module pane inside a desktop viewport.

A grid gives the two count spans stable space while keeping each button on its own predictable row cell. The markup, accessible names, DOM order, handlers, enabled/disabled state, and host-local selection remain unchanged.

## Alternatives considered

- A viewport media query: rejected because the module can be narrow while the browser remains wide.
- Allowing the current inner flex row to wrap: rejected because the selected count remains a flex item competing with three non-wrapping buttons and can still shrink below its text width.
- Changing JSX or action semantics: unnecessary for a layout-only defect and outside issue scope.

## Unknowns

None remain. Existing container-query support and the target widths are already used by this module and its current Studio browser tooling.
