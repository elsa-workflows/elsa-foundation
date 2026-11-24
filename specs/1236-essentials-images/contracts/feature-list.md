# Contract: the essentials feature list

Consumers: the essentials engine host and the migration CLI, which runs inside the Elsa 3 migration image built
`FROM` the engine (its own specification). Neither references the other.

## File

`apps/Elsa.Essentials.Composition/essentials.features.json`, in `Elsa.Essentials.Composition`, copied to its build output and, in the image, to `/app/composition/essentials.features.json`. An image built `FROM` the engine adds its own `*.features.json` in the same folder (R-12).

```json
{
  "features": [
    {
      "name": "JsonWorkflowReconciliation",
      "reason": "Workflows folder: imports and publishes /app/migrated/workflows at startup",
      "settings": {
        "Options": {
          "SourceId": "essentials",
          "FolderPath": "/app/migrated/workflows",
          "PublishOnReconcile": true
        }
      }
    },
    {
      "name": "ActivitiesHttp",
      "reason": "JTest http suites: HttpEndpoint, WriteHttpResponse"
    }
  ]
}
```

## Library surface

| Member | Purpose |
|---|---|
| `EssentialsFeatureList.Read(string compositionFolder)` | The entries of `essentials.features.json`, then of every other `*.features.json` in the folder in ordinal name order, entries in file order; a later entry for the same feature merges over an earlier one key by key. |
| `EssentialsFeatureAssemblies.All` | The assemblies feature discovery needs for every entry. |
| `IConfigurationBuilder.AddEssentialsConfiguration(string compositionFolder, string bakedSettingsPath, string userSettingsPath)` | Adds, lowest first: the lists as `CShells:Shells:default:Features` keys, the baked `appsettings.json`, the user file (optional). The caller adds environment and command line after it. |

Inside the migration image the CLI passes `/app/composition`, the image's baked `appsettings.json` and the
`--settings` file as the user file, so `assess` merges exactly as the engine does.

## Required settings in the CLI

The identity features refuse composition without their secrets (for example `BuildInitialAdmin` throws when
`SeedAdminUserName` is set and the password is empty). `inventory` runs with default settings, so the CLI supplies
throwaway values for every required setting of [the engine contract](engine-image.md#required-settings) in its
in-memory run; a user's `--settings` file overrides them as in the engine.

## Rules

- Every feature the engine image composes is in the essentials list; the guard fails on any difference between the
  list and the composed set. A derived image's additions are checked by that image's own specification.
- A change to the list is a change to both the image and the CLI closure; its PR runs the JTest acceptance again
  (FR-019) before the initiative lands.
