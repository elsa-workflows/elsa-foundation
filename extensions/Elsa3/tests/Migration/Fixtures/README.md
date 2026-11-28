# Elsa 3 migration CLI fixtures

Test fixtures of the Elsa 3 workflows migration CLI ([spec 1237](../../../../../specs/1237-elsa3-migration-cli/spec.md),
tasks T002 and T003).

- `export/` is an Elsa 3 Studio export spread over nested folders, read recursively. It holds single-definition files,
  array files (`activities/calculate-discount.json`, `workflows/customers/customer-notifications.json`), one file
  that is not valid JSON, one JSON file that holds no definition, and a text file the reader skips.
- `export.zip` holds a byte copy of `export/` under an `export/` root, entries in ordinal order. Rebuild it with
  `python make-export-zip.py` after changing the folder. `.gitattributes` keeps the folder's bytes as committed.
- `AcmeFeature/` is the fixture feature project `Acme.Elsa3MigrationFixture` (shell feature `AcmeElsa3MigrationFixture`).
  It provides the custom activity `Acme.NotifyCustomer` through an activity reconciliation source (no CLR activity
  type), the host functions `acme.lookupCustomer`, `acme.getConfig` and `acme.getCustomerData`, and the variable type
  alias `Acme.Orders.OrderDto`. With it listed in a host folder's `shells.Local.json`, `assess` clears the findings
  that name those components (SC-007).

## Redaction sentinel

Every literal, script body, variable default and setting value carries `SENTINEL-1237`, so the sentinel test (T029)
can prove that none reaches `report.json` or `report.html` (FR-019, SC-005). Identities the report may name carry no
sentinel: definition ids and names (the dispatch target `partner-onboarding` too), version ids, type names, function
and member names, case labels and outcome names (`Escalated`), and literal keys naming a target member or input.

## Cases

The expected code is the one the definition is there for. Codes come from the converter unless the stage says
otherwise. Every converted workflow also carries the routine `ELS3-NULL-POLICIES` info.

