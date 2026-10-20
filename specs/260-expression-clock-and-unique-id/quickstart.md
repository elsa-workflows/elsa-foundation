# Quickstart: verify spec 260

Build and test only the touched projects (AGENTS.md: shared machine).

```bash
dotnet test Essentials/tests/Expressions/Tests
dotnet test Essentials/tests/Expressions/JavaScript/Jint/Tests
dotnet test Essentials/tests/Workflows/Runtime/Tests --filter "FullyQualifiedName~Clock|FullyQualifiedName~UniqueId|FullyQualifiedName~BindingPureV2|FullyQualifiedName~CanonicalRuntimeInputBindingResolver"
dotnet test Essentials/tests/Activities/Runtime/Tests --filter "FullyQualifiedName~ClockParameter|FullyQualifiedName~WorkflowInstanceParameter"
dotnet test Essentials/tests/Activities/ControlFlow/Tests --filter "FullyQualifiedName~While|FullyQualifiedName~Do"
dotnet test Essentials/tests/Workflows/Publishing/Api/Tests
dotnet test Essentials/tests/Workflows/Design/Tests
dotnet test Essentials/tests/Activities/Http/Tests --filter "FullyQualifiedName~WriteHttpResponseExecutionTests"
```

Expected after implementation:

1. A JavaScript input `args.now` with a `Clock` parameter returns the snapshot's `MaterializedAt` in ISO form, and a
   retried activity returns the same value.
2. Two `UniqueId` parameters return different UUID version 7 strings.
3. A `While` condition `args.now < args.deadline` exits once the test clock passes the deadline, and each pass records
   a different time.
4. Under `binding-pure-v2`, `new Date("2026-10-20T08:30:00.000Z").toISOString()` returns the same string, and each
   removed form throws its contract message.
5. The same source stamped `binding-pure-v1` still fails with the existing stripped-capability message.
