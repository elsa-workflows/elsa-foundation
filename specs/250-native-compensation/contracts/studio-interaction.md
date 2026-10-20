# Contract: Studio authoring and inspection

Studio is the React/Vite workflows module (`Studio/src/essentials/Elsa.Studio.Workflows/Client`). The scope
uses the existing structure design facet and slot navigation. The paired list is a new canvas mode beside
`SequenceDesigner`, chosen when the current scope owner's structure kind is `elsa.compensation-scope.structure`.

## Authoring — sequential example

```
Order fulfilment  ›  Compensation scope "Reserve and confirm"
┌──────────────────────────────────────────────────────────────────────────────┐
│ Steps run top to bottom. If a step fails, completed steps are undone          │
│ bottom to top.                                                                │
├────┬──────────────────────┬───────────────────────────────────────────────────┤
│ #  │ Step                 │ Compensation                                      │
├────┼──────────────────────┼───────────────────────────────────────────────────┤
│ 1  │ ▣ Reserve inventory  │ ↩ Release inventory     Undoes: Reserve inventory │
│ 2  │ ▣ Reserve delivery   │ ↩ Cancel delivery       Undoes: Reserve delivery  │
│ 3  │ ▣ Confirm order      │ [+ Add compensation]                              │
├────┴──────────────────────┴───────────────────────────────────────────────────┤
│ [+ Add step]                                                                  │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Row actions** (keyboard: arrow keys move between rows; Tab moves between the row's cells; Enter opens; Delete
  removes after confirmation): rename the step, move up/down, delete, open the step, open the compensation,
  replace or remove the compensation.
- **Opening a cell** drills into that slot with `enterSlotScope`. The breadcrumb reads
  `… › Reserve and confirm › Reserve inventory (compensation)`.
- **Accessible names**: row `Step 2, Reserve delivery, compensated by Cancel delivery`; cell buttons
  `Open step Reserve delivery` and `Open compensation for Reserve delivery`; add button `Add compensation for Confirm order`.
- **Inspector** for the scope: inputs (`faultAfterCompensation`, boolean: off "Finish with outcome Compensated" | on "Fail with the
  original error"), outcomes `Done` and `Compensated`.
- **Captured values**: when editing a handler input, the expression editor's reference picker lists the paired
  step's outputs first, under "From *Reserve inventory*". The stored value is the ordinary activity-output
  reference. Nothing new is persisted.
- **Validation** messages:
  - `Step 2 needs a name.` (every row message uses the path `{scope}/steps/{index}`)
  - `Step "Reserve inventory" is defined twice.`
  - `Step "Reserve delivery" has no activity.`
  - Deferred to M5 (FR-014): the warning `"Release inventory" is marked replay-safe; its effect may not be
    recorded before it runs.`

## Authoring — nested example (M3 semantics, notation fixed now)

```
│ 2  │ ⧉ Delivery (scope)   │ — undone through its own steps —                  │
```

A step that is itself a compensation scope shows a nested-scope icon and the text "undone through its own
steps" in place of a handler. Adding a handler to it is allowed and runs after the inner steps are undone.

## Authoring — partial-loop example (M3 semantics, notation fixed now)

```
│ 1  │ ⟳ For each item: Reserve item │ ↩ Release item   Undoes each completed item │
```

A loop step's handler runs once per completed iteration, newest first. The row says "each completed item".

## Inspection — run view

```
Run 4f2c… › Compensation scope "Reserve and confirm"            ⟲ Compensated
┌────┬────────────────────┬──────────────┬──────────────────────┬──────────────┐
│ #  │ Step               │ Forward      │ Compensation         │ Recovery     │
├────┼────────────────────┼──────────────┼──────────────────────┼──────────────┤
│ 1  │ Reserve inventory  │ ✓ Completed  │ Release inventory    │ ⟲ Compensated (2nd) │
│ 2  │ Reserve delivery   │ ✓ Completed  │ Cancel delivery      │ ⟲ Compensated (1st) │
│ 3  │ Confirm order      │ ✕ Faulted    │ —                    │ — Not applicable    │
└────┴────────────────────┴──────────────┴──────────────────────┴──────────────┘
Recovery started because "Confirm order" failed: ActivityReturnedFault — Order rejected.   [Show incident]
```

- Each state is an icon plus text: `✓ Completed`, `✕ Faulted`, `● Running`, `◌ Not run`, `⟲ Compensated`,
  `◌ Can be undone` (Registered: the step completed and has a handler, and the scope is still running, so a later
  fault would undo it), `Kept` (the scope completed or was cancelled without recovery; undoing a completed scope is
  M3's late compensation), `… Pending`,
  `● Undoing`, `✕ Undo failed`, `— Not applicable`, `Not needed` (the step has no handler), and `Not loaded` when the
  run's activity list is a bounded page that may hold the occurrence on a later page. In M1 `Undo failed` means the
  handler faulted and its incident blocks the run. M2 adds `⚠ Uncertain` and `✋ Needs action`.
- The recovery cause shows the step and the fault's incident failure type and message. The activity's own fault code
  (such as `order.rejected`) needs the incident summary to carry it.
- Clicking a Forward or Recovery cell selects that occurrence, using the existing `selectedEvidenceId`.
- **Handler occurrence inspector**: header `Compensation · Cancel delivery`; first row `Compensates: Reserve
  delivery (occurrence ae-5)` as a link, then the normal input/output evidence.
- **Timeline**: handler rows carry a `Compensation` tag and the ⟲ icon. Their tooltip and accessible label read
  "Compensation of Reserve delivery".
- **Canvas** (parent Flowchart): the scope node's run badge shows `⟲ Compensated` when its outcome is
  `Compensated`.

## Data the UI needs

- Authoring: the existing structure facet (kind, slots), plus a `compensation` designer mode in
  `getActivityDesignerSupport`.
- Inspection: the derived view model in [data-model.md](../data-model.md), from the run's executions and incidents.
  Handler labelling in the timeline uses the execution's scheduling cause `compensation`.
