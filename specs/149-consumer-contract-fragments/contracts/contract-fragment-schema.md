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
          "defaultValue": "async",
          "displayName": "Response Mode",
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
  "counts": { "activities": 28, "features": 100, "fragments": 94, "intrinsics": 5, "structures": 10 },
  "fragments": [
    { "assembly": "Elsa.Activities.ControlFlow", "fingerprint": "sha256:…" },
    { "assembly": "Elsa.Activities.Http", "fingerprint": "sha256:…" }
  ],
  "generator": "tools/contracts/Elsa.Contracts.Generator",
  "schemaVersion": "1.0",
  "submitSchema": "sha256:…"
}
```

(Fragment fingerprints are an array of records, not a dictionary — assembly names are verbatim identifiers and must never be re-cased by a serializer key policy.)

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
| `merge [--configuration Release] [--output <dir>]` | Project every built src assembly → `docs/contracts/` (fragments, submit-schema, manifest). | 0 / 1 diagnostics errors / 2 usage |
| `check [--configuration Release]` | Regenerate to temp, byte-compare vs committed `docs/contracts/` (manifest included; authored README exempt). | 0 fresh / 1 stale / 2 |
| `emit --assembly <dll> [--references <rsp>] --output <dir>` | Project one assembly → fragment (the standalone mode consumers run against their own activity packages). No contribution → no output, exit 0. | 0 / 1 / 2 |

Diagnostics printed in canonical MSBuild format (`path: warning|error ELSACT0NN: …`) so CI logs and IDEs surface them as first-class build diagnostics. Embedding is not a CLI concern: `src/Elsa/Directory.Build.targets` embeds the committed fragment file by existence (research R4).
