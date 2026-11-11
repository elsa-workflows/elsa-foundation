# Quickstart: validating Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · Provisional (initiative #3052)

How to confirm the read model once implemented. Each maps to a spec success criterion.

## Literal reads (US1 / SC-001)

1. Declare a workflow variable `order`. Add an activity whose input is `variables.order.total`; publish and run.
   Expect: the script sees the workflow `order`.
2. Nest a container declaring its own `order`; a script inside reads `variables.order`. Expect: the container's `order`
   (nearest scope wins).
3. In a loop body, read `variables.item` where `item` is the iteration variable. Expect: the iteration value.
4. Use `getVariable('order')` and `variables['order']`. Expect: same value as `variables.order`, pinned at publish.
5. Liquid `{{ variables.order }}`. Expect: resolves to the nearest-scope `order`.
6. Declare `order` with no value; read it. Expect: JSON `null`.

## Computed reads via the run-time fallback (US2 / SC-004)

7. Publish a workflow with `getVariable(name)` (non-literal) or `variables[key]`. Expect: publish succeeds and reports
   the variables the script could read.
8. Run it with `name = 'order'`. Expect: reads the nearest-scope `order`; the step's input snapshot contains a recorded
   read of `order`, and no recorded read for candidates the script did not read.
9. Run it with a name that matches nothing. Expect: JavaScript reads `undefined` (not `null`), Liquid reads nil, a
   warning is recorded, and the read is marked absent.
10. Run it with a name that matches a transient variable. Expect: the step faults with the transient-read diagnostic
    (OQ-3).

## Converter faithfulness (US3 / SC-001, SC-002, SC-003)

11. Convert an Elsa 3 `RunJavaScript` reading `getVariable("order")`. Expect: emits `variables.order`; publishes (no
    arguments type-mismatch refusal — #3287, with #3167 from S-0).
12. Convert an Elsa 3 script reading `getVariable(name)`. Expect: emits `getVariable(name)`; publishes; reads the same
    value as in Elsa 3 through the fallback.
13. Convert a script that declares its own `args` alongside converted reads. Expect: converter refuses with guidance
    (#3296), not a silent mis-bind.
14. Convert a statement body. Expect: no IIFE wrapper on the `RunJavaScript` body path (#3010).

## Selective load (SC-007)

15. Declare a large/external variable not named by a literal-only script that reads only `variables.small`. Expect: the
    large variable is not loaded.

## Secrets (US5 / SC-005)

16. Read a protected variable by literal and by computed name; trigger logging. Expect: the value is redacted
    everywhere, including the recorded read. Attempt to read a no-permission value. Expect: not provided; the step
    faults with the protection diagnostic.

## Rename safety (US4 / SC-006)

17. Rename a variable referenced by a draft script. Expect: Studio updates the literal reference and flags a computed
    read that could reach it. Remove a declaration a script names literally. Expect: publish refused. Confirm a
    published version's behaviour is unchanged by a draft rename.

## Input snapshot vs body session

18. In a `RunJavaScript` body, write a variable and then read it by literal and computed name. Expect: the body sees its
    pending write (ADR 0092 session); an input-binding expression on the same step does not.

## Regression / tiers

- xUnit only (no FluentAssertions). Place unit, converter and architecture tests in the correct CI tier.
- Confirm `Runtime_projects_do_not_add_design_references` and `Elsa3MigrationBoundaryTests` still pass.
