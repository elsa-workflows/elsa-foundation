# Test Baseline — Unit B (pre-refactor)

**Date:** 2026-05-28
**Branch:** 001-activity-identity-catalog

## Scope

Audit of pre-existing tests that exercise the implementations being refactored in Unit B. Output of T001 per `tasks.md`. Supports the §2.21.1 golden-rule audit (T146).

## Finding

The only test project in the solution is `src/Test.Activities.Import/`. Its sole file is `Class1.cs` containing an empty placeholder class — **no actual test methods exist**.

```
src/Test.Activities.Import/Class1.cs  →  empty placeholder
```

No xUnit `[Fact]` / `[Theory]` discoveries. No tests targeting:

- `ActivityVersionProvisioner` / `ActivityVersionProvisionerStartupTask`
- `ActivityDefinitionVersionSavingHandler` / `ActivityDefinitionVersionLoadingHandler`
- `IGlobalEntitySavingHandler` / `IEntitySavingHandler<,>` / `IEntityModelCreatingHandler` consumers
- `IActivityDefinition` / `IActivityDefinitionVersion` consumers
- `Elsa3.Activities.Design.Import` mapping

## §2.21.1 Implication

The golden-rule constraint ("existing tests on refactored implementations MUST pass without modification") has **no binding force** in Unit B — there are no existing tests to preserve. All test work in this unit is net-new (per the test tasks T026–T091, T116–T125, T141–T144, T154).

## Net-new test surface created by Unit B

Per `tasks.md`:

| Phase | Tests added | Project |
|---|---|---|
| US1 | T026, T027, T028 | `tests/Elsa.Activities.Design.Tests/Unit/` |
| US2 | T048–T051 | `tests/Elsa.Activities.Design.Tests/Integration/` |
| US3 | T055–T059 | `tests/Elsa.Activities.Design.Tests/Unit/` + `Integration/` |
| US4 | T086–T091 | `tests/Elsa.Activities.Design.Tests/Unit/` + `Integration/` |
| US5 | T116–T117 | `tests/Elsa.Activities.Design.Tests/Unit/` |
| US6 | T123–T125 | `tests/Elsa.Activities.Design.Tests/Unit/` |
| Polish | T141–T144 (registration), T154 (cross-context) | `tests/Elsa.Activities.Design.Tests/Registration/` + `Integration/` |

The `Test.Activities.Import` placeholder project is orphaned — it can either be deleted or repurposed during a future Elsa3-import-mapping verification pass; out of scope for Unit B.
