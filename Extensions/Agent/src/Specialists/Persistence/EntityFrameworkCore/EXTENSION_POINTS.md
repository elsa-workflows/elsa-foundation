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

## Agent source ports

`AddAgentSourceStore<TContext>()` composes `IAgentSourceReader` and `IAgentSourceWriter` over the
shared `EfSourceRevisions` implementation in a consumer-owned context mapping `AddSourceRevisions`.
`AgentSourcesEntityFrameworkCore` / `AddAgentSourcesEntityFrameworkCore` supplies the built-in contexts
without enabling the turn runtime. The existing combined journal registration includes source ports.
Use one built-in context configuration per shell. The scoped context may have no pending unrelated
writes when a source operation starts; domain-owned transactions use shared staged operations instead.
See [README](README.md#agent-source-authoring) and the [shared capability](../../../../../../Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md#shared-source-revisions).
