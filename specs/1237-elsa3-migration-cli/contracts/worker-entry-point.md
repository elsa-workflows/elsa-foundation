# Contract: the Elsa 3 migration frozen entry point

This is a compatibility surface, frozen as `EfToolingHost` is under ADR 0076 D1. The ADR 0076 amendment
(spec Q4) allows it.

## Type

- Assembly: `Elsa3.Migration.Tooling` (project `extensions/Elsa3/src/Migration/Tooling/`).
- Type: `Elsa3.Migration.Tooling.Elsa3MigrationToolingHost`.
- Method: `public static Task<int> RunAsync(Stream request, Stream response)`.

The worker resolves the type by name with `GetType(name, throwOnError: false)`. A missing assembly or type is the
`host-entry-point-missing` refusal, which names the minimum `Elsa3.Migration.Tooling` version.

## Request (UTF-8 JSON on `request`)

```json
{
  "contractVersion": 1,
  "command": "inventory",
  "exportPath": "/abs/export",
  "outPath": "/abs/out",
  "allVersions": false,
  "settingsPath": "/abs/appsettings.json",
  "closureKind": "reference",
  "packages": [{ "id": "Acme.Elsa.Invoicing", "version": "1.0.0" }]
}
```

`settingsPath` is `null` for `inventory`. `packages` lists what the worker loaded. The worker owns package loading
(R-6), so the entry point only records the list.

## Response (UTF-8 JSON on `response`)

```json
{ "contractVersion": 1, "exitCode": 1, "outPath": "/abs/out", "refusal": null }
```

`refusal` is `{ "reason": "<reason code>", "message": "..." }` when `exitCode` is 2. The method's return value equals
`exitCode`. An unknown `contractVersion` is refused with reason `arguments`.

## Rules

- The entry point writes the report and `migrated/` itself, through a staging folder that it swaps in last.
- Nothing is written to standard output. The worker already moves stray output to standard error.
- The contract is additive. A new request field has a default that keeps today's behaviour, and removing a field
  increments `contractVersion`.
