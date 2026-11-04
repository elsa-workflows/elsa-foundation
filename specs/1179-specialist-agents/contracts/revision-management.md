# Shared source revision management

**Status**: Accepted direction under [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md), merged #3783; source delivery authorized in #3790. This is the canonical responsibility mapping. Concrete source placement and the partial-delivery boundary live in [plan.md](../plan.md#3790-source-slice-placement-and-transition). Publication coordination remains T066. [Agent ownership](agent-ownership.md) adds domain rules; meanings live in the [glossary](../../../docs/glossary/elsa.md).

## Selected editing model

Use reusable mutable working copies plus immutable snapshots at test, review and publication boundaries. This preserves Elsa's working-copy experience and Workflow's domain diff commands without storing a full immutable copy for every autosave. Append-only-on-every-save is a feasible alternative, not a requirement. Additional branches or multi-draft collaboration are not introduced here; working-copy identity leaves existing domain cardinality policy explicit.

| Shared responsibility | Minimum state / invariant |
|---|---|
| Source identity | Exact tenant/partition + source kind + stable source ID; trusted authority and concurrency generation; presentation names never identify a source |
| Working copy | Source + working-copy ID; domain-typed content/schema profile, CAS revision, optional base snapshot/release provenance; domain controls edits and allowed copies |
| Immutable revision snapshot | Scoped exact revision ID, source identity, captured working-copy revision, canonical content fingerprint, payload/schema profile and provenance; write once |
| Snapshot reference/pin | Operation, retained release or nonterminal test references the exact revision; no garbage collection while referenced |
| Publication operation | Scoped operation ID, revision ID, release label, expected working-copy revision and source/publication heads, compiler/profile, reviewed readiness fingerprint, reservation/fence and recoverable status/receipt reference; no independent source content |
| Release association | Source + public SemVer + exact revision + confirmed publication receipt/reference; immutable association with separately mutable recommendation/lifecycle policy |

Only the first four responsibilities belong to reusable source revision storage. Publication operation/release coordination belongs to shared publishing, using source CAS/reservation ports. Domain-facing views may say Agent draft/version or Workflow draft/version, but must not own another snapshot store or publication authority.

Working-copy concurrency revisions are not release SemVer. Snapshot IDs/fingerprints are not template hashes. Schema profiles describe serialization/interpretation. A source snapshot does not assert that validation passed or a runnable artifact exists.

## Shared operations and boundaries

| Operation | Shared mechanics | Domain responsibility |
|---|---|---|
| Create/read/list | Scope-safe source/working-copy storage; bounded query support and exact revision reads | Trusted scope, permissions, typed payload, filtering/read-model policy and draft cardinality |
| Save | Store-side atomic expected-revision CAS; payload/revision/provenance update together | Apply typed commands/diff and validate resulting content; preserve losing local edits |
| Capture snapshot | Atomically check expected working-copy revision/authority and persist exact immutable content; operation-key replay returns original snapshot and rejects changed material | Canonical serializer/schema profile; select test/review boundary; exclude secrets and irrelevant presentation metadata from behavioral fingerprints |
| Fork/discard | Shared provenance/reference-aware working-copy primitives | Fork permission/new authority and discard policy; never delete retained snapshots |
| History/read exact | Reusable revision/provenance and bounded history access | Domain presentation/content visibility; no universal public JSON CRUD endpoint |
| Reserve publication | Shared coordinator CASes authority and expected publication heads; pins exact snapshot | Domain validation/compatibility, release policy, compiler and runtime requirements |
| Finish/reconcile | Receipt-authoritative shared publication, idempotent release association and repairable indexes | Domain outcome/inspection view; recommendation/activation remains a separate permitted command |
| Collect unreferenced revisions | Explicit retention policy with atomic pin/delete exclusion | Protected-content policy and history expectations; published material follows publication retention rules |

CAS and pin/delete exclusion must hold in the persistence provider, not only service read-before-write checks. Source capture has a local atomic boundary; source/publication stores may be independent. Unavailable dependencies yield explicit failure/pending, never bypass of scope, review or receipts. Generic lifecycle events alone do not establish transactional publication.

Extract mechanics behind owning Core contracts; typed adapters supply serialization, mutation and validation. Core source types stay independent of Activities Design DTOs and executable representations. Concrete EF/query implementations stay in persistence features with CQS boundaries. Package placement/public generic signatures wait for both real adapters to demonstrate the dependency boundary. A base entity over separate engines is insufficient.

## Workflow and Agent mapping

This is an implementation map, not a claim that current stores already satisfy it.

| Domain operation | Workflow adapter | Agent adapter | Shared responsibility |
|---|---|---|---|
| Create definition/draft | Workflow metadata + graph working copy | Agent metadata + instructions/model/typed contract/policy working copy | Source identity and working-copy creation |
| Edit/save | Existing graph-aware diff commands, validation and local undo semantics remain | Agent field/contract commands and validation | Same CAS update primitive; different payloads/commands |
| Test/review | Capture exact Workflow source and revision evidence | Capture exact Agent source and revision evidence | Same immutable snapshot capture/pin and replay rules |
| Promote/select source version | Promotion becomes a view/association to an exact validated snapshot; a source version alone does not prove executable publication | Agent release selects an exact validated snapshot | Shared source history; domain SemVer policy and permission |
| Compile/publish | Resolve exact activity publications and build runnable workflow material | Derive ports/model schemas and build the activity template | Shared operation identity, review/reservation and commit/reconciliation; domain-specific compiler outputs |
| Recommend/activate | Select a confirmed published workflow | Select a confirmed published Agent activity | References existing publication; never substitutes for its receipt |
| Inspect | Workflow source/release/artifact provenance | Agent source/release/activity provenance | Shared revision history/receipt associations; domain read views |

