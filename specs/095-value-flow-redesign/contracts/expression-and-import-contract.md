# Expression And Elsa 3 Import Contract

This contract defines portable input-binding expressions and the one-way Elsa 3 memory-reference
lowering boundary. Expression evaluation is a pure function of serialized source, serialized options,
and explicitly bound immutable parameters.

## 1. Canonical expression model

```csharp
public sealed record ExpressionDefinition(
    string Language,
    string Source,
    TypeReference ResultType,
    IReadOnlyDictionary<string, ExpressionParameterBinding> Parameters,
    JsonElement Options,
    string CapabilityProfile);

public sealed record ExpressionEvaluationRequest(
    ExpressionDefinition Definition,
    IReadOnlyDictionary<string, object?> ParameterValues,
    CancellationToken CancellationToken);

public interface IExpressionEvaluator
{
    ValueTask<object?> EvaluateAsync(ExpressionEvaluationRequest request);
}
```

Evaluator infrastructure is supplied through evaluator/handler constructor injection. The request
contains no service provider, workflow/activity context, variable frame, output register, memory
register, mutable host object, or parent context.

Each parameter binding is exactly one of:

```text
Literal(value)
WorkflowRequest(memberKey)
Variable(declaringScopeNodeId, variableKey)
ActivityResult(producerNodeId, projectionKey)
WorkflowInstance(member)        // member: correlationId | instanceId
```

`WorkflowInstance` was added by the ADR 0045 amendment for issue #2717 (spec 247). It reads the running
instance's correlation ID or instance ID from committed runtime state as a declared parameter, so it satisfies
the "instance identity declared as a parameter" rule below.

Nested expressions are not an implicit parameter kind. Authors compose expressions explicitly or
bind one expression result as the enclosing activity input. Every dynamic parameter is resolved and
pinned as part of the consumer invocation's complete input snapshot.

## 2. Serialized shape

```json
{
  "language": "JavaScript",
  "source": "args.subtotal + args.tax",
  "resultType": { "alias": "Decimal", "collection": "None" },
  "capabilityProfile": "binding-readonly-v1",
  "parameters": [
    {
      "name": "subtotal",
      "binding": {
        "kind": "WorkflowRequest",
        "memberKey": "subtotal"
      }
    },
    {
      "name": "tax",
      "binding": {
        "kind": "Variable",
        "declaringScopeNodeId": "root",
        "variableKey": "tax"
      }
    }
  ],
  "options": {}
}
```

Serialization invariants:

- parameter names are unique using ordinal comparison and are rendered in ordinal order for hashing;
- every parameter carries exactly one legal binding payload;
- source, language, result type, capability profile, all parameter bindings, options, metadata, and
  effective value-protection policy participate in the behavioral artifact hash;
- types use aliases/schema descriptors, never assembly-qualified CLR names;
- JSON values are cloned/immutable at the model boundary;
- options are language-specific portable data validated at publication; and
- no delegate, closure, expression tree, service, host callback, or runtime object is serializable.

## 3. Language surfaces

JavaScript receives a read-only `args` object:

```javascript
args.customer.id + ":" + args.orderNumber
```

Liquid receives only declared parameter roots:

```liquid
{{ customer.id }}:{{ orderNumber }}
```

Both engines MUST materialize dynamic `Any` parameters consistently from fresh `JsonNode` and durable
`JsonElement` forms. Mutation of the engine-local object MUST NOT write workflow state and SHOULD be
prevented/frozen where supported.

The `binding-readonly-v1` capability profile (ADR 0083 amendment, epic #3052 decisions D-1, D-5, D-6
and D-11; it replaces `binding-pure-v1`) permits value transforms that read workflow values and never
change engine-owned workflow state. They need not be deterministic.

It permits:

- the current time, random values and GUID generation. Evaluators run in UTC with the invariant culture, and
  time comes from the host's `TimeProvider`;
- functions implemented in .NET that are registered for this profile and exchange only JSON values with
  the expression. A call is never refused or skipped because of what it does, and the workflow designer
  owns the idempotency of external effects (D-6). No function profile is required; a durability hint, if
  the D-2 details need one, is defined there.

