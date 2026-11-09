# Quickstart: Feature Selection Reflow

## Prerequisites

Install the existing Studio workspace dependencies. The tests use the repository's configured Chromium Playwright project and local Vite fixture server.

## Validate the rendered layout

From `Studio/`, run:

```bash
pnpm exec playwright test tests/browser/feature-management-layout.spec.ts
```

Expected: the empty and 114-selected counts are fully readable at 320px, 360px, and a 286px module container; count and action rectangles do not overlap; all buttons remain inside the row; the document has no horizontal overflow; and the 1280px reference stays on one row. The fixture only changes the checkbox selection and never activates a feature action.

## Run scoped module checks

From the repository root:

```bash
pnpm --dir Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client test
pnpm --dir Studio exec stylelint --config .stylelintrc.json ../Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css
pnpm --dir Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client build
```

Expected: the existing module tests pass, the changed stylesheet passes Stylelint, and the module client builds without adding dependencies.
