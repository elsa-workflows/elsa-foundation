# Activities Design Disclosure extension points

Core contracts belong to `Elsa.Activities.Design.Core`. This feature owns their orchestration. See the
[catalog plan](../../../../../specs/1219-entity-aware-integrations/plan.md) for the full unit's remaining
integration and qualification; these registrations alone protect no existing catalog reader/writer.

## Registration source

`IActivityCatalogDisclosureRegistrationSource.Read()` returns static policy/producer registration
metadata. `CollectActivityCatalogDisclosureRegistrations` is its single aggregating event handler.
Contributors register sources rather than adding another aggregation handler. The frozen singleton
registry keeps metadata only; it retains no scoped policy, producer, provider or permission decision.

Policy registrations select a keyed scoped `IActivityCatalogDisclosurePolicy` by exact ordinal policy
key and binding schema. Producer registrations bind an exact keyed scoped producer, its interface aliases
and an `IActivityCatalogAdmissionOwner` classification strategy. The registration helpers are documented
by their public contracts in the feature's `Extensions` directory. Unknown, duplicate or invalid
registrations are diagnosed; a source/provenance string cannot select Public admission.

## Replacement contracts

| Core contract | Default implementation / lifetime | Responsibility |
|---|---|---|
| `IActivityCatalogDisclosureEvaluator` | `ActivityCatalogDisclosureEvaluator`, scoped | Validate resource/requirement and current authority; evaluate the complete keyed conjunction |
| `IActivityCatalogAdmission` | `ActivityCatalogAdmission`, scoped | Validate current producer origin and ownership; retain classification plus inheritance |
| `ICatalogProducerScope` | `CatalogProducerScope`, scoped | Resolve/memoize the actual producer and a scope-specific binding; never cache authorization |
| `IActivityCatalogDisclosureAuthority` | Unavailable default, scoped | Report trusted current identity availability; presence is not permission |
| `IActivityCatalogOwnerScopeAccessor` | Unspecified default, scoped | Supply trusted current provisioning scope; never accept authored tenant switching |

Exactly one replacement is allowed per composed context. Register a host replacement before the feature
or replace its descriptor explicitly; adding a second implementation is not a last-registration-wins
override. Policy strategies and producer classifiers are keyed registrations, not global replacements.
Missing policy/schema is unavailable; only explicit valid Allowed decisions grant. Cancellation
propagates, and infrastructure failures use fixed outward diagnostics with their causes retained.

## ActivityCatalogDisclosureInitializing

- **Payload:** Core event with a get-only `ICollection<CatalogDisclosureRegistration>` initialized empty.
- **Meaning:** collect installed static policy/producer metadata before this registry is used.
- **Delivery:** Sequential, awaited through `IInlineEventPublisher`; no background/read-back shortcut.
- **Publisher:** `InitializeCatalogDisclosure`, once for each composed host's startup.
- **Handler:** exactly one `CollectActivityCatalogDisclosureRegistrations`, consuming all registration sources.
- **Failure:** invalid, duplicate, missing or uninitialized composition refuses; no Public/Allowed fallback.

## Tasks and handlers

The feature registers the one startup task and the one handler above. It registers no recurring or
background cadence. No persistence, HTTP projection, assistant history or Runtime consumer is wired by
this foundation batch; later integration must complete every applicable C01–C12 outcome.
