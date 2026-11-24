# Contract: the essentials feature list

Consumers: the essentials engine host and the migration CLI's reference closure (its own specification). Neither
reads the other.

## File

`essentials/src/Foundation/Essentials/essentials.features.json`, embedded in `Elsa.Foundation.Essentials`.

```json
{
  "features": [
    {
      "name": "JsonWorkflowReconciliation",
      "reason": "Workflows folder: imports and publishes /app/workflows at startup",
      "settings": {
        "Options": {
          "SourceId": "essentials",
          "FolderPath": "/app/workflows",
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
| `EssentialsFeatureList.Read()` | The entries, in file order. |
| `EssentialsFeatureAssemblies.All` | The assemblies feature discovery needs for every entry. |
| `IConfigurationBuilder.AddEssentialsConfiguration(string bakedSettingsPath, string userSettingsPath)` | Adds, lowest first: the list as `CShells:Shells:default:Features` keys, the baked `appsettings.json`, the user file (optional). The caller adds environment and command line after it. |

The CLI passes its own baked defaults path (or none) and the `--settings` file as the user file, so `assess` merges
exactly as the image does.

## Rules

- Every composed feature is listed; the guard fails on any difference between the list and the composed set.
- A change to the list is a change to both the image and the CLI closure; its PR runs the JTest acceptance again
  (FR-019) before the initiative lands.
