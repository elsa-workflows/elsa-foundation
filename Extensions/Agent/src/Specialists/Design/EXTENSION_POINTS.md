# Specialist authoring contributions

`Elsa.Agent.Specialists.Design` owns `SpecialistsDesign`; its provider implementation is internal.
Public authoring remains the ordinary Activity Design and Publishing APIs. There is no separate
specialist definition/version/draft catalog or CRUD store.

## Cross-domain contributions

| Contract | Contribution and lifetime |
|---|---|
| `IActivityProvider` | Scoped owner of `elsa.specialist/1`; manifest validation, Done-outcome proposal, capabilities and same-schema migration. |
| `IActivityTemplateProviderCompiler` | Scoped compiler of immutable business schemas, typed native facade and background model-turn child. |
| `IActivityTemplateDependencyDiscoverer` | Scoped owner reporting no authored activity dependencies for the initial specialist manifest. |

The generic provider/compiler/discovery registries are scoped, so selected persistence access and
scoped model contributors remain inside the publication operation. Stateless existing contributors
may retain singleton lifetimes. Register contributions through DI in every operation; a mutation of
one scoped registry does not configure other operations.

The provider reads the existing published-head baseline and contributes conservative major-version
changes for changed or unclassifiable business constraints. The generic publisher owns authorization,
review invalidation, version floors, idempotency, head/revision concurrency and every durable write.
Prompt-only implementation changes use its ordinary implementation compatibility floor. Presentation
labels and descriptions do not become model instructions.

Normal placement supplies exact version/template/source-reference stamps and rewrites child node
identities. Runtime uses those pins and child slots. See the [model contribution contract](../Core/EXTENSION_POINTS.md)
and [runtime catalog](../Elsa.Agent.Specialists/EXTENSION_POINTS.md).
