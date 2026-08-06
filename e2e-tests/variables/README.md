# variables — typed variables, value conversion, coercion, inputs/outputs

Backend REST tests for the value/typing layer. Shared helper: `_VarCommon.ps1`. Runs against a from-source
`Elsa.Workbench` (see ../README.md).

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-TypedVariables.ps1` | workflow-scope variables of `Int32`/`Boolean`/`Decimal`/`Object` and an `Int32` **collection** (`Array`) round-trip via Set → SetOutput; an unset variable surfaces its declared **default** |
| `Test-ValueConversionProfiles.ps1` | `publishing/value-conversion/profiles` advertises the `elsa.json` + `elsa.xml` converters (sources/targets); `expressions/variable-types` lists the well-known aliases |
| `Test-CrossTypeCoercion.ps1` | Literal string → `Int32` coerces (100); JS → `Int32` coerces (6*7→42); Object → String sink **faults** (the materialization boundary) |
| `Test-WorkflowInputsOutputs.ps1` | typed workflow inputs bound via `WorkflowRequest` (`{ memberKey }`), executed WITH inputs (execute body carries `inputs`), echoed into typed outputs |

## Contract notes learned

- **`collectionKind`** values are `Single` / `Array` / `List` (not "collection"); use `Array` for array-typed variables.
- **Output value shape:** a string output carries its value under `value.preview`; numbers/objects/arrays carry the raw value under `value.value` (objects/arrays as a structured `{kind, items/properties}` tree). `Get-OutputPreview` coalesces both.
- **Coercion:** Literal/JS bindings coerce string↔number into a numeric target; an **Object cannot be materialized into a String** input (faults) — the same boundary seen with `WriteHttpResponse.Body`.
- **Inputs:** the `execute` body accepts `inputs` (name → JSON value); `input.*` is read with a `WorkflowRequest` expression whose `memberKey` is the declared input's reference key.

All four scripts pass against current `main`; no bugs surfaced in this area.
