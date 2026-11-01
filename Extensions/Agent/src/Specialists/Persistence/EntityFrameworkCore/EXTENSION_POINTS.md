# Specialist EF extension points

| Surface | Purpose |
| --- | --- |
| `SpecialistsEntityFrameworkCoreFeature` | Selects the provider, connection, schema and pooling for the shell. Depends on SpecialistsRuntime. |
| `SpecialistsEntityFrameworkCoreRegistration.AddSpecialistsEntityFrameworkCore` | Registers module-owned contexts, the scoped turn journal and the shared migration lifecycle. |
| `SpecialistsEntityFrameworkCoreOptions` | Explicit choices for that registration. |
| `SpecialistsModelBuilderExtensions.AddSpecialistsModel` | Installs only the specialist mappings in a consumer-owned context. The host owns migrations. |
| `SpecialistsEntityFrameworkCoreRegistration.AddSpecialistTurnStore<TContext>` | Replaces the journal over a registered context without requiring an Elsa base class. |
| `SpecialistsDbContext` and four provider contexts | Optional module-owned models with provider-specific migrations. |
| `SpecialistsEfModule` | Stable migration history identity and canonical schema chain. |

Consumers use the engine-free [Core replacement contract](../../Core/EXTENSION_POINTS.md).
Entities, query projections and the EF store remain internal. Generated migration entry points appear
in the package API baseline. See [registration and limits](README.md).
