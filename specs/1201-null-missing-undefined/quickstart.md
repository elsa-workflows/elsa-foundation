# Quickstart: validating S-2

Run after implementation (not now: this spec is provisional).

1. **Explicit null (US1)**: publish a workflow whose `Set` writes literal `null` into an `Object` variable; run it and
   read the variable: explicit null. Repeat with an `Int32` variable: publication fails with `VF-ACT-004`.
2. **Omitted inputs (US2)**: start `composition-bulk-dispatch-sub-workflow` (JTest corpus) with no input. It completes;
   no poisoned work item. Start a workflow with a required input omitted: refused with `VF-WF-REQ-001`, no instance step.
3. **Default-on-null (US3)**: bind `return null;` to an `Int32` input. Without the policy: `VF-ACT-004`. With
   `nullHandling.defaultOnNull`: input `0`, `VF-VAL-NULL-DEFAULTED` on the invocation.
4. **Undefined (US4)**: evaluate `({ a: undefined, b: NaN })`. Default: `{ "b": null }` plus two warnings. With
   `undefinedAsNull`: `{ "a": null, "b": null }` plus one warning.
5. **Converter (US5)**: convert an Elsa 3 definition, export, import on a host without `Elsa3Mapping`, run: same
   outputs. The exported JSON shows `nullHandling`.
6. **Computed read (US6)**: `getVariable('no' + 'pe') === undefined` is `true`; `VAR-NOT-FOUND` is recorded.
7. **Storage (US7)**: suspend and resume a workflow with one explicit-null input and one omitted input, on each EF
   provider.
8. **Measurement**: rerun the #3052 JTest measurement and post it on #3052.
