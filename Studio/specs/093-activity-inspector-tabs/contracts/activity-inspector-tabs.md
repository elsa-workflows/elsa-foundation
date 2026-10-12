# UI Contract: Activity Inspector Tabs

## Purpose

Define the observable workflow-authoring Inspector behavior after activity content is separated into focused inner tabs.

## Outer Panel Boundary

- Inspector remains a workflow-level right-panel tab beside Runtime and Artifacts.
- Activity inner tabs exist only while Inspector is showing an activity.
- The feature does not create a new Workflow Designer Slot or accept contributed inner tabs.

## Fixed Activity Context

The non-scrolling context contains:

1. Selected activity display name.
2. Scope-owner explanation when the canvas container is being inspected.
3. Availability warning when the activity is no longer available for new use.
4. Inner activity tablist.

The display name is not duplicated in Details.

## Tab Contract

| Order | Tab | Content | Empty behavior |
|-------|-----|---------|----------------|
| 1 | Inputs | Existing input/property authoring and intrinsic destination controls | Explicit no-configurable-inputs message |
| 2 | Outputs | Existing output-capture controls | Explicit no-outputs message |
| 3 | Variables | Existing container-scoped variable declarations | Tab omitted when unsupported |
| 4 | Slots | Existing child-slot navigation and replacement controls | Tab omitted when no slots are exposed |
| 5 | Details | Node ID and Activity Type | Always populated for a selected activity |
| 6 | Version | Activity version identity and reusable-boundary details/actions | Always present; may contain only version identity |

Variables and Slots retain their relative order when only one is present.

Inputs and Outputs do not repeat section headings that duplicate the active tab.

## Interaction Contract

- Inputs is the initial active tab.
- Pointer activation selects a tab.
- Arrow, Home, and End behavior follows the shared Studio tablist contract.
- Only the active tab participates in sequential tab order.
- Each tab exposes selected state and controls a labelled tabpanel.
- Inactive panels are hidden from presentation and assistive technology while retaining state.
- A tab switch retains every panel's scroll and transient control state.
- An activity change retains the active tab only if available, otherwise selects Inputs.
- An activity change clears all per-activity transient state and scroll positions.
- An Inspector → Runtime/Artifacts → Inspector round trip retains the active inner tab.

## Layout Contract

- Inner tabs use text labels only.
- The tablist is a single horizontal row.
- If labels exceed the available width, the row scrolls horizontally without widening the Inspector or page.
- Fixed activity context remains visible while the active tabpanel scrolls vertically.
- Each tabpanel owns its vertical scroll position.

## Compatibility Contract

- Existing input editing, output capture, variable declaration, slot navigation/replacement, exact-version changes, source links, autosave, undo/redo, and save behavior do not change.
- Non-activity designer-element Inspectors keep their existing presentation.
- No-selection behavior remains the existing activity-selection prompt.
- Existing `PanelTabList` callers without tabpanel bodies remain valid and do not receive dangling ARIA references.
