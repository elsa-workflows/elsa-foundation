# Quickstart: verifying spec 236

Build and test only the touched projects (AGENTS.md, shared machine).

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
```

```bash
dotnet test Essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --filter "FullyQualifiedName~ClrAssemblyScanner"
```

```bash
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~Elsa3MigrationBoundary"
```

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests
```

## What to check

1. **Coverage (SC-001).** `Elsa3ActivityMapCoverageTests` reports 0 unclassified activities or members against the
   test-owned Elsa 3 surface fixture. Each targeted Elsa 4 key, slot and outcome exists in a catalog that the real CLR
   scanner builds over the Elsa 4 activity assemblies.
2. **Real catalog (SC-002).** One definition with a node for every convertible built-in, rooted in a `Sequence`,
   converts with 0 Errors against that real catalog. Warnings appear only for W members.
3. **Unconvertible causes (SC-003).** One theory case per error code in `contracts/diagnostic-codes.md` yields exactly
   that code. No test in the Elsa 3 suites sees an exception other than cancellation.
4. **Collection lowering (SC-004).** Each single-definition value-flow case (literal, request, variable, result, loop,
   javascript, liquid), applied as a collection item, stores the same bindings.
5. **Determinism (SC-005).** Converting twice gives byte-identical serialized state.
6. **Reusable outputs (SC-006).**
   - The converted reusable workflow, given to `GraphActivityProvider.ValidateAsync` and `CompileAsync` with its
     contract, validates clean, and the compiled descriptor maps `CustomOutput` to its synthesized variable.
   - The direct-start wrapper exposes `CustomOutput`.
   - The run-time check (a caller receives the value) waits for #2796, which publishes imported reusables.
7. **Alias (US3).**
   - A test assembly activity with `[ReplacesActivityType("Acme.SendInvoice")]` resolves the Elsa 3 node.
   - A second claimant gives `ELS3-ACTIVITY-AMBIGUOUS-ALIAS`.
   - A new version without the attribute gives `ELS3-ACTIVITY-UNMAPPED-TYPE`.
8. **Complete/End in a reusable workflow (D-1 to D-7).**
   - `Complete("fail")` nested in a loop, `End`, and a natural end each convert. The provider validates and compiles
     the result, and `completeOutcomes` holds `fail` and `done`.
   - `Complete(["a","b"])` and an expression outcome give `ELS3-ACTIVITY-UNSUPPORTED-MODE`.
   - An undeclared outcome gives `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED`.
   - A declared but unreached outcome gives `ELS3-BEHAVIOUR-OUTCOME-UNREACHED` and is absent from the contract.
