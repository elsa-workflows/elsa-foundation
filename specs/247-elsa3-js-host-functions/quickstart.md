# Quickstart: verify spec 247

Build and test only the projects this spec touches:

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
dotnet test Essentials/tests/Expressions/Tests
dotnet test Essentials/tests/Workflows/Runtime/Tests --filter "FullyQualifiedName~WorkflowInstance|FullyQualifiedName~CorrelationId"
```

## What to look at

1. **Top-level return.** `return false` as a FlowDecision condition converts and evaluates to `false`. A body that
   ends in an `if` with no trailing `return` is still refused with `VF-IMP-006`.
2. **Accessors.**
   - A variable `HttpRequestBody` with `getHttpRequestBody().text` converts to the same binding as
     `getVariable("HttpRequestBody").text`.
   - `gethttpRequestBody()` is refused.
3. **Correlation ID.**
   - A workflow started with correlation ID `c-1` whose WriteLine text is a converted `getCorrelationId()` writes
     `c-1`.
   - After a `SetCorrelationId` to `c-2`, a later WriteLine writes `c-2`.
   - Without a correlation ID, it writes `null`.
4. **Refusals.**
   - `newGuid()` is refused, and the guidance names `getWorkflowInstanceId()`.
   - `setOutcome('x')` is refused, and the guidance names Decision and Switch.
5. **Corpus measurement** (local only; the harness and the corpus are never committed):
   - Rerun the throwaway probe over the JTest workflow definitions.
   - Expect 0 parse refusals (SC-001), and at least 7 of 33 definitions with fully converting JavaScript (SC-002).
