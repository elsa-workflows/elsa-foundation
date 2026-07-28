# Quickstart: Validate Activity Definition Graph Authoring

## Prerequisites

- Studio dependencies installed with `pnpm install`
- a Foundation backend advertising Activity Graph schemas 1 and 2
- an authorized account that can create and edit Activity Definitions
- the coordinated Foundation convergent-outcome change when testing shared public targets

## Focused automated checks

```bash
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm lint
pnpm test:browser -- activity-definition-graph-authoring.spec.ts
```

Run the touched Foundation Activity Graph design/runtime tests in the Foundation repository, followed by its required build/test gate.

## Creation scenario

1. Open Workflows → Activity Definitions → Create.
2. Confirm provider/schema controls are absent when Activity Graph is the only meaningful implementation type.
3. Confirm Flowchart is selected by default and Sequence/BPMN are available.
4. Open Category, select an existing authorized suggestion, then replace it with a new category.
5. Create and confirm the latest supported schema and chosen root composition are present.

## Shared designer scenario

1. Add activities from the palette.
2. Select and edit properties using shared property/expression editors.
3. Enter and leave a nested container scope using breadcrumbs.
4. Move nodes and confirm layout survives reload.
5. Undo and redo; confirm each accepted state saves as an exact activity draft revision.
6. Open a workflow definition and confirm equivalent designer interactions still behave as before.

## Public interface scenario

1. Add two emitted public outcomes.
2. Map two different graph root outcomes to the same public outcome.
3. Map another source to the second public outcome.
4. Confirm a source cannot be mapped twice and every emitted target needs at least one mapping.
5. Add required and optional outputs; confirm required mapping validation and optional zero-mapping behavior.
6. Use a public input and graph variable in expressions without duplicating the input as a variable.

## JSON scenario

1. Open Code and change presentation, contract, graph payload, and layout.
2. Introduce invalid JSON and confirm autosave, validation, migration, test, publication, and navigation are blocked.
3. Reset and confirm no new revision.
4. Apply valid JSON containing unchanged and new node identifiers.
5. Confirm unchanged node positions remain and new nodes are placed without overlap.
6. Undo the Apply and confirm the restored state saves as a new revision.

## Diagnostics and test scenario

1. Create graph, contract, mapping, and JSON errors.
2. Open diagnostics and focus each supported node or field, then return focus to its diagnostic.
3. Repair all blocking issues.
4. Start an Activity Definition test run with public inputs.
5. Inspect public outputs, emitted outcome, incidents, logs, and execution details.

## Legacy scenario

1. Open a schema 1 Activity Graph draft.
2. Save an ordinary schema 1-compatible edit and confirm no migration.
3. Request a schema 2-only mapping operation and confirm explicit migration guidance.
4. Review and accept migration, then confirm history resets and schema 2 mappings are available.