| Definition id (version) | File | Case | Expected code(s) |
|---|---|---|---|
| `order-intake` | `workflows/orders/order-intake.json` | Clean | none (routine only) |
| `daily-report` | `workflows/reports/daily-report.json` | Clean, with a String variable default | none (routine only) |
| `invoice-reminder`, `shipment-notice`, `welcome-customer` | `workflows/customers/customer-notifications.json` (array) | Unmapped custom activity `Acme.NotifyCustomer`, used by three definitions | `ELS3-ACTIVITY-UNMAPPED-TYPE` (`ComponentName` `Acme.NotifyCustomer`); with the feature: none |
| `welcome-customer` | `workflows/customers/customer-notifications.json` (array) | Also calls the undeclared function `lookupCustomer(…)` in another node: the unmapped activity refuses the definition, but the scan goes on (#6231) | `ELS3-JS-HOST-FUNCTION-MISSING` (`lookupCustomer`) next to `ELS3-ACTIVITY-UNMAPPED-TYPE`; with the feature: none |
| `customer-lookup` | `workflows/customers/customer-lookup.json` | Undeclared function called: `lookupCustomer(…)` | `ELS3-JS-HOST-FUNCTION-MISSING` (`lookupCustomer`); with the feature: none (rewritten to `acme.lookupCustomer(…)`) |
| `customer-batch` | `workflows/customers/customer-batch.json` | Undeclared function passed as a value: `ids.map(lookupCustomer)` | `ELS3-JS-HOST-FUNCTION-MISSING` (`lookupCustomer`); with the feature: `ELS3-REPLACED-FUNCTION-REFERENCE` |
| `customer-profile` | `workflows/customers/customer-profile.json` | `get`-prefixed custom global `getCustomerData(…)`, no such variable or input (T004b) | `ELS3-JS-HOST-FUNCTION-MISSING` (`getCustomerData`); with the feature: none |
| `regional-pricing` | `workflows/pricing/regional-pricing.json` | Matchable refused Elsa 3 built-in `getConfig(…)` | `ELS3-JS-HOST-FUNCTION-MISSING` (`getConfig`); with the feature: none |
| `instance-label` | `workflows/pricing/instance-label.json` | Unmatchable call: `moment()` | `VF-IMP-005` (with or without the feature) |
| `draft-refund` | `workflows/orders/draft-refund.json` | No published version: its latest version converts | tooling `ELS3-MIGRATION-NO-PUBLISHED-VERSION` (Info) |
| `order-archive` | `workflows/orders/order-archive.json` | Unregistered variable type `Acme.Orders.OrderDto`, JSON starting value | `ELS3-VARIABLE-TYPE-UNREGISTERED` (Info, `ComponentName` `Acme.Orders.OrderDto`); with the feature: none |
| `session-cleanup` | `workflows/sessions/session-cleanup.json` | A variable on a custom storage driver and one on the Elsa 3 memory driver | `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` (`Acme.Storage.BlobStorageDriver, Acme.Storage`), `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED` |
| `bad-defaults` | `workflows/legacy/bad-defaults.json` | Known-type variables whose starting values do not convert: `Int32`, a `String` array, `ObjectDictionary` | `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` ×3 |
| `calculate-discount` (1, 2, 3) | `activities/calculate-discount.json` (array) | Reusable with three versions: 3 latest published, 1 older and pinned by `quote-builder`, 2 unpinned | none; 3 converts as the latest published version and 1 because it is pinned (FR-012); 2 is listed as not converted |
| `quote-builder` | `workflows/sales/quote-builder.json` | Workflow that pins the older reusable version `calculate-discount-v1` (T022) | none; `migrated/activities/activities.json` holds `calculate-discount` 1 and 3 |
| `format-address` | `activities/format-address.json` | Reusable with two users | none |
| `ship-order`, `return-label` | `workflows/shipping/*.json` | The two users of `format-address` | none |
| `credit-check` | `activities/credit-check.json` | Reusable with the custom Elsa 3 outcome `Escalated` next to `Done` (T022) | none; its exported contract declares `done` and `Escalated` |
| `loan-application` | `workflows/loans/loan-application.json` | Routes on `credit-check`'s `Done` and `Escalated` | none |
| `audit-entry` | `activities/audit-entry.json` | Reusable nested in a container (a Sequence), used from a workflow and from another reusable (T022) | none |
| `close-order` | `activities/close-order.json` | Reusable that runs `audit-entry` inside a Sequence | none |
| `nightly-audit` | `workflows/audit/nightly-audit.json` | Workflow that runs `audit-entry` inside a Sequence | none |
| `order-closing` | `workflows/orders/order-closing.json` | Workflow that uses `close-order` | none |
| `legacy-tax-lookup` | `activities/legacy-tax-lookup.json` | Reusable the converter refuses: it sets a variable | `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` |
| `invoice-tax` | `workflows/invoices/invoice-tax.json` | Workflow whose reusable dependency is refused | skipped as a dependent of `legacy-tax-lookup` (import stage) |
| `csharp-greeting` | `workflows/legacy/csharp-greeting.json` | C# expression | `VF-IMP-007` |
| `dotnet-member` | `workflows/legacy/dotnet-member.json` | .NET-style member call on a sentinel literal: `'…'.ToUpper()` | `VF-IMP-012` |
| `partner-sync` | `workflows/partners/partner-sync.json` | Dispatch target `partner-onboarding` absent from the export | none from the converter; listed as a missing target, Take into account (T020); Elsa 4 refuses to publish it because the target does not resolve (`ELS3-RECONCILE-PUBLISH-FAILED`, publish stage) |
| `ticket-router` | `workflows/support/ticket-router.json` | Switch case conditions: a non-boolean literal and an unparsable JavaScript expression | `ELS3-ACTIVITY-UNSUPPORTED-MODE` ×2, then `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED` on the case connections |
| `ticket-escalation` | `workflows/support/ticket-escalation.json` | Switch with an unsupported mode literal | `ELS3-ACTIVITY-UNSUPPORTED-MODE`, then `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED` on the case connection |
| `parallel-checks` | `workflows/support/parallel-checks.json` | Fork whose join mode is an expression | `ELS3-ACTIVITY-UNSUPPORTED-MODE` |
| `liquid-letter` | `workflows/legacy/liquid-letter.json` | Unparsable Liquid template | `VF-IMP-007` |
| `legacy-octal` | `workflows/legacy/legacy-octal.json` | Script that parses only in sloppy mode (`010`) | `VF-IMP-007` |
| `inventory-sync` (1, twice) | `workflows/inventory/inventory-sync.json`, `duplicates/inventory-sync-copy.json` | Duplicate input: same version id, definition id and version | `ELS3-RECONCILE-DUPLICATE-INPUT` (import stage, from input selection); both inputs are refused |
| (file) | `notes/broken-export.json` | Not valid JSON | input `ELS3-MIGRATION-INPUT-UNREADABLE` |
| (file) | `settings/studio-preferences.json` | Valid JSON that holds no definition | input `ELS3-MIGRATION-UNSUPPORTED-INPUT` |
| (file) | `notes/export-notes.txt` | Not `*.json` | not read, no finding |

The converter codes were checked by converting each definition alone against the shipped activity catalog, with and
without the three host functions. A reusable reference converts only in a collection import, so the rows of
reusables and their users, the import-stage rows and the tooling rows are what the full run (T021, T024, T027) must
show.