It forbids:

- ambient `variables`, `input`, `output`, or `ExpressionExecutionContext` objects;
- `getVariable`, `setVariable`, named variable getters/setters, `getInput`, `getOutput`, and
  `getOutputFrom` host functions;
- workflow/activity/instance identity not declared as a parameter;
- live .NET objects crossing into the expression. Scripts that may use objects the host explicitly
  registers use a separate profile (for example `binding-readonly-interop-v1`; analysis A-1 and the
  ADR 0046 amendment, V10). A host opt-in allows that profile, and it is pinned in the executable like any
  profile, so this profile always forbids live objects;
- any change to engine-owned workflow state: variable mutation, workflow output publication,
  correlation or instance-name changes, and outcome selection;
- functions not registered for this profile, added through evaluation events, filters,
  preprocessors, or postprocessors; and
- nonpersistable results in durable execution.

The first two forbidden items stay until
[ADR 0089](../../../docs/adr/0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md)
(proposed, decision D-7), or its A-4 amendment
([#3420](https://github.com/elsa-workflows/elsa-foundation/pull/3420)), is accepted. It lets `variables.X` and `getVariable('X')` read through
publish-resolved declarations.

A result of `undefined` is an error unless the workflow selects the undefined-as-null policy (ADR 0045 §2
amendment, D-11). Under that policy, an `undefined` result and every `undefined`-valued object property
materialize as `null`.

JavaScript/Liquid extension registration MUST declare the capability profiles in which it is allowed.
The evaluator builds its processor/filter set from that allowlist. A global extension registration
cannot silently join a capability profile.

Stateful scripts, configuration-dependent scripts, or scripts with workflow-visible side effects are
activities. They receive plain inputs, return a typed result, and use a separate graph-visible `Set`
intrinsic when a variable must change.

## 4. Evaluation ordering and failures

For each expression input the runtime MUST:

1. validate the definition and capability profile;
2. resolve every parameter binding from one structural/causal runtime view;
3. type-check, policy-check, and snapshot parameter values;
4. evaluate exactly once for the new logical invocation;
5. coerce and validate the declared result type;
6. validate result persistability/protection policy; and
7. include the final input value in the complete invocation snapshot before activity activation.

Retry and resume do not reevaluate an already pinned input expression. An absent or ambiguous required
parameter fails before evaluator invocation. Evaluator exceptions are wrapped with language,
executable node, input key, and expression fingerprint while preserving cancellation. Sensitive
source values and results are redacted according to their effective policy.

## 5. Elsa 3 importer algorithm

Elsa 3 DTOs and memory-reference semantics exist only in the importer module. Import uses two passes.

### Pass 1: inventory

For every typed activity property, record independently:

```csharp
public sealed record Elsa3MemoryOccurrence(
    string MemoryReferenceId,
    string JsonPath,
    string ActivityNodeId,
    string ActivityPath,
    string StablePropertyKey,
    Elsa3PropertyDirection Direction,
    string StructuralFrameId,
    Elsa3Expression? Expression);
```

The inventory MUST retain output-only `{ "memoryReference": ... }` values and combined
`{ "expression": ..., "memoryReference": ... }` values. It MUST NOT require an expression in order
to record an output occurrence.

### Pass 2: validate and lower

The importer resolves each memory id within its structural frame:

- Elsa 3 workflow input -> canonical workflow-request binding;
- Elsa 3 variable id -> canonical variable declaration/read by stable key;
- unique activity output producer -> canonical activity-result projection;
- literal/object expression -> canonical literal;
- safe JavaScript/Liquid expression -> canonical pure expression with declared parameters; and
- loop/carried value -> explicit canonical state transfer when structurally representable.

An output-only occurrence establishes the producer/result projection relationship. A combined value
is split: its expression becomes the property's input binding and its memory reference records the
property's produced result relationship. Neither half may be discarded.

The lowering result is normal Elsa 4 authored state. It contains no memory-reference id unless that
id is retained solely as non-behavioral migration provenance metadata outside canonical value
identity.

## 6. Elsa 3 expression rewriting

Language-specific importer analyzers may rewrite only statically provable ambient reads.

Examples:

| Elsa 3 source | Canonical source | Added parameter |
| --- | --- | --- |
| `variables.total * 1.2` | `args.total * 1.2` | `total -> Variable(scope,key)` |
| `getVariable("total")` | `args.total` | `total -> Variable(scope,key)` |
| `input.customerId` | `args.customerId` | `customerId -> WorkflowRequest(customer-id)` |
| `getOutput("receiptId")` | `args.receiptId` | `receiptId -> ActivityResult(node,receipt-id)` |
| `{{ Variables.customer.name }}` | `{{ customer.name }}` | `customer -> Variable(scope,key)` |

Rewriting MUST use a JavaScript parser or Liquid syntax tree appropriate to the registered language;
regex-only semantic rewriting is forbidden. Property names, string literals, comments, and escaping
must remain correct.

The importer MUST fail rather than guess when source uses computed variable/output names, dynamic
property discovery, variable assignment, setter host calls, ambient configuration/time/randomness,
unknown host functions, multiple possible producers, or a producer outside the visible structural
frame. A custom expression language requires an explicit importer lowering provider or fails as
unsupported.

## 7. Import diagnostics

Importer diagnostics are accumulated where possible. Each includes source name, workflow definition
id, activity node id/path, property stable key, exact JSON path, memory id when present, and actionable
guidance.

| Code | Required condition |
| --- | --- |
| `VF-IMP-001` | Memory reference is dangling or has no producer/declaration |
| `VF-IMP-002` | Memory reference has multiple or ambiguous producers |
| `VF-IMP-003` | Reference crosses a subtree/frame boundary without a legal explicit transfer |
| `VF-IMP-004` | Output-only or combined reference shape cannot be resolved |
| `VF-IMP-005` | Script performs mutation or uses a forbidden ambient capability |
| `VF-IMP-006` | Computed/dynamic accessor cannot be statically lowered |
| `VF-IMP-007` | Custom memory or expression shape has no registered lowering provider |
| `VF-IMP-008` | Lowered value is incompatible with the target stable member/type/policy |
| `VF-IMP-009` | Elsa 3 live workflow-instance state was supplied for resume |

Definition-level `DefinitionMappingFailed` may summarize an unexpected importer failure, but it MUST
NOT replace available path-specific diagnostics for known incompatibilities. A failed construct is
never silently converted to `null`, a default, a literal memory id, or a canonical compatibility
object.

## 8. Conformance fixtures

Expression conformance MUST run the same fixture set for JavaScript and Liquid where the language can
represent the operation:

1. literal, workflow request, variable, causal result, and workflow-instance parameters round-trip and evaluate;
2. parameter order does not change the artifact hash;
3. parameter binding, options, result type, source, or capability change does change the hash;
4. restart/retry uses the pinned value without reevaluation;
5. fresh and durable `Any` values produce equivalent reads;
6. undeclared ambient reads, all mutation forms, service/configuration access, and nondeterministic
   helpers are rejected;
7. expression results cannot downgrade source sensitivity/protection; and
8. no processor, filter, or event can add a forbidden host capability to the pure profile.

Importer fixtures MUST include literals, variables, workflow inputs, unique prior outputs,
output-only references, combined expression/reference values, JavaScript, Liquid, loops, and nested
containers. Negative fixtures cover every `VF-IMP-*` code. A successful fixture MUST publish an
executable with zero references to Elsa 3 DTOs, memory contracts, memory ids as value identity,
ambient expression carriers, or compatibility shims. Every failure asserts the exact JSON path and
stable diagnostic code.

## 9. Forbidden canonical dependencies

Expressions Core, language evaluators in the pure profile, Workflows Runtime, and published executable
models MUST NOT reference `IExpressionExecutionContext`, `IExecutionExpressionState`,
`IMaterializationExpressionState`, `IMemoryBlock*`, `IMemoryRegister`, delegate expressions,
JavaScript variable writeback, Elsa 3 DTOs, or importer reference-table types. If an activity-script
compatibility profile exists during migration, it is explicitly named, cannot evaluate canonical
input bindings, and has a recorded removal milestone.
