# Activities Design Disclosure extension points

Core contracts belong to `Elsa.Activities.Design.Core`. This feature owns their orchestration. See the
[catalog plan](../../../../../specs/1219-entity-aware-integrations/plan.md) for the full unit's remaining
integration and qualification; these registrations alone protect no existing catalog reader/writer.

## Registration source

`IActivityCatalogDisclosureRegistrationSource` *(Core — `Elsa.Activities.Design.Core`)* is a **Source**
(pull) with signature `IEnumerable<CatalogDisclosureRegistration> Read()`. It returns static
policy/producer registration metadata. Register a source with
`services.AddScoped<IActivityCatalogDisclosureRegistrationSource, MySource>()`, or use
`AddActivityCatalogDisclosurePolicy<TPolicy>(key, schema)` and
`AddActivityCatalogProducer<TProducer, TAdmissionOwner>(id, scope, minimumPolicy, aliases)` on the
service collection. `CollectActivityCatalogDisclosureRegistrations` is its single aggregating handler
for `ActivityCatalogDisclosureInitializing`.
Contributors register sources rather than adding another aggregation handler. The frozen singleton
registry keeps metadata only; it retains no scoped policy, producer, provider or permission decision.

Policy registrations select a keyed scoped `IActivityCatalogDisclosurePolicy` by exact ordinal policy
key and binding schema. Producer registrations bind an exact keyed scoped producer, its interface aliases
and an `IActivityCatalogAdmissionOwner` classification strategy. The registration helpers are documented
by their public contracts in the feature's `Extensions` directory. Unknown, duplicate or invalid
registrations are diagnosed; a source/provenance string cannot select Public admission.

**Known source implementations:** `CatalogDisclosureServiceRegistration.MetadataSource`
*(intra-domain — default, `Elsa.Activities.Design.Disclosure`)* is registered as an immutable singleton
by the two helpers. Each instance returns one policy or producer descriptor and captures no scoped
service. There is no shipped source-specific policy or producer classifier in this foundation;
hosts supply the exact keyed `IActivityCatalogDisclosurePolicy` and `IActivityCatalogAdmissionOwner`
strategies. Their signatures are respectively
`ValueTask<CatalogDisclosureDecision> EvaluateAsync(CatalogDisclosureResource, CatalogDisclosurePolicyRequirement, CatalogDisclosurePurpose, CancellationToken)`
and `ValueTask<CatalogDisclosureRequirement> ClassifyAsync(CatalogAdmissionCandidate, CancellationToken)`.
The scoped evaluator and admission service consume them; they are keyed strategies rather than a
second fan-in flow.

## Replacement contracts

| Core contract | Default implementation / lifetime | Responsibility |
|---|---|---|
| `IActivityCatalogDisclosureEvaluator` | `ActivityCatalogDisclosureEvaluator`, scoped | Validate resource/requirement and current authority; evaluate the complete keyed conjunction |
| `IActivityCatalogAdmission` | `ActivityCatalogAdmission`, scoped | Validate current producer origin and ownership; retain classification plus inheritance |
| `ICatalogProducerScope` | `CatalogProducerScope`, scoped | Resolve/memoize the actual producer and a scope-specific binding; never cache authorization |
| `IActivityCatalogDisclosureAuthority` | Unavailable default, scoped | Report trusted current identity availability; presence is not permission |
| `IActivityCatalogOwnerScopeAccessor` | Unspecified default, scoped | Supply trusted current provisioning scope; never accept authored tenant switching |

All five contracts above have the *(Core — `Elsa.Activities.Design.Core`)* layer badge; their defaults
belong to `Elsa.Activities.Design.Disclosure`. Signatures and current consumers are:

| Contract | Signature | Consumer and override use |
|---|---|---|
| `IActivityCatalogDisclosureEvaluator` | `ValueTask<IReadOnlyList<CatalogDisclosureDecision>> EvaluateAsync(IReadOnlyList<CatalogDisclosureResource>, CatalogDisclosurePurpose, CancellationToken)` | Explicit host callers; existing catalog readers are not integrated yet. Replace for another complete disclosure decision implementation. |
| `IActivityCatalogAdmission` | `ValueTask<CatalogDisclosureRequirement> AdmitAsync(CatalogAdmissionOrigin, CatalogAdmissionCandidate, CancellationToken)`; `ValueTask ValidateAsync(CatalogAdmissionOrigin, CatalogAdmissionCandidate, CatalogDisclosureRequirement, CancellationToken)` | Explicit trusted provisioning callers; existing writers are not integrated yet. Replace for another complete admission implementation. |
| `ICatalogProducerScope` | `object GetBinding(string)`; `CatalogAdmissionOrigin GetOrigin(string)` | `ActivityCatalogAdmission` and explicit host producer callers. Replace only while preserving exact scope/alias identity. |
| `IActivityCatalogDisclosureAuthority` | `bool IsPresent { get; }` | `ActivityCatalogDisclosureEvaluator`. Replace the unavailable default with a trusted current-identity adapter. |
| `IActivityCatalogOwnerScopeAccessor` | `CatalogOwnerScope Current { get; }` | `ActivityCatalogAdmission`. Replace the unspecified default with trusted provisioning ownership. |

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
