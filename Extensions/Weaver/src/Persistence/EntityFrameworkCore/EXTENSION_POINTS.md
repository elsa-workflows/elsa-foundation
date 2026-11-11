# Agent EF extension points

| Surface | Purpose |
| --- | --- |
| `WeaverEntityFrameworkCoreFeature` | Opt-in shell feature `WeaverEntityFrameworkCore`: provider, connection, schema, pooling and lease. Depends on `Weaver` and `Secrets`. |
| `WeaverEntityFrameworkCoreRegistration.AddWeaverEntityFrameworkCore` | Registers the module-owned contexts and the shared migration lifecycle, and selects the durable record store and audit store. |
| `WeaverEntityFrameworkCoreOptions` | Explicit choices for that registration. |
| `WeaverModelBuilderExtensions.AddWeaverModel` | Installs only the agent mappings in a consumer-owned context. The host owns migrations. |
| `WeaverEntityFrameworkCoreRegistration.AddWeaverStores<TContext>` | Selects the stores over a registered context without requiring an Elsa base class. |
| `WeaverDbContext` and four provider contexts | Optional module-owned models with provider-specific migrations. |
| `WeaverEfModule` | Stable migration history identity (`ElsaWeaver`) and canonical schema chain. |

Consumers use the engine-free [Agent replacement contracts](../../Elsa.Weaver/EXTENSION_POINTS.md):
`IWeaverSessionRecordStore` and the audit sink, reader and ownership, selected through
`WeaverPersistenceSelection`. Entities, the codec and the EF stores remain internal. Generated
migration entry points appear in the package API baseline. See [registration and limits](README.md).
