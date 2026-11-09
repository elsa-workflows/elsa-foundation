# Quickstart: Elsa 3 converter: workflow variables that can be published

Build and test only the touched projects (shared machine).

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests --filter "FullyQualifiedName~Elsa3VariableStartingValueTests"
```

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests --filter "FullyQualifiedName~WorkflowVariablePublishTests"
```

Then run the full suites of both projects, which include every existing conversion and import test.

## What to check

1. An imported Elsa 3 workflow with a workflow variable and no value publishes. Its Event's payload is captured into the
   variable, and the run's root variable frame holds that payload.
2. A workflow with one variable of each kind (`String`, `Int32`, `Boolean`, `Object` holding JSON text,
   `List<String>` and `ObjectDictionary` holding JSON text) publishes. Each variable starts with the value in the
   data model's table.
3. A variable such as `Int32` with `"abc"` is refused with `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` at
   `$.variables[i]`.
