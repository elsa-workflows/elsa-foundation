# Agent authoring and publication contract

**Status**: Draft ownership amendment under [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md); design-only for human review. Proposed public signatures still require independent consumer proof.
Owning requirements: FR-001–FR-009, FR-021, FR-023, FR-027 and Program A1–A5/A12–A13. Configuration-defined agents ([plan D10](../plan.md#d10--configuration-defined-agents)) are source-owned versions compiled through the same provider compiler and preflight.

## Resource and provider identity

Agent owns typed content over the shared source/working-copy/snapshot capability. A candidate is an exact revision referenced by a shared publication operation. The [revision contract](revision-management.md) defines common mechanics, Workflow/Agent mapping and recovery; [Agent ownership](agent-ownership.md) defines permissions and projections. The provider derives source-owned, read-only activity publications using the existing catalog/template system. Agent-facing CRUD/permissions own source; generic activity writes cannot bypass that owner. No second activity catalog or publication engine is introduced.

Pre-GA implementation may cleanly rename provider/consumer/schema identities with coordinated store recreation; old-format aliases/readers are not required. `elsa.specialist` became `elsa.agent` this way under [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). Display-name changes never change identity or bindings. A new Agent version has one primary activity projection with the same user-facing version, while each representation retains its own content/provenance hash. Teams receive their own provider/schema only through their reviewed composition contract.

## One public interface, derived JSON

- Public input/output member reference keys, types, nullability, defaults and required flags remain
  owned by the Agent contract editor and exact Agent source revision; generated activity members are read-only.
- Agent source adds instructions, model reference/settings, policy and optional JSON constraints
  keyed by an existing public member. They cannot rename a member or silently contradict its type.
- Compile a top-level JSON object schema from business inputs and one from result projections.
  Preserve stable keys, required/null/default semantics and array/dictionary shapes; default to no
  undeclared members. Only registered JSON-compatible types are authorable; explicit boundary
  conversion is required for .NET interop. Do not create dynamic CLR classes for user schemas.
- Additional constraints must be a compatible refinement, e.g. a verdict string enum or a nested
  JSON field shape. Unknown keys, contradictory types, remote/file references and unsupported
  provider-schema constructs fail validation. Local definitions/references are bounded by existing
  authoring resource limits. The schema preview is generated, not a separately edited source of truth.
- Published schemas and constraints, schema dialect/engine profile and exact fingerprints enter the
  immutable artifact. Defaults/conversion happen through normal compiled input bindings before validation.
- Initial agent completion is `Done`; workflows branch on validated structured result members.
  Other declared emitted outcomes are refused until a real mapping contract is implemented. Model text
  cannot select arbitrary engine outcomes. Domain faults/cancellation retain their own native semantics.

Optional invocation controls are separate from business JSON: explicit conversation reference/expected
revision and caller-restrictable budgets. Their stable keys cannot collide with authored business
members. The provider derives any control members through the reviewed Agent-to-activity contract
projection path; they are not a second editable business interface. A control cannot widen published resource permissions or host budgets. Absent conversation
means fresh; a supplied conversation when continuity is disallowed is an actionable refusal.

## Mutable authoring

Agent-facing source APIs create/edit/fork exact revisions under Agent authority and permissions. Save conflicts preserve local work. Source-owned configuration/historical content is read-only; permitted copy/fork creates a new Agent source with explicit provenance. Agent commands compose the shared CAS/snapshot/history primitives; generic activity editing cannot create another writer. No legacy route translation or ownership migration is required.

Model selection references the operational connection/model and capability snapshot; unknown settings or changed support require diagnostics/revalidation, never silent stripping. Secrets stay in Connections/Secrets. Durable publication still requires durable connection metadata and secret persistence. Candidate tests and publication operate on a frozen exact source revision; no execution path reads a changing draft.

## Provider-filtered collection

The Agent collection queries Agent source/draft state under one authorized temporal snapshot, before counting/paging. Include unpublished source drafts. Source-owned configuration and release associations carry explicit authority/version identity; unrelated activity provider metadata cannot impersonate Agent ownership. Exact recommendation/head/draft choices, cursor expiry and tenant/global same-ID separation remain explicit. The workflow/BPMN picker continues to read the existing activity catalog and only admitted published projections. Historical generic provider-filter receipts retain their original scope and do not prove the new source read model.

## Test and publication

Playground test first commits/identifies the exact candidate snapshot and reuses the production
agent runtime with a nonpublished candidate artifact scope. Record draft ID/revision/hash and
connection/model/capability identity. No path reads a changing draft while the model is running.
The Agent test adapter reuses the existing candidate/runtime dispatch, idempotency/status/reconcile/cancel boundary;
the agent module adds conversation/usage/evaluation evidence rather than another execution loop.

Publish preflight validates public/schema/provider/model/resource/runtime/storage requirements, exact
dependencies and compatibility floor. Publication uses the accepted review token and idempotency key.
The provider/compiler returns material and diagnostics; it never persists drafts/templates/versions.
Missing consumer/storage/model capability cannot create partial publication. Prompt/settings/schema
changes affect behavioral hash; a prompt-only hash control holds identity/model/version fixed.

The snapshot/reservation/receipt protocol in [revision-management.md](revision-management.md#publication-and-failed-publication-recovery) applies to both authoring sources and the Workflow adapter. No Agent-specific candidate store or coordinator is introduced. Existing reviewed ordered publication commits retain their guarantees. Do not claim that all module
databases form one transaction. Replaying an accepted publish returns the same receipt. Changing
draft/head/reviewed content before the final source reservation invalidates an old review. After that reservation, later edits create another candidate and cannot change the admitted immutable operation. Explicit upgrade/copy/retirement cannot mutate
an existing workflow artifact. JSON constraint changes strengthen the generic compatibility floor;
an unclassifiable narrowing is conservatively breaking, not a silent patch.

Initial archive behavior is the existing exact-version Retired lifecycle and explicit recommendation
decision, labelled **Retire version**. Preserve the version/history and existing workflow bindings.
Discarding an Agent draft is a separate explicit action; generated activity content cannot be independently archived or edited to contradict its Agent owner. No new definition-wide archive flag, bulk hidden
retirement or deletion is implied. The collection shows actual lifecycle and exact history routes.

## Native/BPMN placement and runtime closure

Normal catalog discovery supplies the exact version and usable business/control ports. Sequence,
Flowchart and a BPMN bound child retain that identity on save/reload. The BPMN inspector edits the
ordinary bound activity; pure routing remains BPMN-owned. Native/BPMN use the same agent descriptor,
facade, journal and worker. Import/export support must retain exact bindings or fail explicitly.

Preflight and placement enforce the initial native value profile: sensitivity/redaction is supported
with `RequiresEncryption=false`; encryption-required input/result/capture and secret-reference business
bindings are refused before model dispatch. Late-bound policy is checked again at runtime. Encrypted
journal storage does not make native encrypted values recoverable. See [execution](execution.md).

Implement the BPMN Core/Design split in plan D7. Runtime-only native and BPMN packages must contain
zero Design/Publishing assemblies and perform zero authoring-store lookups. Authoring-only and combined
hosts retain structure projection, compilation and interchange registrations. Preserve schema/aliases,
and document source-package moves without pretending a runtime forwarder can safely reference Design.

## Required proofs

Concurrent save/publish, invalidated review, repeated publish, prompt/schema hash controls, within-deployment frozen-artifact
isolation, source/read-only forks, unpublished/mixed-provider/same-ID cross-scope discovery, stable cursor
counts/retention, public port/result mapping, picker refresh and exact native/BPMN persistence. Include
all four EF providers and independent authoring/runtime/combined package consumers. M0's direct
executable fixtures do not substitute for actual workflow publishing or Studio/interchange journeys.
