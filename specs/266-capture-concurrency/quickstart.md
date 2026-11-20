# Quickstart: check the unordered-write warning

1. Validate a workflow draft whose root Parallel has two branches, each capturing an output into `total`.
   The validations list holds one `ValueFlow/ConcurrentWrite` warning at `$workflow/variables/total` naming both
   nodes. Promotion succeeds.
2. Replace one capture with a separate variable and add one Set after the Parallel. The warning is gone.
3. Put the two capturing nodes in a Sequence instead. No warning.
4. Validate and compile a graph activity whose body Parallel captures into one graph variable: both report
   `activity.graph.unordered-variable-write`, and compilation returns a template.

Tests:

```bash
dotnet test Essentials/tests/Workflows/Design/Tests --filter "FullyQualifiedName~ValueFlowValidator|FullyQualifiedName~UnorderedVariableWrite"
dotnet test Essentials/tests/Activities/Graph/Tests --filter "FullyQualifiedName~UnorderedWrite|FullyQualifiedName~GraphBodyReferenceValidator"
```