Existing Workflow definition/draft/version stores and promotion commands, reusable-activity draft/CAS stores, and publication commit/receipt contracts are extraction inputs. Preserve Workflow semantic commands, layout separation and publisher commit contracts. Do not equate source promotion with publication or claim current publishers already share every step. Extract operation/reservation/reconciliation where both adapters demonstrate the same invariant; compilation stays domain-specific.

Assess reusable-activity authoring as a third existing consumer before interface freeze. It may adapt to the capability but must not become Agent's editable source again. A future Rules/Decision-like source supplies typed content, validation, permissions and a compiler contribution while reusing revision/publication mechanics without Workflow graph inheritance. No new domain is admitted here.

## Publication and failed-publication recovery

For either adapter:

1. Allocate scoped operation identity P, then capture immutable snapshot S from the expected working-copy revision. Capture durably records its source-side operation intent and pin atomically with S; pin acquisition and collection exclude one another. Initialize shared publishing operation P referencing S, proposed release, scope/authority, expected heads and compiler/profile. A crash between capture and publishing-operation creation leaves a recoverable source intent/pin; replay initializes the same P. It is not runnable publication and cannot become an unprotected dangling reference. A snapshot captured for history alone has no publication claim and follows ordinary history retention.
2. Run domain validation and shared compile/preflight. Review binds exact S/content, profile, dependencies/readiness and expected heads. Definitive rejection leaves no runnable publication. Correct the working copy and submit a new snapshot/review/operation; P never changes source or release material.
3. At commit admission, recheck the reviewed working-copy revision (or explicit configuration fingerprint), source authority and expected heads through coordinator/CAS ports, then reserve the operation. A changed working copy before admission requires fresh review; after admission later saves cannot change the retained snapshot. The source reservation, intent and pin must be durably recoverable together in the source store, linked to the recoverable publishing operation by P. A crash cannot leave an ownerless claim; this does not require an atomic transaction across the two stores. Later saves do not mutate S. Competing cancellation/ownership/lifecycle/head changes serialize or conflict.
4. Existing publishing commits compiled output and authoritative receipt R. Success is not inferred from an event, source-side Published flag, compilation or timeout. Source indexes/release associations may be repaired from R.
5. Reconcile P against R after restart. Same operation/material returns original identities. Changed snapshot/scope/release/profile conflicts. Unknown outcome retains reservation/pin; do not republish different material or reuse the release claim. A stranded reservation or capture intent/pin is released only when the publisher's durable outcome/claim protocol proves safe cancellation or no possible commit, including preventing a delayed publisher from later committing under P; silence or a missing receipt alone is insufficient. Completed publication transfers retention to the confirmed release association before an operation pin is removed.

**Concrete example, both domains:** Copywriter working-copy revision 7 becomes snapshot A; Editorial Workflow revision 12 becomes snapshot W. Each has its own scoped operation. Copywriter `1.2.0` fails schema validation: A stays unpublished, with no activity or release-success index. Fixing revision 8 produces snapshot B and a new reviewed operation. Publication commits receipt R for B, then source storage becomes unavailable before the Agent index updates. Recovery reads the same operation's R and repairs the association to B without recompilation, another version or inference. If R is unreadable, status stays pending.

For Editorial Workflow, the same lost-reply sequence repairs W's release/artifact association through its adapter. A missing required activity publication rejects W before commit. Source promotion may already identify W, but does not make it runnable. A common coordinator implements retry/reservation/repair; Agent/Workflow compilers and output commit adapters retain their distinct responsibilities. No cross-store transaction is assumed.

## Extraction versus framework adoption

Choose focused extraction first. Orchard Core demonstrates shared identity/version/lifecycle services; Aster demonstrates immutable snapshots separated from activation. Neither alone supplies Elsa executable publication. Aster adoption would also require evaluation of resource/aspect/facet storage, query translation, ordinal-to-SemVer mapping, authority/receipt coordination, protected content and host/package closure. Its Elsa 3 adapter is not an Elsa 4 integration. Sources/tradeoffs live in [research R10](../research.md#r10--shared-revision-management-orchard-core-and-aster).

Before freezing contracts, demonstrate Agent and Workflow adapters using one working-copy/snapshot implementation and shared publication coordination invariants, with concurrent-save, capture-race and lost-reply proofs. Duplicated engines hidden behind interfaces require revisiting this decision before expanding APIs. No quantitative code-reduction or third-party readiness claim is made.

## Pre-GA reset boundary

No import/backfill, old-schema reader, compatibility alias, cross-break rolling upgrade or historical-ID preservation is required. Planned breaking delivery may recreate databases after declaring affected stores/configuration/packages, stopping affected hosts and preventing incompatible binaries from admission. Clean initialization and explicit source recreation/republish restore readiness. Artifact binding, retention pins, scope and accepted-result replay remain mandatory within a supported deployment. This delivery performs no live reset and introduces no automatic drop-on-start behavior. See [ADR D7](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md#d7--pre-ga-clean-breaks-replace-compatibility-machinery).
