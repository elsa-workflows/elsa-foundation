# UI State Model: Activity Inspector Tabs

This feature introduces no persisted domain data. It reorganizes existing activity-authoring capabilities around a small session-local UI state model.

## Activity Inspector Tab ID

Allowed values:

- `inputs`
- `outputs`
- `variables`
- `slots`
- `details`
- `version`

The value identifies the current authoring concern, not a module Contribution.

## Available Tab Set

Derived for the currently inspected activity:

| Tab | Availability |
|-----|--------------|
| Inputs | Always |
| Outputs | Always |
| Variables | Activity supports container-scoped variables |
| Slots | Activity exposes one or more child slots |
| Details | Always |
| Version | Always |

The set is always ordered as Inputs, Outputs, Variables when present, Slots when present, Details, Version.

## Active Tab State

Owner: current workflow editor session.

Initial value: `inputs`.

Validation rule: active tab must belong to the current Available Tab Set.

Fallback: `inputs`.

The value is not persisted to the workflow, user preferences, browser storage, or URL.

## Activity Inspector View State

Scoped to one inspected activity identity:

- Per-tab vertical scroll position.
- Editor-local disclosures, menus, and partially entered transient control state.
- Slot activity-picker state.

This view state survives inner-tab changes for the same activity and is discarded when activity identity changes.

## State Transitions

### Open workflow editor

1. Available Tab Set is derived from the inspected activity.
2. Active Tab is `inputs`.
3. Activity Inspector View State is fresh.

### Select another inner tab

1. Active Tab changes to the selected available value.
2. Existing per-tab view state remains associated with the current activity.
3. Inactive panels become non-presentational but retain state.

### Select another activity

1. Available Tab Set is re-derived.
2. Active Tab is retained if still available; otherwise it becomes `inputs`.
3. All Activity Inspector View State is reset.
4. The newly visible tab starts at its top.

### Leave Inspector for Runtime or Artifacts

1. Active Tab remains in workflow-editor session state.
2. Returning to Inspector restores that Active Tab, subject to current availability.

### Lose the selected activity

1. The activity-tab surface is replaced by the existing no-selection prompt.
2. Active Tab may remain session-local but has no visible effect until another activity is inspected.
3. The next activity receives fresh Activity Inspector View State.
