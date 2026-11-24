# Workflow definition values

For the terms, see [workflow definition state and type reference](../glossary/elsa.md). The definition API and exported
workflow JSON carry the same `strategyOptions` data. Native workflows omit these engineer settings by default:

```json
{
  "strategyOptions": {
    "nullHandling": { "defaultOnNull": true, "undefinedAsNull": true }
  }
}
```

Publication pins both choices into the executable and its behavioral hash. Exporting and importing the definition
preserves them; execution needs no Elsa 3 converter installed. Studio has no policy controls.

## Admission and declarations

Requiredness tests presence before defaults. A required nullable input accepts supplied JSON `null`; omitting it is
refused before scheduling. A member declaring a default must be optional (`VF-ACT-014`). Studio turns Required off
when a default is applied and offers **Make optional** to recover an older contradictory contract.

An omitted optional workflow input uses its declared default first. Otherwise a TYPE that accepts null receives null
(including String, Any and collections); non-nullable Boolean, numeric and enum types use `false` or zero. Date/time, duration and Guid
members need a declared default or Required: publication refuses an unsupported implicit default (`VF-ACT-003`).
An optional activity input without a default stays absent when its CLR declaration accepts null. CLR declarations
retain their C# nullability; authored variables, inputs, outputs and activity contracts derive it from TYPE.

| Setting | Off | On |
|---|---|---|
| `defaultOnNull` | Explicit null into a non-nullable Int32 faults with `VF-ACT-004`. | The same null writes `0` and records informational `VF-NULL-001`. |
| `undefinedAsNull` | Consumed undefined or a whole-variable undefined assignment faults with `VF-UNDEF-001`. An object property is dropped with `VF-UNDEF-002`. | Consumed undefined and undefined properties become explicit null. The target's null contract still applies. |

Default-on-null applies at workflow admission and activity/graph inputs, intrinsic Set/SetOutput, step-variable
writes and output captures. It never repairs an authored variable default or an operator's ModifyVariable command.
A CLR non-nullable String has no supported non-null type default and still refuses null with either setting.
Unassigned Boolean/numeric variables read false/zero without requiring a policy.

Graph inputs, initializers and captures retain protection minima through storage and retries. A null needs no external
payload, but keeps its original storage minimum and profile. Substitution runs before encoding, so an incompatible
driver or encryption requirement refuses a new scalar before storage. A graph output mapping refuses a value whose
protection or lifetime its pinned public result contract cannot preserve.

Expressions and completion-value forms do not universally require `return`. A Run JavaScript function body whose
result is bound must contain a return statement; a path that produces no value still uses the undefined boundary at
runtime. An unbound result writes nothing. Sparse array positions stay in place: holes and undefined elements become
null placeholders with `VF-UNDEF-003`. Computed NaN and infinity become null with `VF-NUM-001`. Existing execution
diagnostics show the codes, severity and permitted paths; protected reads restrict paths to the root.

Model-owned BPMN targets retain their own declared-default admission and optional omission. Native workflow workers
called by a model use the exact pinned worker's policies. The bounded authored BPMN model profile refuses strategy
overrides and explicit-null defaults instead of silently ignoring them.

## Elsa 3 conversion

`Elsa3MappingOptions.ApplyElsa3NullPolicies` defaults to true. Its corresponding engineer feature option records both
policies in converted definitions; setting it false keeps native Elsa 4 rules. For example, a converted missing-property
binding yields null with the option enabled and `VF-UNDEF-001` with it disabled. The converter no longer rewrites
expressions with `?? null`. The conversion report identifies the recorded choice.

See the [converter guide](../../extensions/Elsa3/src/Mapping/README.md) and
[migration readiness](elsa3-migration-readiness.md) for unsupported declarations and built-in activity differences.
