# Contract: Workflow state in a step body

This is the proposed author/runtime contract for coordinator approval. Identifiers below are concrete; additive API compatibility and public surface baselines are checked in implementation.

## Native surface

`ActivityExecutionContext.WorkflowState` exposes `IStepWorkflowState` from Activities.Runtime.Core:

```csharp
bool TryGetOutput(string name, out JsonElement value);
void SetOutput(string name, JsonElement value);
string? CorrelationId { get; set; }
string? InstanceName { get; set; }
```

Native output values are explicit JSON, the existing dynamic value world; null is `JsonValueKind.Null`, and undefined is refused. `TryGetOutput` distinguishes absent from null without a nullable transport ambiguity. Returning a clone prevents ambient mutation. A convenience typed serializer API is not required by this work unit. The capability exposes no arbitrary metadata, activity result, service locator or input setter.

Every body-created output has `ValueTypeDescriptor(TypeAliasConvention.AnyAlias)` with Single collection kind. A new unprotected slot defaults to `ValueProtectionPolicy.InstanceInline`. For replacement, keep the existing output slot's policy/storage as destination minima, while replacing its payload/type with Any: a prior type is not a declared schema constraint. Combine source protection minima and destination requirements through `ValuePolicyCombiner`; use the existing selected storage encoder and replace stale external references with the newly encoded payload reference. Refuse incompatible or unavailable external/custom/encrypted encoding through existing domain diagnostics instead of silently falling back to inline or weakening protection. Retention/redaction/storage-profile requirements survive the combination. Merely loading destination policy does not count as reading its payload for body taint; an actual read does. This destination-policy selection is an explicit S-13 rule; the current intrinsic helper itself only consumes the supplied envelope and does not load prior policy. No author-selectable storage/type option is added here. Test new slots, typed/external replacements, explicit null, protected destination with constant replacement, protected source read and incompatible policy refusal.

An activity type declares `[AccessesWorkflowState]`. Its contract pins the signal independently of variable-reference inputs/free-name markers; unmarked use is a sticky `VF-STATE-002` refusal. Add the context property without changing existing constructor signatures or required members on third-party implemented interfaces. Extend an existing interface only with a compatible default member, or introduce the narrow capability contract instead.

## Script surface

Only the script-body evaluation request may carry `IScriptWorkflowState`, a narrow JSON/string adapter in Expressions.JavaScript.Core. Jint installs a non-replaceable, non-extensible `workflowState` object with:

```javascript
workflowState.getOutput(name)       // JSON copy; undefined when absent, null when explicitly null
workflowState.setOutput(name, value) // undefined return; explicit JSON assignment
workflowState.getCorrelationId()     // string or null, including this evaluation's own writes
workflowState.setCorrelationId(value)
workflowState.getInstanceName()
workflowState.setInstanceName(value)
workflowState.requireExactIdentity(value) // string/null unchanged; sticky refusal if native normalization would change it
```

Setters return `undefined`, accept exact arity and reject spread/unsupported payloads at the appropriate analysis/runtime boundary. Methods are host capabilities, not CLR proxies; prototype manipulation, property overwrite, `args.workflowState` or a hand-built object grants no access. Ordinary input evaluators never install this capability. Invalid calls latch a host-side refusal checked after user code and value serialization even when caught in JavaScript.

Output assignment uses the existing JSON bridge/precision checks. Reading/changing an object copy without a setter is local only. `setOutput('A', {n: 1}); getOutput('A').n` reads 1 through the same pending session. No new `getOutputFrom` or arbitrary activity-result lookup is added.

## Validation and diagnostics

| Code | Boundary | Meaning / guidance |
|---|---|---|
| `VF-STATE-001` | Publication when provable; otherwise execution | Invalid output name, identity kind, arity or missing/unserializable value; use a nonempty string name, JSON output value or string/null identity. No value in the diagnostic. |
| `VF-STATE-002` | Publication and execution | Missing declaration or unavailable inline leaf capability; use a declared inline body. Input expressions, structural/background/closed graph contexts do not supply it. |
| `VF-STATE-003` | Execution | Existing protection/precision/storage rules refuse the state write/read; retain the underlying stable domain code where one already exists, without quoting protected values. |
| `VF-STATE-004` | Authoring/inspection warning | Actual same-address step-write/capture overlap; capture wins. Distinct variable/output namespaces do not trigger it. |
| `ELS3-STATE-IDENTITY-NORMALIZATION` | Import literal / execution guard | The source identity string would change under native whitespace-to-null normalization. Rewrite the source to use explicit null or a nonblank identity before import. |

