# Contract: null policies and diagnostics

## Workflow definition JSON (authored)

```json
{
  "strategyOptions": {
    "nullHandling": { "defaultOnNull": true, "undefinedAsNull": true }
  },
  "inputs": [
    { "name": "discount", "type": { "alias": "Decimal" }, "isRequired": false, "isNullable": true }
  ]
}
```

Omitted `nullHandling` means both `false`. The block round-trips through export and import unchanged. `isNullable` on
a workflow input already exists on the authored `InputDefinition`; S-2 pins it in the executable's input contract.

## Diagnostics

Codes follow the existing `VF-<AREA>-NNN` pattern (`VF-ACT-*`, `VF-READ-*`, S-8's `VF-PREC-*`) and the admission
reason codes of `WorkflowExecutableInputValidationReasonCodes` (`runtime.input.*`).

| Code | Severity | When | Names | Exists |
|---|---|---|---|---|
| `VF-ACT-003` | error | Omitted required activity input at publication; omitted optional input whose type has no type default and that declares no default | node, input | yes |
| `VF-ACT-004` | error | `null` for a non-nullable target without `DefaultOnNull`, or whose type has no type default | node, input or target | yes |
| `runtime.input.missing-required` | error | Admission: omitted required workflow input (#3731) | input | yes |
| `runtime.input.null-not-allowed` | error | Admission: explicit `null` for a non-nullable workflow input without `DefaultOnNull`, refused like #3731's codes (category `Input`, `400`) | input | new |
| `VF-NULL-001` | info | `DefaultOnNull` substituted a type default (recorded on the invocation, or on the start for a workflow input) | node or workflow, input, default | new |
| `VF-UNDEF-001` | error | A consumed result produced no value: a `Run JavaScript` body with a bound result and no `return` (publication), or a consumed result that is `undefined` at run time, or `undefined` assigned to a whole variable (FR-030), without `UndefinedAsNull` | activity, bound target or variable | new |
| `VF-UNDEF-002` | warning | An `undefined` object property was dropped (no `UndefinedAsNull`) | node, input or variable, JSON path | new |
| `VF-UNDEF-003` | warning | An `undefined` array element or sparse hole became a `null` placeholder (either policy) | node, input or variable, JSON path | new |
| `VF-NUM-001` | warning | `NaN` or `±Infinity` the script computed became `null` (either policy) | node, input or variable, JSON path | new |
| `VF-READ-001` | warning | A computed variable name matched nothing; read marked absent (built by S-4) | node, variable name | yes |
| `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` | error | Converter: null `SetVariable` / `SetOutput` value for a non-nullable target with no recorded `DefaultOnNull` (narrowed from spec 267) | activity, member | yes |

Run-time warnings go through S-8's `IExecutionWarningSink` (`ExecutionWarning(Code, Message, Severity, Details)`), so
Studio shows them with the instance's other diagnostics. Under `UndefinedAsNull` an `undefined` property kept as `null`
gives no warning: the policy is the recorded, visible choice.

## Elsa 3 converter configuration

```json
{ "Elsa3Mapping": { "ApplyElsa3NullPolicies": true } }
```

When `true`, every converted workflow gets `nullHandling: { defaultOnNull: true, undefinedAsNull: true }`, and the
conversion report lists the policies.
