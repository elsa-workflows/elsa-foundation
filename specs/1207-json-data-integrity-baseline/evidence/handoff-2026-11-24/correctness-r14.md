# S-9 independent correctness review, round 14

Reviewed exact source head: `04ae1285c2f8b3eef1a464c28adea65781724c62`.
Initiative comparison: `2db2d8a899564fdc98a823157e68ad71e2ec7e62`.
Reviewer: `/root/s9_review_correctness_r12`, independent source review.

**Verified actionable correctness findings: none.** This is a clean source-review result, not an execution,
acceptance, final receipt-head, or merge-readiness claim. No build, Docker command, source edit, commit, private
corpus access or CI wait was performed. Only this ignored review receipt was written.

## Repaired findings and reviewed deltas

Rechecked both integrated S-2 default serializers fixed by `1ca8ace5eec531ed4b26efdcd58d7a8ea3609322`.
GraphActivityProvider and RuntimeInputBindingResolver pass the shared baseline to their runtime-Type writers;
their supported-default, nullability, protection-policy and retained-Absent decisions are unchanged. Both source
owners have runtime-Type negative controls, and the resolver joins the protected workflow-data set. The bounded
search for remaining optionless STJ runtime-Type writers found no additional production site.

Read the explicit HTTP DOM target repair and its shape/null controls. Object/Any retains a cloned JsonElement;
explicit JsonObject/JsonArray/JsonValue targets go through the actual requested conversion contract. Null/string
transport return behavior remains intact. No native SendHttpRequest-to-parser path was assumed.

Independently read every production delta since the preceding review through this exact head:

- **BPMN document projection (`c8f8fe7dfc`)**: typed baseline serialization retains child literal/default and extension
  JsonElements before the optional empty scopeHeaders omission. Removing an absent compatibility header uses a
  dictionary of raw child elements, rather than a JsonNode re-emission. Explicit empty headers remain present.
  The document handler still validates the resulting structure. Inspected the no-op and actual identity-replacement
  controls, nested native Sequence data, process/scope/native defaults, extension metadata and source immutability.
- **BPMN persisted-state codec (`9198bf43a3`)**: the DOM is used only for existing shape validation. Actual values are
  read from the original JsonElement; v1 activeChildren/producingActivityExecutionId translations reconstruct only
  those owned wrappers, preserving loop items. Writes preserve the requested original state version and invocation
  correlation. Existing conflict checks and host exception wrapping remain. Inspected read/write/cold-reread controls
  for both versions, exact wide numbers/escape spelling/null items and case-distinct correlation keys. No old hash
  verifier or state-version restamping was introduced.
- **Agent completion (`7fd9a22ad9`)**: enabled completion keeps each accepted result member as a cloned JsonElement
  and adds only the conversation metadata. Disabled completion retains the entire result. Existing object-shape,
  duplicate-key and reserved-output refusal semantics remain. Inspected the in-memory/durable authenticated-journal
  to native completion/replay controls, including legacy/current controls profiles and raw result members.
- **Protected trigger configuration (`04ae1285c2`)**: the recursive edit rebuilds only changed object ancestors.
  Omitted versus explicit-null protected values remain distinct; proposed explicit values retain the former
  precedence; missing object parents are created only for retained protected values, while non-object parents retain
  the prior behavior. Literal dotted keys and nested paths are both handled. Reads still redact and saves still call
  configuration validation before store writes. Inspected actual management read/list/preview/apply controls,
  restored object/array/null secrets, raw public siblings, validation refusal and no-change whole-value preservation.

The graph test-fixture adjustments use the canonical Any capability and admitted JSON literal shape; they do not
weaken production binding checks. The SDK result-file tests now accept JsonException subclasses without weakening
the missing/invalid-result failure requirement. The structured result file remains separate from warning output,
and exact compiler reference identity/MVID/SHA/alias evidence remains required.

## Whole approved scope check

Carried forward my independently inspected unchanged source from round 12 and checked the current boundaries:
shared read-only baseline/copies; raw JsonElement writes and paired depth checks; scoped type/member conversion,
unmatched-key traversal and operation-local metadata; strict temporal API/conversion modes; Elsa 3 key/date
interpretation and exact portable expression producers; native/Switch/graph/placement raw values; canonical exact
numbers and null/missing equality; JsonHashInput and opaque/runtime projections; Git raw outer rendering; and
Studio's selected-zone date display/input helpers. No additional concrete failing case was source-verified.

Final Q9/Q10/Q11 remain applied: code/package-scoped conversion selection without executable stamps, no host-wide
run-time date interpretation, and one canonical hash authority with the pre-stable regeneration/first-stable freeze
rule. S-2 workflow null/missing/undefined/admission/write policies were not redesigned. Existing VariableValueDigest
semantics, its 19 retained vector records and pins were not changed by this review.

The owner removed composed/visual Studio acceptance from this stream. I inspected source only and neither ran nor
required that acceptance. Parent-reported negative/positive runs are not independently certified by this receipt.
Affected whole suites, complete source/deviation/hash receipts, remaining failed or not-run states, final artifact-head
review, external D-18 review and coordinator merge remain separate qualification or formal gates.