Input-expression import refusals keep `VF-IMP-005` with body/intrinsic guidance. Existing `VF-BG-001 (Cn)` remains the background eligibility format; extend the correct C9 reason for this capability. Existing graph/reusable guidance/codes remain and link the graph limitation. No diagnostic invents support for deferred graph writes.

## Conversion

| Elsa 3 direct body call | Lowering |
|---|---|
| `setOutput(name,value)` | `workflowState.setOutput(name,value)` |
| `setCorrelationId(value)` | `workflowState.setCorrelationId(workflowState.requireExactIdentity(value))` |
| `getCorrelationId()` | `workflowState.getCorrelationId()` in bodies only |
| `setWorkflowInstanceName(value)` | `workflowState.setInstanceName(workflowState.requireExactIdentity(value))` |
| `getWorkflowInstanceName()` | `workflowState.getInstanceName()` in bodies only |

Preserve argument source ranges, nested evaluation order and source setter `undefined` value, including a last-expression return. Use the existing rewriter's lexical scopes/root-collision checks, not string replacement. Declared local helper names stay local. A collision with `workflowState`, helper function escaping/alias, unsupported spread or wrong arity keeps/refines `VF-IMP-005` or `VF-IMP-006` with the exact helper/source location and how to rewrite it; do not emit unsafe lowered code. Computed output names convert and get conservative effect metadata, then validate at runtime. Native `setInput`/`setResult` equivalents are not added.

Elsa 3's identity setters assign strings directly, including empty/whitespace strings, whereas native identity setters normalize them to null. Refuse those literal imports with `ELS3-STATE-IDENTITY-NORMALIZATION`. For computed values the bounded `requireExactIdentity` guard evaluates once, returns a nonblank string or explicit null unchanged, and latches the same code before any setter runs if normalization would change the value. The guard is a generic value check, not an Elsa 3 runtime mode; it has no write side effects and its refusal cannot be swallowed by a user catch. Unsupported kinds/undefined use the existing invalid-value refusal. Test `setCorrelationId(' '); return getCorrelationId() === null;`, the instance-name equivalent, computed empty/whitespace, and caught guard refusal as negative controls. Native authors who call an ordinary setter directly retain the existing normalization contract.

Converted correlation/name reads in supported bodies must use the session even if no write appears lexically before them (branches/functions can order execution differently). Other pinned identity/input/output arguments keep their existing semantics. In input expressions, do not lower a getter into this root; existing supported pure identity reads remain pinned, and existing unsupported reads remain refused.

## Commit, scope and protection

Use S-7's inline leaf evaluation lifetime and existing completion/suspension checkpoint. Workflow header changes and identity projections must merge with variable-frame changes; output rows share the existing output IDs. All preparation must succeed before acceptance. A captured/replayed completion with output/identity-only effects cannot omit them. No extra persistence boundary or separate write history.

Inside an ordinary nested structural container the capability still targets the current running workflow. At a closed reusable graph boundary it is unavailable; no remapping to graph outputs or parent identity is inferred. Invocation/resume only; no structural callback or background session. Publication and runtime both enforce these limits using pinned metadata, not just script-text heuristics.

Protected input/session/output reads taint the evaluation conservatively, including reads occurring after assignments. Output values preserve that policy; public identity and output names cannot hold protected envelopes and are refused when tainted. Never rely on source string analysis to declassify a constant-looking assignment. Underlying withheld/precision/storage refusals remain sticky and use value-free messages.

Changing correlation produces no bookmark/registration/stimulus-index changes. A later wait reads its own input, which may explicitly bind the updated identity. Its existing bookmark key is never recomputed from current workflow state.
