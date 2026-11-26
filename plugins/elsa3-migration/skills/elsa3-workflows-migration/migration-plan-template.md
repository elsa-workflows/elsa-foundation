# Elsa 3 to Elsa 4 migration plan

<!--
Copy this file into your workspace as `elsa3-migration-plan.md` and keep it with your migration files. It is the only
record of where the migration stands: every session, with or without an AI assistant, reads it first and updates it
as it goes. Hold only paths, names, decisions and notes here, never workflow content or secrets.
Rows marked "(example)" show the shape; replace or delete them.
-->

Elsa 4 clone: <path> at <commit> (the tool and the migration host run from it; try the result on the same commit)
Stage: inventory
Export: <path>
Last report: <path> (<command>, exit <code>, <date>, <tool name> <tool version>)

<!--
Stage moves forward only: inventory → prepare → test-migration → handoff.
A new finding during test-migration sends one component back to `porting`, not the stage.
Moving the clone to another commit means running the assessment again.
-->

## Elsa 3 source

- <folder or repository>

## JavaScript prefixes

| Prefix | Functions |
|---|---|
| acme (example) | lookupCustomer |

## Elsa 4 features and projects

| Feature | Project | Components | Referenced and listed by the migration host |
|---|---|---|---|
| Acme (example) | ../acme-elsa4/Acme.Elsa4/Acme.Elsa4.csproj | Acme.SendInvoice, lookupCustomer, Invoice | yes |

## Components

<!--
Kind: activity type, JavaScript function, variable type, storage driver.
Uses: definitions that use the component, then references, as the report counts them.
Source: a path, `not found`, or `Elsa 3 built-in` (not searched for).
Cases: porting catalog case numbers, in catalog order.
Status: todo → porting → ported → verified, or redesign / dropped (with the decision in Notes);
        a storage driver: todo → interviewed (with the goal and decision in Notes).
-->

| Kind | Elsa 3 name | Uses | Source | Elsa 4 target | Cases | Tests to port | Status |
|---|---|---|---|---|---|---|---|
| activity type (example) | Acme.SendInvoice | 12 definitions, 15 references | src/Acme.Elsa3/Activities/SendInvoice.cs | Acme.SendInvoice | 01, 02, 03, 04, 05, 10, 18, 19 | tests/Acme.Elsa3.Tests/SendInvoiceTests.cs | porting |

## Accepted refusals

<!-- Only the user adds a row. -->

| Definition | Finding | Reason | Date |
|---|---|---|---|
| invoice-reminder (example) | `<code>` at $.root.activities[3] | Workflow retired; not migrated | 2026-01-15 |

## Notes

- <decisions, redesigns, storage-driver interview answers, links to the owner's own plans>
