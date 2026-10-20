# Data and UI state

No persisted model or endpoint is added.

| Existing concept | Use and invariant |
|---|---|
| Catalog input descriptor | Optional tier/requiredness metadata guides presentation; unknown/absent tier stays visible. Tier never changes runtime requiredness. |
| ActivityNode input | Opaque referenceKey identifies existing authored state. Disclosure only reads it; method changes edit Method alone. |
| Wrapped expression / ArgumentState | Expression type/value, conversion, structured references and unknown extras remain lossless. Stored and synthesized defaults are distinct. |
| Category/propertyGroups | Existing labels and order remain coherent in direct and secondary partitions. |
| Local row visibility snapshot | Transient current-row validation-error/options-failure booleans only. Scoped to activity/input identity; removed/stale rows cannot force disclosure. No values, credentials or diagnostics are copied into workflow state. |
| Optional editor reporting capability/callback | Client-only additive `supportsValidationReporting?: true` and `onValidationChange?: (hasError: boolean) => void`. Unknown contributions stay directly visible. Per-mounted-editor error aggregation preserves sibling errors and releases obsolete instance/scope reports; no values or diagnostic text cross the callback. |
| Disclosure state | UI-local open/closed state using the existing shared component. HTTP opts into configured collapse with a set-count badge; current errors, unobservable editors and failed options force open. Other callers retain existing defaults; hidden children remain mounted. |

The only backend metadata change is HTTP Connection tier Optional→Essential; it remains optional at execution. There is no migration or new storage field.
