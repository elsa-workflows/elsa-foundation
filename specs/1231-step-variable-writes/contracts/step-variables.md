# Contract: step variables (spec 1231)

## Script (`RunJavaScript` body)

| Form | Kind | Publish |
|---|---|---|
| `variables.x`, `variables['x']`, `getVariable('x')` | read (S-4) | pinned |
| `variables.x = v`, `variables['x'] = v`, `setVariable('x', v)` | whole assignment | pinned; in may-write set |
| `variables.x += v`, `variables.x++` | read and whole assignment | pinned |
| `variables.o.m = v`, `getVariable('o').m = v`, mutation through an alias of a read value | member change (C2) | `o` in may-write set |
| a read value passed to a function, stored in a local or captured by a closure | possible write | in may-write set, unless the variable's declared type is a scalar |
| `[variables.a, variables.b] = …`, `for (variables.k in o)`, `(variables.o ?? {}).m = v` | whole assignment or member change | pinned; in may-write set |
| `variables[k] = v`, `setVariable(k, v)` | computed write | publishes; node may write every visible variable; name resolved at run time; undeclared or transient → step faults |
| a write through `variables` or `setVariable` used as a value (`const v = variables; v.X = 1`) | opaque write | same as a computed write (Q5) |
| literal write to an undeclared, transient, or read-only (loop item/index) target | — | refused |
| `set<Name>(v)` | not provided by the runtime | unknown function |

## C#

Illustrative; the exact activity base-class signature follows the activity model on the branch.

```csharp
[AccessesUndeclaredVariables] // optional: enables the string overloads
public class MyActivity : Activity<MyResult>
{
    [ActivityInput] public VariableReference<decimal> Balance { get; set; } = default!;      // read and write
    [ActivityInput] public ReadOnlyVariableReference<string> Currency { get; set; } = default!; // read only

    protected override ValueTask<ActivityTransition<MyResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var balance = context.Variables.Get(Balance);   // a copy; recorded as a session read
        context.Variables.Set(Balance, balance + 5);     // an intent, committed with the step
        // context.Variables.Set("total", 5);            // free name: marked types only
        ...
    }
}
```

| Call | Unmarked type | Marked type |
|---|---|---|
| `Get(reference)`, `Set(writableReference, v)` | yes | yes |
| `Set(readOnlyReference, v)` | throws | throws |
| `Get<T>("name")`, `Set("name", v)` | throws → step faults | resolves nearest visible scope; undeclared → step faults before commit |

## Diagnostics

| Code | When |
|---|---|
| `VF-WRITE-002`, `VF-WRITE-004` | a script body's literal write to an undeclared, or loop item or index target (publish and draft validation) |
| `VF-WRITE-003` | a script body's literal write to a transient variable (publish only: transience is a runtime storage decision) |
| `VF-WRITE-005` | warning: a script body writes by a computed or opaque name |
| `VF-BG-001 (C9)` | background placement of an activity with a variable signal (FR-030), at publication, admission and the worker |
| `VF-ACT-010` | session read of a withheld value |
| publish diagnostics (codes chosen in implementation, `ValueFlow/*` family) | unbound / undeclared / read-only-target reference; may-write set overlaps own capture (warning); script writes: see the `VF-WRITE-*` rows |
| `ELS3-SETTER-UNDECLARED-VARIABLE` (`Elsa3ImportDiagnosticCodes`) | setter of an undeclared variable (a computed setter name converts to `variables[k] = v`, FR-033 as amended; no variable converts to a transient one) |
