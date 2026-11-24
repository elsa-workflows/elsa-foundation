# Data model: Elsa essentials images

## Essentials feature list

One JSON document, `essentials.features.json` ([contract](contracts/feature-list.md)).

| Field | Type | Rule |
|---|---|---|
| `features[]` | array | One entry per composed feature; names unique. |
| `features[].name` | string | A CShells feature name that resolves in `EssentialsFeatureAssemblies`. |
| `features[].reason` | string | Non-empty. One of: a JTest suite or template that needs it; the essentials Studio; JSON workflow reconciliation; `required by <feature>`. |
| `features[].settings` | object | Optional baked settings, the same shape as the feature's CShells settings. Secrets are absent or empty. |

Projection: each entry becomes `CShells:Shells:default:Features:<name>` with its settings, the lowest configuration
layer.

## User settings file

`/app/config/appsettings.json`, optional for the file itself but required in practice for the secrets
([engine image contract](contracts/engine-image.md)).

| Content | Effect |
|---|---|
| `CShells:Shells:default:Features:<baked name>:<key>` | Overrides that baked setting. |
| `CShells:Shells:default:Features:<new name>` | Adds a feature; its package must be in `/app/packages`. |
| Any other host key (`ConnectionStrings`, `Elsa:*`, `Logging`, `Cors`) | Overrides the baked host default. |

## Workflows folder

`/app/migrated/workflows`: a flat folder of `*.json` files, each a JSON workflow reconciliation catalog (an array of workflow
version models). Read once per start in ordinal file-name order; imported and published.

## Activities file

`/app/migrated/activities/activities.json`: one JSON activity reconciliation catalog (an array of activity version
models), the converted reusables as composite activities. `[]` in the image.

## Acceptance run record

A row in `extensions/Elsa3/tests/JTest/ledger/runs.md`, the S-15 run ledger.

| Field | Content |
|---|---|
| Engine | `essentials` |
| Head | initiative commit |
| Image | local tag and digest |
| CLI | version and commit of the CLI that wrote `migrated/` |
| `jtest` | version |
| Result | per suite: pass, fail, or the S-15 exemption it carries; compared with the Workbench row of the same head |
