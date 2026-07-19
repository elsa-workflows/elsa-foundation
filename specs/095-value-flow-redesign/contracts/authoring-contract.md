# Code-First And Dynamic Authoring Contract

This contract defines the developer-facing workflow composition model and the equivalence boundary
between code-first, visual, and API/JSON authoring. Authoring objects are compiler inputs. They are
never workflow-instance state and are never executed by the runtime.

## 1. Workflow definition entry point

The recommended code-first entry point is IDE-guided inheritance:

```csharp
public abstract class WorkflowDefinition<TRequest, TResult>
{
    protected abstract void Build(IWorkflowBuilder<TRequest, TResult> workflow);
}
```

Example:

```csharp
public sealed record OrderRequest(string CustomerId, decimal Total);
public sealed record OrderResult(string ReceiptId);

public sealed class ProcessOrder : WorkflowDefinition<OrderRequest, OrderResult>
{
    protected override void Build(IWorkflowBuilder<OrderRequest, OrderResult> workflow)
    {
        var retries = workflow.Variable<int>("retries", initialValue: 0);

        var charge = workflow.Sequence.ChargeCard(
            customerId: workflow.Request.CustomerId,
            amount: workflow.Request.Total);

        workflow.If(
            charge.Outputs.Approved,
            then: branch => branch.Return(new OrderResult(charge.Outputs.ReceiptId)),
            @else: branch => branch.Set(retries, retries.Value + 1));
    }
}
```

`Build` runs while compiling or publishing a definition version. It MUST NOT run when a workflow
instance starts, retries, resumes, or migrates. A definition object, builder, call handle, variable
handle, or generated facade MUST NOT be serialized into the executable.

Equivalent declared build inputs MUST produce the same authored state and behavioral artifact
fingerprint. Definition-time configuration that changes behavior is a declared build input and MUST
produce a new version/fingerprint.

## 2. Foundational builder contract

The non-generated API makes literals and dynamic sources explicit:

```csharp
public interface IWorkflowBuilder<TRequest, TResult>
{
    WorkflowRequestSource<TRequest> Request { get; }

    Variable<T> Variable<T>(string name, T? initialValue = default);

    ActivityCall<TResultValue, TOutputs, TOutcomes> Add<
        TActivity,
        TResultValue,
        TOutputs,
        TOutcomes>(
        Action<IActivityInputBuilder<TActivity>> inputs);

    void Set<T>(Variable<T> variable, WorkflowValue<T> value);
    void Return(WorkflowValue<TResult> result);
}

public interface IActivityInputBuilder<TActivity>
{
    IActivityInputBuilder<TActivity> From<T>(string inputKey, WorkflowValue<T> source);
    IActivityInputBuilder<TActivity> Value<T>(string inputKey, T? literal);
}
```

`.From(source)` accepts only a typed workflow request member, visible variable read, causally
available result projection, or expression result. `.Value(literal)` always emits a literal binding.
Neither returns or creates a runtime `ValueSource<T>` object.

Sequence builders append calls in source order. Structured builders create explicit lexical regions.
Connections are therefore derived from the owning builder operation or selected call outcome, not
from chaining an activity CLR instance.

## 3. Generated activity-call facade

For an activity contract, the generator emits a method equivalent to:

```csharp
public static ActivityCall<
    ChargeCardResult,
    ChargeCardOutputSources,
    ChargeCardOutcomeSources> ChargeCard(
        this ISequenceBuilder sequence,
        ActivityArgument<string> customerId,
        ActivityArgument<decimal> amount);
```

`ActivityArgument<T>` is an authoring-only carrier. It supports implicit conversions from:

- a literal assignable to `T`;
- a `WorkflowRequestMember<T>`;
- a `VariableRead<T>`;
- an `ActivityResultProjection<T>`; and
- an `ExpressionSource<T>`.

Explicit factories MUST exist for ambiguous cases:

```csharp
ActivityArgument.Value<T>(literal)
ActivityArgument.From<T>(source)
ActivityArgument.Null<T>()
ActivityArgument.Default<T>()
```

An omitted optional argument, explicit `null`, and `default(T)` are three different authored states.
Publication normalizes each accepted state to one concrete canonical binding. A required omitted
argument fails publication.

The returned handle has a consistent shape:

```csharp
public sealed class ActivityCall<TResult, TOutputs, TOutcomes>
{
    public ActivityNodeHandle Node { get; }
    public ActivityResultSource<TResult> Result { get; }
    public TOutputs Outputs { get; }
    public TOutcomes Outcomes { get; }
}
```

- `Node` is authoring identity/control placement, not a runtime invocation.
- `Result` is the entire immutable typed activity result.
- `Outputs` contains typed read-only projections using stable result-member keys.
- `Outcomes` contains typed control-edge selectors and no data values.
- Fault, cancel, and suspend are runtime transitions and MUST NOT appear as successful authored
  outcomes.

The generated facade MUST contain no runtime execution code. It immediately lowers arguments and
handles into normal authored `ActivityNode`, `ArgumentState`, structure, and connection records.

## 4. Variables and structured values

```csharp
public sealed class Variable<T>
{
    public string ReferenceKey { get; }
    public VariableRead<T> Value { get; }
}
```

