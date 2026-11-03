---
status: proposed
date: 2026-11-04
---

# Agent definitions own authored content and providers publish activities

Status: Proposed — design-only for human review. Sipke approved the architectural direction and requested this draft on 4 November 2026; that instruction is not approval of the final text or authority to implement it. [Design unit #3781](https://github.com/elsa-workflows/elsa-foundation/issues/3781), part of [Elsa Agents #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).

## Context

Spec 1179 currently authors a user-defined agent as a generic activity definition. Its public contract belongs to Activities Design, its implementation settings are a specialist manifest, and the provider compiles the pair into an immutable execution template. The Agents collection and editor adapt the generic authoring APIs. This reuses proven revision, version, publication and runtime behavior, but gives Activities ownership of the Agent source.

Elsa 3's Agent activity provider projects independently owned Agent configuration into activity descriptors. That separation can be retained without returning to mutable configuration lookup during workflow execution. The choice of source owner is distinct from the choice to freeze behavior at publication.

The [previous research](../../specs/1179-specialist-agents/research.md#r1--publication-ownership-and-unpublished-discovery) rejects a competing Agent publication catalog. A canonical Agent source plus a read-only activity projection does not create two independently editable sources or two publication authorities.

The current source-owned bridge calls `CompileSourceOwnedRoot`; the interactive specialist path uses its provider compiler to produce a boundary and background model-turn child. Generating catalog rows alone cannot preserve those semantics. The shared source-owned publication path needs provider compilation and the same preflight, as already identified by spec 1179 plan D10.

## Decision

### D1 — Agent is the canonical term

Use **Agent**, **Agent definition**, **Agent draft**, **Agent version**, **Agent conversation** and **Agent turn** for this product and its proposed domain model. Specialist is a descriptive role, not a separate entity kind or lifecycle. Weaver remains the named Studio assistant experience; a workflow execution actor remains an execution actor. Context qualifies these meanings without a second product noun for user-defined agents.

Existing `Specialist*` CLR/package/feature identifiers, `elsa.specialist` provider and consumer keys, schema versions, codecs, endpoints, persisted JSON names and database identities remain compatibility identifiers. This decision authorizes no blanket rename, alias substitution, package move or wire/schema change. New API/package signatures require their own implementation review. The pinned root remains `Elsa.Agent`, not a new `Elsa.Agents` domain. Meanings live in the [glossary](../glossary/elsa.md).

### D2 — Agent owns authored source

The Agent domain owns stable definition identity, mutable draft/revision, the typed public input/result contract, instructions, model reference, settings and policies. It owns authoring, publication intent, content authority, version selection, history and permitted lifecycle actions through Agent-facing contracts and permissions. Its logical records are independent of the generated activity representation; this decision does not require a separate physical database.

One Agent contract generates both the activity contract and model JSON schemas. Generated members are not independently editable. Configuration-defined and Studio-authored Agents have explicit source ownership and use the same validation and publication boundary. Copy/fork establishes new Agent source authority and provenance rather than making the generated activity editable.

### D3 — The activity provider derives one workflow representation

The provider projects an exact immutable Agent candidate into the existing activity catalog and execution-template system. It translates the public contract, validates Agent semantics and compiles through the existing provider contribution. Generated activity content is Agent-source-owned and read-only through generic activity authoring APIs; authorized users navigate to its Agent owner to edit it. Independent generic recommendation/retirement/revocation writes must also be refused or delegated through Agent authority; the trusted bridge executes shared lifecycle primitives without granting broad activity management as Agent permission.

There remains one activity catalog for native and BPMN authoring. Unpublished Agent drafts are discoverable in the Agent domain without manufacturing published activity rows. Display names do not determine identity. New mappings are scoped and explicit, bound to Agent/version identity and the compilation profile; migrated identities are retained by recorded mapping, not recomputed from a new naming formula.

### D4 — Publication has one runnable authority

Freeze and persist one exact candidate before compilation. It includes source revision/authority, Agent/version identity, public contract, behavior and content fingerprint. Shared publication validates compatibility, model capabilities, runtime/storage requirements and exact dependencies, then commits the immutable activity version, template, reference and publication receipt.

Preflight and review tokens bind the exact candidate and authority, expected heads, candidate version, compiler/profile and the relevant readiness facts. Final admission reserves the source publication operation against its expected authority/revision. Competing head changes, cancellation, ownership transfer and retirement must serialize against that reservation or report conflict/pending. A reservation is never released or reused while its shared-publication outcome is unknown. Saving a later draft cannot mutate a reserved immutable candidate.

The shared receipt determines whether publication succeeded. An Agent-side publication pointer or list status is a repairable projection, never a second successful-publication authority. The source candidate is durable before a catalog publication can refer to it. Unknown receipt reads produce an explicit pending/unknown answer; they never mean unpublished, failed or safe to publish different content under the same operation.

Retry/restart uses the same scoped operation identity and exact candidate. An equivalent repeat converges on the existing receipt; different material conflicts. A crash after the shared commit but before the Agent index update requires reconciliation, not another publication. This holds with separate stores and makes no distributed-transaction claim.

### D5 — One version selects fixed behavior

For the primary activity projection, Agent and generated activity versions share the user-facing version; the activity has no independently edited version stream. Content hashes, compiler identity and projection provenance distinguish their internal representations. A published Agent version selects its retained compiled publication. Installing a new compiler must not silently regenerate it. Behavior-changing compilation requires a newly reviewed Agent version.

Recommendation, retirement and runtime admission retain their distinct meanings. Changes affect future selections/admission under their documented domain gates, never rewrite existing workflow artifacts. Operational connection authorization and credential rotation stay with Connections/Secrets; secrets do not enter authored source or artifact hashes. A pinned model reference does not promise deterministic remote model output.

### D6 — Retain the existing execution boundary

Workflow publication places the exact retained template into the runnable artifact. Runtime requires that artifact and its deployed interpreters, not Agent drafts, source definitions or design-side catalog reads. Reuse the native facade, model-turn worker, journal, request reservations, accepted-result replay and acceptance-versus-process-application settlement.

A future direct Agent invocation must resolve a deployed immutable execution reference and use the same admitted execution/journal semantics. This ADR does not implement a direct invocation API, another scheduler, a model-call shortcut or a second artifact store. Runtime inspection should lead with the Agent's inputs/results and exact version while retaining internal-step evidence for diagnosis; that presentation is independent of source ownership.

### D7 — Preserve existing published identities and behavior

Adoption is additive. Preserve existing Agent/activity definition and version identifiers, SemVer values, source references, templates, behavioral hashes, compiled workflow bindings, existing recommendation/lifecycle/admission states, runtime descriptor readers and accepted journal/conversation identities. Adoption cannot reactivate retired or revoked content or broaden access. Preserve exact persisted values and their interpretation; migration does not normalize or reserialize old behavior to a new hash.

For each supported legacy version, record an Agent-to-existing-publication association. It identifies the retained version as legacy publication material without pretending that a new compilation produced the old artifact. Same-version/different-content or ambiguous scope/authority mappings stop with a diagnostic. No name-based guessing, automatic republish or implicit latest-version upgrade is allowed.

Draft transfer is explicit and revision-fenced. Concurrent legacy editors cannot overwrite the new owner. A staged cutover establishes one writable source per definition; compatibility reads or command translation do not establish another writer. Configuration ownership remains read-only, and an explicit fork creates new Agent-owned content. Existing accepted turns, active native operations and conversation revisions retain their old exact identities and readers; importing a definition does not start a model call or migrate conversation history.

Rollout and rollback use a declared readable schema/profile and ownership epoch. Rollback to an older reader is refused before activation if it cannot interpret the new records; it must never restore competing writers or reset source/publication/journal state. Existing artifacts remain executable through their retained readers. No current store, host or live workflow is changed by this design PR.

## Alternatives and consequences

| Alternative | Benefit | Cost / disposition |
|---|---|---|
| Keep activity-owned source | Least new persistence and lifecycle work; current guarantees already exercised | Agent source, permissions and public contract remain constrained by generic activity authoring; not selected as the target |
| Agent API/editor facade over current source | Improves terminology and authoring at lower migration cost | Useful transition, but generic activity write authority must not be presented as independent Agent ownership |
| Agent-owned source and provider-generated publication | Clear source authority and one derived workflow representation | Selected; requires explicit migration and crash-safe coordination with shared publication |
| Independently editable Agent and activity definitions | Allows divergent editing | Rejected: competing contract/content authorities |
| Live descriptor/configuration lookup at workflow execution | Small integration surface | Rejected: does not preserve the selected immutable behavior without an additional publication boundary |
| Separate Agent publication engine and runtime | Full infrastructure independence | Rejected: duplicates existing publication, recovery and execution ownership |

The selected design adds logical source records, source-to-publication coordination and migration work. Reuse proven publication and persistence contracts where they fit; extract shared mechanics only for demonstrated consumers. It does not justify a universal callable-resource abstraction or an eager project hierarchy. Agent Core must not acquire Activities Design implementation dependencies; the bridge composes through owning Core contributions.

## Agreement, propagation and implementation hold

This ADR and the [spec 1179 amendment](../../specs/1179-specialist-agents/contracts/agent-ownership.md) are proposed together. Spec D1/D2/D9/D10, publication/Studio contracts and the definition state model change; the existing native execution and acceptance guarantees remain. The prior approved baseline and delivered evidence are retained as historical evidence, not recertified as this design's implementation.

The PR must remain draft for human review. Required final-text review is requested from Sipke Schoorstra (owner/requester and Agent program steward), Joey Barten and Frans van Ek (technical architects). Their agreement on the final text is distinct from bot review and CI. This unit is outside an Initiative boundary; it does not edit an Initiative board or borrow its agreement.

No production implementation, migration, package/API rename, host change or record rewrite is authorized by this PR. Implementation requires final design agreement and a separately authorized delivery unit following the proposed task sequence. No constitution text is amended: artifact-only runtime and the persisted activity catalog are retained. Draft/provisional activity-versioning/Model X provisions are not ratified by this decision.
