# Agent EF extension points

| Surface | Purpose |
| --- | --- |
| `WeaverEntityFrameworkCoreFeature` | Opt-in shell feature `WeaverEntityFrameworkCore`: provider, connection, schema, pooling and lease. Depends on `Weaver` and `Secrets`. |
| `AgentEntityFrameworkCoreRegistration.AddAgentEntityFrameworkCore` | Registers the module-owned contexts and the shared migration lifecycle, and selects the durable record store and audit store. |
| `AgentEntityFrameworkCoreOptions` | Explicit choices for that registration. |
| `WeaverModelBuilderExtensions.AddAgentModel` | Installs only the agent mappings in a consumer-owned context. The host owns migrations. |
| `AgentEntityFrameworkCoreRegistration.AddAgentStores<TContext>` | Selects the stores over a registered context without requiring an Elsa base class. |
| `WeaverDbContext` and four provider contexts | Optional module-owned models with provider-specific migrations. |
| `WeaverEfModule` | Stable migration history identity (`ElsaAgent`) and canonical schema chain. |

Consumers use the engine-free [Agent replacement contracts](../../Elsa.Weaver/EXTENSION_POINTS.md):
`IAgentSessionRecordStore` and the audit sink, reader and ownership, selected through
`AgentPersistenceSelection`. Entities, the codec and the EF stores remain internal. Generated
migration entry points appear in the package API baseline. See [registration and limits](README.md).
