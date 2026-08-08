# Contract: Fragment & Manifest Wire Shapes

**Feature**: 149-consumer-contract-fragments

This is the external contract of the feature — what consumers read. Shapes are stable within `schemaVersion` major; evolution is additive (new optional fields only). Field-level semantics in [data-model.md](../data-model.md).

## Fragment example (abridged) — `docs/contracts/fragments/Elsa.Activities.Http.json`

```json
{
  "activities": [
    {
      "activityTypeKey": "Elsa.Activities.Http.Activities.HttpEndpoint",
      "category": "HTTP",
      "containerStructure": null,
      "contentHash": "9A3F…",
      "description": null,
      "displayName": "Http endpoint",
      "executionType": "Action",
      "featureId": "ActivitiesHttp",
      "inputs": [
        {
          "category": "Simple",
          "collectionKind": "Single",
          "defaultSyntax": null,
          "defaultValue": "Async",
          "displayName": "Response mode",
          "hasStaticDefault": true,
          "isBrowsable": true,
          "isNullable": false,
          "isRequired": false,
          "name": "ResponseMode",
          "order": 30,
          "referenceKey": "ResponseMode",
          "type": "Elsa.Http.Core.ResponseMode",
          "uiHint": null,
          "uiSpecifications": null
        }
      ],
      "outputs": [
        { "collectionKind": "Single", "isBrowsable": true, "isRequired": true,  "name": "Request",       "referenceKey": "Request",       "type": "…HttpRequestModel" },
        { "collectionKind": "Single", "isBrowsable": true, "isRequired": true,  "name": "RouteData",     "referenceKey": "RouteData",     "type": "…" },
        { "collectionKind": "Single", "isBrowsable": true, "isRequired": false, "name": "ParsedContent", "referenceKey": "ParsedContent", "type": "…" }
      ],
      "ports": [],
      "version": "1.0.0"
    }
  ],
  "assembly": "Elsa.Activities.Http",
  "features": [
    {
      "dependsOn": ["Http", "WorkflowsRuntimeHttp"],
      "description": "…",
      "displayName": "HTTP activities",
      "id": "ActivitiesHttp",
      "options": [
        { "clrType": "String", "defaultValue": "/workflows/http", "jsonType": "string", "name": "BasePath", "required": false }
      ]
    }
  ],
  "schemaVersion": "1.0.0",
  "structures": []
}
```

(Keys ordinal-sorted, 2-space indent, LF, UTF-8 no BOM — the example above reflects the real ordering rule.)

## Manifest example — `docs/contracts/manifest.json`

```json
{
  "counts": { "activities": 42, "features": 21, "fragments": 18, "structures": 10 },
  "fragments": {
    "Elsa.Activities.ControlFlow": "sha256:…",
    "Elsa.Activities.Http": "sha256:…",
    "Elsa.Activities.Sequence": "sha256:…"
  },
  "generator": "tools/contracts/Elsa.Contracts.Generator",
  "schema_version": "1.0",
  "submit_schema": "sha256:…"
}
```

## Embedded resource

Each opted-in assembly carries manifest resource **`elsa.contract.json`** — byte-identical to its `docs/contracts/fragments/<Assembly>.json` at the same commit.

## Served catalog change (additive)

`GET design/activities/catalog` — `ActivityOutputDescriptorView` gains:

| New field | Type | Meaning |
|---|---|---|
| `referenceKey` | string | Binding key for authoring an output target. |
| `isRequired` | bool | Publish compile rejects the definition if this output has no authored target. |

`ActivityInputDescriptorView.defaultValue` (existing field) is now populated for every input with a statically representable default (G1). No route, permission, or existing-field changes.

## CLI contract — `tools/contracts/Elsa.Contracts.Generator`

| Command | Behavior | Exit codes |
|---|---|---|
| `emit --assembly <path> --references <rsp> --output <dir> [--embed]` | Project one assembly → fragment; optionally inject as embedded resource. No contribution → no output, exit 0. | 0 ok / 1 diagnostics errors / 2 usage |
| `merge` | Collect fragments from built src assemblies → `docs/contracts/` + manifest. | 0 / 1 (unreadable or duplicate fragment) / 2 |
| `check` | Regenerate to temp, byte-compare vs committed `docs/contracts/` (manifest included). | 0 fresh / 1 stale / 2 |

Diagnostics printed in canonical MSBuild format (`path(line): warning ELSACT0NN: …`) so `Exec` surfaces them as first-class build diagnostics.
