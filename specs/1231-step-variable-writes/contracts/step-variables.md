# Contract: step variables (spec 1231)

## Script (`RunJavaScript` body)

| Form | Kind | Publish |
|---|---|---|
| `variables.x`, `variables['x']`, `getVariable('x')` | read (S-4) | pinned |
| `variables.x = v`, `variables['x'] = v`, `setVariable('x', v)` | whole assignment | pinned; in may-write set |
| `variables.x += v`, `variables.x++` | read and whole assignment | pinned |
| `variables.o.m = v`, `getVariable('o').m = v`, mutation through an alias of a read value | member change (C2) | `o` in may-write set |
| a read value passed to a function, stored in a local or captured by a closure | possible write | in may-write set |
| `variables[k] = v`, `setVariable(k, v)`, `setVariable` as a value | computed write | refused |
| any write in a body where `variables` or `setVariable` escapes | opaque write | refused (Q5) |
| write to an undeclared, transient, or read-only (loop item/index) target | — | refused |
| `set<Name>(v)` | not provided by the runtime | unknown function |

## C#

Illustrative; the exact activity base-class signature follows the activity model on the branch.

```csharp
[AccessesUndeclaredVariables] // optional: enables the string overloads
public class MyActivity : Activity<MyResult>
{
    public VariableReference<decimal> Balance { get; set; } = default!;      // read and write
    public ReadOnlyVariableReference<string> Currency { get; set; } = default!; // read only

    protected override ValueTask<MyResult> ExecuteAsync(IActivityExecutionContext context)
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
| `VF-BG-001 (C9)` | background placement of an activity with a variable signal (FR-030), at publication, admission and the worker |
| `VF-ACT-010` | session read of a withheld value |
| publish diagnostics (codes chosen in implementation, `ValueFlow/*` family) | unbound / undeclared / read-only-target reference; computed, opaque, undeclared, transient or read-only script write; may-write set overlaps own capture (warning) |
| `ELS3-*` (stable, chosen in implementation under `Elsa3ImportDiagnosticCodes`) | computed setter name; setter of an undeclared variable; setter of a transient variable |
