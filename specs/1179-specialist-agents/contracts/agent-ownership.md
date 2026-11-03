# Agent source ownership and activity publication

**Status**: Draft, provisional amendment under [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md). Design-only; implementation is blocked pending final human review and separate delivery authorization. [#3781](https://github.com/elsa-workflows/elsa-foundation/issues/3781), part of #3519.

This contract specifies the proposed ownership, transaction and migration shapes. The ADR owns the architecture decision and alternatives; the glossary owns terminology. Logical names below are not frozen C# signatures or new package names. Existing implementation identifiers and artifact formats remain compatibility inputs.

## Ownership and identity

| Logical resource | Content authority and invariant |
|---|---|
| AgentDefinition | Agent source owner; stable scoped identity, metadata and explicit source authority |
| AgentDraft | Agent authoring; revision-CAS instructions, model reference, typed public contract and policy |
| AgentPublicationCandidate | Immutable exact draft/configuration snapshot plus reviewed version, fingerprint, compiler/profile, expected heads, trusted source reservation and operation identity |
| AgentVersion | Immutable source identity/content selected by a confirmed shared publication, or an explicit adopted legacy-version association |
| AgentActivityProjection | Read-only association of exact Agent version to existing activity definition/version, template hash, source reference and publication receipt; never a second content editor |
| Agent publication status/index | Derived from authoritative shared receipt/lifecycle evidence; repairable without re-running compilation or model inference |

One Agent-owned typed contract defines stable member keys, names, aliases/collection shapes, required/null/default behavior and compatible JSON refinements. Model schemas and the public activity contract derive from that one source. Proposed Agent Core source shapes do not depend on Activities Design DTOs. The publishing bridge maps through public Core contracts; runtime consumes the compiled contract already present in the artifact.

Agent-facing APIs own create/edit/fork/history and authoring permissions. Generic activity authoring must refuse independent content and recommendation/lifecycle writes to derived Agent-source-owned content, even for a caller who can otherwise manage activities; authorized Agent commands delegate shared lifecycle primitives through the trusted bridge. Read visibility of a catalog entry alone grants no Agent authoring or invocation right. Source authority, tenant/partition and principal are resolved by trusted services; caller-authored source-kind, owner or projection fields cannot claim authority.

The Agent collection queries Agent sources/drafts, including unpublished ones, using authorized server-side filtering, counting and stable pagination. The workflow picker queries the existing activity catalog. No second activity picker or catalog is introduced. Source-owned configuration is read-only; an authorized copy/fork creates a new Agent definition and provenance.

## Publication protocol

1. Save/identify a source revision and freeze an immutable candidate, including its exact public contract, instructions, model/settings/policy and proposed version. Candidate storage contains references to credentials, never credential material. A configuration source supplies explicit version and source fingerprint through the same candidate boundary.
2. Evaluate through the registered provider compiler and shared preflight: schema/defaults/constraints, version compatibility, model capabilities, runtime consumers/codecs, storage and exact dependencies. No inference is required for publication. A failed candidate cannot become a selectable published activity.
3. Issue a review token binding the candidate, trusted authority, expected source/publication heads, compiler/profile and relevant readiness. Before final commit, validate/reserve publication against the expected source authority/revision and heads. A conflicting ownership/lifecycle action must serialize with this reservation or return conflict/pending. No unconfirmed reservation may be reused for different content.
4. The shared publisher commits the generated activity version, compiled template, source reference and receipt using its existing idempotency and commit contracts. Generic candidate evaluation must be shared by interactive and source-owned provider publication; duplicating specialist-only publication code is forbidden.
5. Resolve publication outcome from that receipt. Only a confirmed successful receipt makes a new Agent version published. Agent-side indexes may be repaired; a delayed index update cannot demand another publication or mutate the retained candidate. Catalog projection and Agent reads expose consistent immutable source identity and explicit readiness.

| Loss/race point | Required behavior |
|---|---|
| Before candidate persistence/reservation | No runnable publication; normal exact revision retry |
| Candidate durable, shared commit not confirmed | Retain candidate and operation; report pending/unknown, query the existing receipt before retry |
| Shared commit durable, reply or Agent index update lost | Reconcile the original successful receipt and repair the index; return the same published identities |
| Same operation and material repeated | Return the existing candidate/receipt/version |
| Same operation with other content/scope/profile | Conflict; never overwrite or start a replacement publication |
| Source ownership, retirement or cancellation races final admission | Reservation policy serializes the action, or reports conflict/pending; resolve any unknown shared outcome before releasing source/version claims |
| Compiler or capability profile changed since review | Reject stale preflight, or require a newly reviewed candidate; never reinterpret an existing Agent version |

No cross-store atomicity is assumed. The implementation must qualify the reservation/receipt protocol with stores independently unavailable, rather than infer success from an outbox message or silence. It must specify repair and cancellation for stranded pre-commit reservations; a timeout alone cannot prove absence of the shared commit. Version names cannot be reused for different material while a prior outcome is unknown.

One primary activity projection follows the Agent version's public SemVer. Internal compiler/profile and template hashes retain distinct meanings. A published version always selects its retained publication; a new compiler cannot silently replace it. Copy/fork creates new source identity. Recommendation and retirement govern future selection; existing workflow bindings stay exact. Operational execution revocation remains an independently enforced domain gate.

## Runtime and invocation

Native and BPMN workflow publication pins the selected exact template and its behavior into the workflow artifact. Artifact execution requires no Agent authoring database, draft, mutable latest-version lookup or Design/Publishing assembly. The existing model connection authorization, journal, request reservation, uncertain outcome and accepted-versus-applied decisions remain unchanged.

An Agent playground test freezes an exact candidate and uses the same admitted runtime path with a candidate-scoped, nonpublished execution reference. It cannot manufacture a published Agent version. A future standalone API uses a deployed immutable execution reference and the same execution/journal semantics; it is not a model-call shortcut and is not delivered by this design amendment.

Agent history and invocation evidence link both Agent source/version and derived publication/native identities. The user view leads with public inputs/results and meaningful state. Internal model-turn handles remain technical references, not business outputs; diagnostic drill-down remains available without widening protected-content access.

## Adoption and cutover

| Legacy material | Preservation and adoption rule |
|---|---|
| Existing published Agent/activity version | Record its Agent association; retain exact definition/version IDs, SemVer, template/source references, hashes, recommendation/lifecycle states and admission restrictions. Adoption never reactivates retired/revoked content. Do not recompile or change its bytes |
| Existing workflow/BPMN artifact | Keep its original binding and reader. No implicit upgrade, import rewrite, template regeneration or hash normalization |
| Existing Agent draft | Explicit snapshot and revision-CAS transfer. Fence/reject stale legacy writes; preserve local conflict recovery. No transfer by mutable display name |
| Existing configuration authority | Preserve source ID and operator version. Read-only in Studio; explicit fork produces new Agent-owned content |
| Existing conversation/turn/accepted receipt | Preserve version binding, scope, revisions, operation/lease/fence semantics and protected content. Adoption creates no model call and does not migrate history to another version |
| Old public/wire/package identity | Retain `elsa.specialist`, `Specialist*`, schema/codec identities, feature names, paths and persisted fields while readers or consumers require them. New terminology alone never rewrites stored payloads |

Map legacy definitions and versions explicitly using scoped stable IDs and verified provenance/fingerprints. Different-content/same-version, cross-tenant, missing-source or ambiguous mappings stop with diagnostics. Mapping to an adopted legacy version records its original publication fingerprint, not a claim that new source compilation is byte-identical.

Before transfer, legacy authority remains authoritative. After transfer, Agent authority is the single writer; compatibility routes may redirect, reject or translate through that same authority with exact revision checks. They cannot keep an independent writable copy. Mixed deployments need a persisted ownership epoch/fence so stale writers cannot win. New pending drafts and new Agent versions do not change any already published identity.

Recovery/retry of adoption is idempotent and resumable. Partial transfer leaves an explicit owned/pending state and never two writers. Rollback uses verified readable schema/epoch bounds: it may disable new authoring while retaining artifact execution, but cannot downgrade to an incompatible reader or restore legacy writers over new authority. Acceptance must cover active operations and accepted-but-unapplied turns throughout cutover and rollback.

## Acceptance witnesses

| Witness | Required positive and causal negative | Planned task |
|---|---|---|
| W1 — Source authority | Agent CRUD/CAS works independently of generic activity CRUD; direct generic edit of a generated Agent is refused | T065,T069 |
| W2 — One contract | Agent interface generates equivalent activity/model schemas; conflicting separately authored generated members are rejected | T066 |
| W3 — One publication | Crash at every candidate/reservation/commit/index boundary converges on one receipt/version; changed material and ambiguous authority cannot replay | T066,T069 |
| W4 — Frozen execution | Changing/deleting/unavailable authoring source does not retarget an existing native/BPMN artifact; missing reader is diagnosed rather than resolved by latest source | T067,T069 |
| W5 — Legacy preservation | Before/after retained-package run has identical old version/template/artifact identities and correct result contract; a reserialization, guessed association or cross-scope collision fails | T067,T069 |
| W6 — Conversation/recovery | Active operations and accepted results replay without a new model request or history revision reset; accepted-but-unapplied state remains distinct | T067,T069 |
| W7 — One writer/rollback | Stale legacy saves fail after cutover; partial migration and older-reader activation cannot restore two owners or lose old artifact interpretation | T067,T069 |
| W8 — Product and configuration | Real Agent create/test/publish/use/inspect and config source share the pipeline; unauthorized/source-owned edits fail; Agent labels coexist with old compatibility keys | T068,T070 |

All witnesses remain unexecuted for this amendment. Existing M0–M5 receipts are regression inputs, not proof of W1–W8. Proposed tasks may be elaborated into implementation leaves only after human agreement and separate authorization.
