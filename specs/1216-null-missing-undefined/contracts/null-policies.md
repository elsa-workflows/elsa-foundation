# Contract: null policies and diagnostics

## Workflow definition JSON (authored)

```json
{
  "strategyOptions": {
    "nullHandling": { "defaultOnNull": true, "undefinedAsNull": true }
  }
}
```

Omitted `nullHandling` means both `false`. The block round-trips through export and import unchanged.

## Diagnostics

| Code | Severity | When | Names |
|---|---|---|---|
| `VF-ACT-003` | error | Omitted required activity input at publication (unchanged); omitted optional input whose type has no type default and no declared default (unchanged) | node, input |
| `VF-ACT-004` | error | `null` for a non-nullable target without `DefaultOnNull`, or whose type has no type default | node, input or target |
| `runtime.input.missing-required` | error | Admission: omitted required workflow input. Exists since #3731 (`WorkflowExecutableInputValidationReasonCodes.MissingRequired`, start refused with category `Input`, `400`); unchanged | input |
| `VF-WF-REQ-002` | error | Admission: explicit `null` for a non-nullable workflow input without `DefaultOnNull`. New reason code in `WorkflowExecutableInputValidationReasonCodes`, refused the same way as #3731's | input |
| `VF-VAL-NULL-DEFAULTED` | info | `DefaultOnNull` substituted a type default (recorded on the invocation) | node, input, default |
| `JS-SCRIPT-NO-RESULT` | error | A function-body script's result is consumed and it has no `return` (publication), or any script form's consumed result is `undefined` at run time without `UndefinedAsNull`. Never raised for an expression script or completion value that lacks `return` | activity, bound target |
| `JS-UNDEFINED-DROPPED` | warning | An `undefined` property was dropped (no `UndefinedAsNull`) | node, input, JSON path |
| `JS-NONFINITE-NULL` | warning | `NaN` or `±Infinity` became `null` | node, input, JSON path |
| `VAR-NOT-FOUND` | warning | A computed variable name matched nothing; read marked absent | node, variable name |
| `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` | error | Converter: null `SetVariable` / `SetOutput` value for a non-nullable target with no recorded `DefaultOnNull` (narrowed from spec 267) | activity, member |

Code names are proposals; T002 aligns them with the existing diagnostic code catalogs before any test pins them.

## Elsa 3 converter configuration

```json
{ "Elsa3Mapping": { "ApplyElsa3NullPolicies": true } }
```

When `true`, every converted workflow gets `nullHandling: { defaultOnNull: true, undefinedAsNull: true }`, and the
conversion report lists the policies.
