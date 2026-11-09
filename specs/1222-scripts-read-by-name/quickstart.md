# Quickstart: validating Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · Provisional (initiative #3052)

How to confirm the read model once implemented. Each maps to a spec success criterion.

## Direct reads (US1 / SC-001)

1. Declare a workflow variable `order`. Add an activity whose input is `variables.order.total`; publish and run.
   Expect: the script sees the workflow `order`.
2. Nest a container declaring its own `order`; a script inside reads `variables.order`. Expect: the container's `order`
   (nearest scope wins).
3. In a loop body, read `variables.item` where `item` is the iteration variable. Expect: the iteration value.
4. Use `getVariable('order')` and `variables['order']`. Expect: same value as `variables.order`.
5. Liquid `{{ variables.order }}`. Expect: resolves to the nearest-scope `order`.
6. Declare `order` with no value; read it. Expect: JSON `null`.

## Computed names refused (US2 / SC-003)

7. Publish a workflow with `getVariable(name)` (non-literal) or `variables[key]`. Expect: publish refused with a stable
   diagnostic naming the construct and pointing at the direct-name form; nothing reaches run time.

## Converter faithfulness (US3 / SC-001, SC-002)

8. Convert an Elsa 3 `RunJavaScript` reading `getVariable("order")`. Expect: emits `variables.order`; publishes (no
   arguments type-mismatch refusal — #3287, with #3167 from S-0).
9. Convert a script that declares its own `args` alongside converted reads. Expect: converter refuses with guidance
   (#3296), not a silent mis-bind.
10. Convert a statement body. Expect: no IIFE wrapper on the `RunJavaScript` body path (#3010).

## Selective load (SC-006)

11. Declare a large/external variable not named by a script that reads only `variables.small`. Expect: the large
    variable is not loaded.

## Secrets (US5 / SC-004)

12. Read a protected variable; trigger logging. Expect: the value is redacted everywhere. Attempt to read a
    no-permission value. Expect: not provided.

## Rename safety (US4 / SC-005)

13. Rename a variable referenced by a draft script. Expect: Studio updates the resolvable reference. Remove a
    declaration a script names. Expect: publish refused. Confirm a published version's behaviour is unchanged by a draft
    rename.

## Regression / tiers

- xUnit only (no FluentAssertions). Place unit, converter and architecture tests in the correct CI tier.
- Confirm `Runtime_projects_do_not_add_design_references` and `Elsa3MigrationBoundaryTests` still pass.
