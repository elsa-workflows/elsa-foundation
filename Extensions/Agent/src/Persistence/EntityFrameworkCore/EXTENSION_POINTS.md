# Agent EF extension points

| Surface | Purpose |
| --- | --- |
| `FoundationAgentEntityFrameworkCoreFeature` | Opt-in shell feature `FoundationAgentEntityFrameworkCore`: provider, connection, schema, pooling and lease. Depends on `FoundationAgentAbstractions` and `Secrets`. |
| `AgentEntityFrameworkCoreRegistration.AddAgentEntityFrameworkCore` | Registers the module-owned contexts and the shared migration lifecycle, and selects the durable record store and audit store. |
| `AgentEntityFrameworkCoreOptions` | Explicit choices for that registration. |
| `AgentModelBuilderExtensions.AddAgentModel` | Installs only the agent mappings in a consumer-owned context. The host owns migrations. |
| `AgentEntityFrameworkCoreRegistration.AddAgentStores<TContext>` | Selects the stores over a registered context without requiring an Elsa base class. |
| `AgentDbContext` and four provider contexts | Optional module-owned models with provider-specific migrations. |
| `AgentEfModule` | Stable migration history identity (`ElsaAgent`) and canonical schema chain. |

Consumers use the engine-free [Agent replacement contracts](../../Elsa.Agent/EXTENSION_POINTS.md):
`IAgentSessionRecordStore` and the audit sink, reader and ownership, selected through
`AgentPersistenceSelection`. Entities, the codec and the EF stores remain internal. Generated
migration entry points appear in the package API baseline. See [registration and limits](README.md).
