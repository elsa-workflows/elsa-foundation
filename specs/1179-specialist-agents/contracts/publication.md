# Specialist authoring and publication contract

**Status**: Proposed behavior; implementation/public signatures require their independent consumer proof.
Owning requirements: FR-001–FR-009, FR-021, FR-023, FR-027 and Program A1–A5/A12–A13. Configuration-defined specialists ([plan D10](../plan.md#d10--configuration-defined-specialists)) are source-owned versions compiled through the same provider compiler and preflight.

## Resource and provider identity

Use existing activity definitions, drafts, versions, templates, review receipts, content authority and
lifecycle operations. Provider key `elsa.specialist`, initial manifest schema `1`, runtime consumer
`elsa.specialist`/`1` are proposed frozen identities. Display name Copywriter is presentation; renaming
it never changes type key, version or an existing binding. Teams receive a distinct provider/schema
when their real composition contract is implemented, rather than overloading prompt fields.

Definition/draft/version CRUD and publication use existing APIs/permissions. Specialist routes may
adapt terminology but cannot maintain a second catalog, trust caller-supplied authority or bypass
generic expected revision/head, review token, idempotency or recommendation checks.

## One public interface, derived JSON

- Public input/output member reference keys, types, nullability, defaults and required flags remain
  owned by the generic contract editor and persisted draft contract.
- Provider fields add instructions, model reference/settings, policy and optional JSON constraints
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
- Initial specialist completion is `Done`; workflows branch on validated structured result members.
  Other declared emitted outcomes are refused until a real mapping contract is implemented. Model text
  cannot select arbitrary engine outcomes. Domain faults/cancellation retain their own native semantics.

Optional invocation controls are separate from business JSON: explicit conversation reference/expected
revision and caller-restrictable budgets. Their stable keys cannot collide with authored business
members. The provider proposes any generated control members through the normal reviewed contract
proposal path. A control cannot widen published resource permissions or host budgets. Absent conversation
means fresh; a supplied conversation when continuity is disallowed is an actionable refusal.

## Mutable authoring

Create chooses this exact provider/schema and a host-supported initial public contract. Save uses the
exact revision and returns the accepted new revision. Invalid local text remains visible without
pretending it was saved. A conflict preserves local working content and identifies the server winner;
parallel recovery/fork follows the generic authoring protocol. Source-owned or historical content is
read-only; copy/fork creates new Design-owned content with explicit provenance.

Model selection references the operational connection, model ID and an identified capability snapshot.
Supported option names/ranges/defaults come from the provider, not a universal temperature form.
Stale/unknown support blocks test/publication with field diagnostics or an explicit revalidation action;
it never silently strips a setting. Credentials and secret material cannot be authored into a descriptor.
Durable publication requires a durable model-connection metadata path; disposable in-memory hosts are
labelled/configured as such and do not qualify restart support.

## Provider-filtered collection

At one resolved temporal snapshot S, include a definition if its matching head/recommendation belongs
to the provider, or an active read-visible matching draft belongs to that exact definition owner scope.
Both parent/draft validity intervals use S. The correlated query precedes integrity validation/count/page,
and existing cursor binding/expiry rules remain. Reuse existing temporal draft projections; no new
membership store. Two matching drafts still produce one definition row.

Resolve the row's resource explicitly: matching active recommendation first, otherwise matching head
with its actual lifecycle, otherwise exact matching active draft or an explicit choice among several.
If another provider owns the recommendation/head, disclose that fact and do not display its model as
specialist metadata. Existing all-provider counts are labelled as such or replaced by filtered totals.
Historical-only matching versions remain available through exact history links; they do not broaden
the initial collection membership rule. Snapshot retention expiration produces the existing expired
outcome, never silently refreshed totals. Tenant/global same-ID definitions cannot cross-correlate.

## Test and publication

Playground test first commits/identifies the exact candidate snapshot and reuses the production
specialist runtime with a nonpublished candidate artifact scope. Record draft ID/revision/hash and
connection/model/capability identity. No path reads a changing draft while the model is running.
The generic draft test-run idempotency/status/reconcile/cancel path remains the invocation authority;
the specialist module adds conversation/usage/evaluation evidence rather than another execution loop.

Publish preflight validates public/schema/provider/model/resource/runtime/storage requirements, exact
dependencies and compatibility floor. Publication uses the accepted review token and idempotency key.
The provider/compiler returns material and diagnostics; it never persists drafts/templates/versions.
Missing consumer/storage/model capability cannot create partial publication. Prompt/settings/schema
changes affect behavioral hash; a prompt-only hash control holds identity/model/version fixed.

Existing reviewed ordered publication commits retain their guarantees. Do not claim that all module
databases form one transaction. Replaying an accepted publish returns the same receipt. Changing
draft/head/reviewed content invalidates an old review. Explicit upgrade/copy/retirement cannot mutate
an existing workflow artifact. JSON constraint changes strengthen the generic compatibility floor;
an unclassifiable narrowing is conservatively breaking, not a silent patch.

Initial archive behavior is the existing exact-version Retired lifecycle and explicit recommendation
decision, labelled **Retire version**. Preserve the version/history and existing workflow bindings.
Discarding a draft is a separate explicit action; no new definition-wide archive flag, bulk hidden
retirement or deletion is implied. The collection shows actual lifecycle and exact history routes.

## Native/BPMN placement and runtime closure

Normal catalog discovery supplies the exact version and usable business/control ports. Sequence,
Flowchart and a BPMN bound child retain that identity on save/reload. The BPMN inspector edits the
ordinary bound activity; pure routing remains BPMN-owned. Native/BPMN use the same specialist descriptor,
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

Concurrent save/publish, invalidated review, repeated publish, prompt/schema hash controls, old-artifact
isolation, source/read-only forks, unpublished/mixed-provider/same-ID cross-scope discovery, stable cursor
counts/retention, public port/result mapping, picker refresh and exact native/BPMN persistence. Include
all four EF providers and independent authoring/runtime/combined package consumers. M0's direct
executable fixtures do not substitute for actual workflow publishing or Studio/interchange journeys.