`Variable<T>` is a declaration handle and carries no runtime value. Identity is the stable reference
key plus declaring lexical scope; name is display metadata. Reads are legal only in the declaring
scope and descendants. Siblings cannot address each other's locals.

`Set(variable, value)` emits a graph-visible engine intrinsic. It is the only ordinary variable-write
operation. It does not instantiate a CLR activity. Potentially concurrent writes to the same frame
and variable key fail publication unless enclosed by an explicit deterministic merge or reduction.

Structured builders expose values only through their declared result:

- every successful branch of `If<T>` returns `T`;
- loops collect by stable iteration/source identity;
- parallel regions merge by stable branch identity and an explicit merge function/operation; and
- cycles use explicit state transfer, never a latest-result lookup.

A required source MUST be definitely available on every path to its consumer. Otherwise the author
must use an explicit optional/union value or merge.

## 5. Child workflows and reuse

A child workflow call binds a complete typed request and returns a complete typed result:

```csharp
ChildWorkflowCall<TChildResult> Invoke<TChild, TChildRequest, TChildResult>(
    WorkflowValue<TChildRequest> request);
```

Parent and child workflows do not share variables, result registers, activity instances, service
instances, or expression contexts. Ordinary C# builder extension methods are the source-level reuse
mechanism. A separately versioned reusable runtime component is a child workflow; this contract does
not introduce a fragment runtime model.

## 6. Canonical authored serialization

Generated and foundational calls lower to the same authored shape as visual/API authoring:

```json
{
  "nodeId": "charge-card",
  "activityVersionId": "payments.charge-card@2",
  "inputs": [
    {
      "referenceKey": "customer-id",
      "binding": {
        "kind": "WorkflowRequest",
        "memberKey": "customer-id"
      }
    },
    {
      "referenceKey": "amount",
      "binding": {
        "kind": "WorkflowRequest",
        "memberKey": "total"
      }
    }
  ],
  "structure": null
}
```

The serialized form MUST use stable keys and alias/schema type references. It MUST NOT contain:

- generated CLR type names or `ActivityArgument<T>`;
- delegates, expression trees, closures, or builder callbacks;
- an activity CLR object or constructor arguments;
- runtime invocation, attempt, scope, or service identity;
- assembly-qualified type names; or
- memory blocks, argument wrappers, or universal value references.

## 7. Canonical equivalence

Two authoring sources are semantically equivalent when publication produces canonical executable
models with identical:

- activity contract/version/fingerprint pins;
- node and structured-region identities after deterministic normalization;
- normalized input bindings, expression definitions, defaults, and policies;
- stable result projections and control edges;
- variable declarations, lexical ownership, and intrinsic operations; and
- behavioral artifact hash.

Layout, source-file location, generated helper names, builder object identity, and harmless authoring
ordering that canonical normalization explicitly sorts do not affect behavioral identity.

The conformance suite MUST construct each shared fixture through generated code-first, foundational
fluent, and dynamic/API authoring. It then compares the normalized executable serialization and
artifact hash byte-for-byte. A syntax path MUST NOT receive special validation or runtime execution.

## 8. Source-generator obligations

The incremental generator reads referenced activity contract metadata and emits typed methods,
argument nullability/defaults, result/output handles, and outcome handles. It MUST:

1. produce deterministic output for deterministic inputs;
2. use stable contract/member keys in lowering code;
3. report compile-time diagnostics for unsupported, duplicate, or ambiguous public contract shapes;
4. avoid loading or instantiating activity CLR types to discover runtime values;
5. keep Roslyn dependencies inside the code-generation project/consumer build; and
6. generate no runtime package dependency on Design or code generation.

Generated, manually described, and reflection-discovered activity contracts MUST pass the same
contract-equivalence fixtures. Reflection is a compatibility fallback, not a different semantic
model.

## 9. Required diagnostics

| Code | Required condition |
| --- | --- |
| `VF-AUTH-001` | Required generated/foundational argument omitted |
| `VF-AUTH-002` | Literal/source conversion is ambiguous or incompatible |
| `VF-AUTH-003` | Variable is outside its lexical visibility region |
| `VF-AUTH-004` | Result producer is not structurally and causally available on every path |
| `VF-AUTH-005` | Concurrent variable write lacks deterministic merge/reduction |
| `VF-AUTH-006` | Structured successful path does not return its declared result |
| `VF-AUTH-007` | Definition build is nondeterministic for the same declared inputs |
| `VF-AUTH-008` | Generated/manual/reflection contract metadata disagree |
| `VF-AUTH-009` | Child request/result contract is incomplete or incompatible |

Diagnostics include definition version, node/region id, stable member or variable key, and source
location when known. They MUST be identical in semantic code and message parameters across generated,
foundational, visual, and API publication paths.

## 10. Forbidden authoring behavior

The authoring API MUST NOT accept activity object instances as workflow nodes, execute `Build` per
workflow instance, serialize callbacks or CLR closures, expose mutable runtime storage, share
variables implicitly with child workflows, resolve outputs by globally latest activity execution, or
introduce a code-first-only executor. Canonical Runtime packages MUST NOT reference the builder,
generator, `ActivityArgument<T>`, or call-handle types.
