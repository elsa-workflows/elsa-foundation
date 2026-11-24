# Contract: the Elsa 3 migration frozen entry point

This is a compatibility surface, frozen as `EfToolingHost` is under ADR 0076 D1. The ADR 0076 amendment
(spec Q4) allows it.

## Type

- Assembly: `Elsa3.Migration.Tooling` (project `extensions/Elsa3/src/Migration/Tooling/`).
- Type: `Elsa3.Migration.Tooling.Elsa3MigrationToolingHost`.
- Method: `public static Task<int> RunAsync(Stream request, Stream response)`.

The assembly arrives as a Nuplane package (`/app/elsa3/packages/`), not in the host closure. The worker therefore
finds `Elsa3.Migration.Tooling` among the loaded package set's assemblies, then resolves the type with
`Assembly.GetType(name, throwOnError: false)`. A missing assembly or type is an image build defect, refused as
`entry-point-missing` (exit 2), naming the type and the minimum `Elsa3.Migration.Tooling` version.

## Request (UTF-8 JSON on `request`)

```json
{
  "contractVersion": 1,
  "command": "inventory",
  "toolVersion": "<dotnet-elsa informational version>",
  "imageVersion": "<essentials tag>",
  "exportPath": "/app/export",
  "outPath": "/app/out",
  "allVersions": false,
  "settingsPath": "/app/config/appsettings.json",
  "compositionRoot": "/app",
  "packages": [{ "id": "Acme.Elsa.Invoicing", "version": "1.0.0" }]
}
```

`settingsPath` is `null` for `inventory`. `compositionRoot` defaults to `/app`; tests point it at a laid-out
temporary root. `packages` lists the user packages the worker loaded. The worker loads them; the entry point finds their assemblies
through `AssemblyLoadContext.All` and registers them for feature discovery. `toolVersion` and `imageVersion` come from
the front end (the tool's informational version and `ELSA3_MIGRATION_IMAGE_VERSION`). The worker owns package loading
(R-6), so the entry point only records the list.

## Response (UTF-8 JSON on `response`)

```json
{ "contractVersion": 1, "exitCode": 1, "outPath": "/app/out", "refusal": null }
```

`refusal` is `{ "reason": "<reason code>", "message": "..." }` when `exitCode` is 2. The method's return value equals
`exitCode`. An unknown `contractVersion` is refused with reason `arguments`.

## Rules

- The entry point writes the report and `migrated/` itself, through a hidden staging folder inside `outPath` that it
  renames into place last.
- Nothing is written to standard output. The worker already moves stray output to standard error.
- The contract is additive. A new request field has a default that keeps the behaviour of requests without it, and removing a field
  increments `contractVersion`.
