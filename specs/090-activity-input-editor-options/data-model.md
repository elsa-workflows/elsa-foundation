# Data Model: Activity Input Editor Options

## ActivityInputOption

- `Label`: nonblank display string.
- `Value`: non-null JSON scalar compatible with the scalar input type or collection element type. Numbers must round-trip exactly through the browser's numeric representation; integral values are limited to ±9,007,199,254,740,991 and floating values must be finite.
- Ordering: declaration order for repeatable attributes and array order for shorthand/provider results.
- Identity: JSON value and scalar kind; duplicate identities are invalid even when labels differ.

## ActivityInputOptionsProviderDescriptor

- `Key`: nonblank, case-sensitive stable provider identifier.
- `DependsOn`: distinct, case-sensitive sibling input names.
- Validation: dependencies require a provider and must resolve within the same activity definition.

## InputUISpecification

Opaque JSON stored on `InputDefinition` with exactly one option source:

- Static: `options: ActivityInputOption[]`
- Dynamic: `optionsProvider: ActivityInputOptionsProviderDescriptor`

Other UI specification fields remain untouched and forward-compatible.

## ActivityInputOptionsContext

- `WorkflowState`: current client-authored workflow definition state, including unsaved edits.
- `Activity`: the activity node selected by `NodeId`, validated to use the routed activity-version identifier.
- `Input`: the cataloged input definition selected by the routed input name.

## Provider Registry

- Contributions are keyed by `Key` using ordinal comparison.
- Zero matches at request time produce an unavailable response.
- More than one contribution for a key is invalid host composition and fails startup.

## Studio State

- `idle/loading/ready/error` request state per dynamic option input.
- The latest successful options list is never used to silently rewrite the activity value.
- Superseded requests transition to neither error nor ready; only the latest request may update UI state.
