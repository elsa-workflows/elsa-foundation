# Elsa 4 value-flow migration

Spec 095 is a major-version replacement of the Elsa 3 memory-block and argument-wrapper programming
model. There is no forwarding adapter in canonical Elsa 4 runtime packages. The canonical meanings of
activity input, activity result, variable, private state, and trigger payload are defined in the
[Elsa glossary](../glossary/elsa.md); this note only explains how authors migrate code.

## Activity authors

Replace `InputArgument<T>` properties with ordinary CLR properties annotated by `[ActivityInput]`.
Return one closed `ActivityTransition<TResult>` from `Activity<TResult>` instead of writing
`OutputArgument<T>` members or calling mutable context output methods. Use an immutable result record with
`[Output]` projections when consumers need named members.

```csharp
public sealed record LookupResult([property: Output] string Name, [property: Output] int Score);

public sealed class LookupCustomer(ICustomerStore store) : Activity<LookupResult>
{
    [ActivityInput(Key = "customer-id")]
    [Required]
    public string CustomerId { get; set; } = null!;

    protected override async ValueTask<ActivityTransition<LookupResult>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        var customer = await store.FindAsync(CustomerId, context.CancellationToken);
        return ActivityTransition.Complete(new LookupResult(customer.Name, customer.Score));
    }
}
```

Activities are transient behavior objects, created in a fresh child DI scope for each attempt. Constructor
injection is supported. Do not retain an activity instance as workflow-instance state, and do not rely on a
retry or resume receiving the same CLR object.

## Workflow values

- Workflow requests, activity inputs, activity results, private state, and trigger deliveries are immutable
  role-owned records.
- Variables live in durable lexical frames and change only through explicit engine `Set` intrinsics.
- Inputs bind through literal, workflow-request, variable-read, causal activity-result, or portable expression
  bindings. There is no generic value address or latest-output lookup.
- JavaScript and Liquid expressions receive only declared immutable parameters. Ambient variable/output
  helpers and expression-side mutation are removed.
- Only persistable values may cross checkpoints. Transient services and arbitrary CLR objects stay inside one
  activation attempt.

## Removed APIs

Canonical packages no longer expose `IMemoryBlock*`, `IMemoryRegister`, `Argument`, `InputArgument<T>`,
`OutputArgument<T>`, the no-op `ActivityBase`/`CodeActivity` authoring bases, activity
constructor/factory registries, synthetic activity property bags, active output registers, or ambient
execution-expression carriers. Derive ordinary activities from `Activity` or `Activity<TResult>`.
Direct `IActivity` implementations now return an atomic transition from
`ExecuteAsync(ActivityExecutionContext)` instead of receiving `IActivityExecutionContext`. Elsa 3
import code may still recognize the serialized legacy concepts at the importer boundary and lowers
provable relationships to canonical bindings.

For executable contracts and edge-case rules, see
[spec 095](../../specs/095-value-flow-redesign/spec.md) and
[ADR 0045](../adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
