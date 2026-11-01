# Specialist authoring contributions

`Elsa.Agent.Specialists.Design` owns `SpecialistsDesign`; its provider implementation is internal.
Public authoring remains the ordinary Activity Design and Publishing APIs. There is no separate
specialist definition/version/draft catalog or CRUD store.

## Cross-domain contributions

| Contract | Contribution and lifetime |
|---|---|
| `IActivityProvider` | Scoped owner of `elsa.specialist` manifests1/2; validation, outcome/control proposals, capabilities and explicit forward migration. |
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

## Explicit continuation contracts

Manifest1 and descriptor1 retain the original fresh-only public invocation semantics. Opting into
manifest2 with `AllowContinuity=true` requires accepting the provider's optional `AgentOptions` input
and required `Conversation` output. Their stable keys are `$specialist.options` and
`$specialist.conversation`. Compile-time validation checks their exact shape and refuses collisions
in names, dotted output roots or the reserved key namespace ignoring case. Existing schema1 business
members are not reinterpreted. Migration1→2 preserves manifest content and still requires those
reviewed contract proposals; reverse migration is refused.

`AgentOptions` may be absent or `{}` for a fresh conversation. To continue, pass the prior
`Conversation` value, shaped as `{"conversationId":"...","revision":1}`. Its revision is checked as
the expected accepted base revision, not a request to select older history. Explicit null, unknown
members, caller owner/tenant fields, malformed IDs and invalid revisions are refused. Native nullable
input materialization also represents optional absence; the control reader independently rejects
explicit null. Controls are excluded from model business inputs and schemas. `Conversation` is added
only after accepted business output and contains the safe ID/revision, never protected history.
Existing native output captures and variable bindings can carry that value between calls.

The compiler emits descriptor2 and an explicit control profile for this contract, with a new compiler
fingerprint. Runtime retains descriptor1 readers; a profile/schema mismatch is refused. Changing the
native metadata/whole-result contract contributes a major compatibility change through the generic
publisher. Manifest2 with continuity disabled remains fresh-only and cannot publish retained control
members. Budget overrides and other later invocation capabilities are not part of this control profile.
