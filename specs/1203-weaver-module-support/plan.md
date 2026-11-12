# Implementation Plan: Weaver generic effects and module-owned operations

**Current naming and upgrade baseline (12 November 2026)**: the owner-authorized [#4027 rename](../../docs/reference/package-renames.md) moves the Studio assistant to `Elsa.Weaver.*` and `api.weaver`; user-defined agents keep `Elsa.Agent.*`. This is an identifier/input reconciliation, not a scope or acceptance waiver. The rename starts a fresh Weaver store. The additive upgrade in this work unit is post-rename, pre-effects Weaver1.0 → Weaver1.1, with genuine actual-writer evidence. Pre-rename Agent-store captures remain unchanged historical evidence and cannot qualify that upgrade. All legacy digest, protected-content, counter-lifetime, hold/finalization and no-replay requirements apply within the Weaver family. Contracts remain Preview until the required composed and independent-package journeys pass.

**Branch**: agent/codex/3443-weaver-effects-plan
**Date**: 2026-11-04
**Status**: Approved for task generation after independent plan review on 2026-11-04; proposed contracts remain Preview until demonstrated freeze
**Spec**: [spec.md](spec.md), approved by [PR #3757](https://github.com/elsa-workflows/elsa-foundation/pull/3757)
**Implementation base**: 9daaa3206cdd98dfb5978468ea354d711415352d

## Summary

Deliver leaf 0 through the existing Weaver lifecycle: current host privileges, an authenticated principal seam, truthful performed results, closed module codes, live contribution discovery and Weaver durable-format 1.1.0. Keep domain operations, guidance, validation, result payloads and application semantics in their owning modules.

This plan resolves PD1–PD3 for generic leaf 0 and the owner prerequisites for stories 2–4. Leaf 0 does not implement Secrets or Connections journeys. Their native sub-issues follow this plan's approval, and their implementations run sequentially after leaf 0. Later leaves retain FR025's separate specification step. All FR020 records and follow-ups remain required.

The proposed public surfaces remain Preview. Demonstrated composed-host and independently packaged journeys must precede a freeze. A plan or component test is not that evidence.

## Technical context

| Concern | Choice |
| --- | --- |
| Language/platform | C#/.NET 10; TypeScript/React; Python 3 conformance driver |
| Existing packages | Weaver Core, Weaver implementation/API/EF; Cluster Core/shared readability; Studio Hosting/SDK; owning Secrets/Connections Core, implementation and API |
| Storage | Existing in-memory owner; existing Weaver EF records on SQLite, SQL Server, PostgreSQL and MySQL |
| New production projects in leaf 0 | None; new common feature and EF variation stay in their existing packages |
| Tests | Existing Weaver, persistence/provider, Workbench, Studio unit/browser and complete package-conformance suites |
| Constraints | One session owner, no effect replay, no new schema fleet, no domain registry, no module-specific provider adapters |
| Scope | Generic leaf 0; proposed owner seams for leaves 1–3; SDK read seams for the built-in-screen satellite remain leaf 5 |

Use the repository build slot when present and /usr/local/share/dotnet/dotnet. Qualify native runs at low load with nice -n 10. Every host/process/output root belongs to its witness; port5095 is not available by assumption.

## Constitution check

Both ratified documents are loaded: framework6.1.0 and Elsa6.3.0. Their Gate indexes and the [review checklist](../../docs/contributing/review-checklist.md) apply before research and after contract design. Draft/provisional activity-model sections are not authority for this change.

| Gate | Application |
| --- | --- |
| Framework2.1/2.18 | Public Weaver contracts/models stay in Weaver Core; no EF or Studio implementation reference there. Owner contracts stay in their own Core and authorization composition in their own API. |
| 2.5/2.19 | Public nonsealed features, virtual ConfigureServices; distinct common/EF identities. Actual-type static metadata is validated. |
| 2.5.1 | Execution, principal lookup, policy/admission and module authorization are scoped. Immutable selection metadata/options monitoring may be singleton; no singleton captures a principal or module callback. |
| 2.6.1/2.6.2/2.11 | Existing named collections remain the discovery path. New replacement contracts diagnose duplicates; Secrets fan-in has one aggregator and independent sources. |
| 2.9/2.10 | Consumer DbContext support remains opt-in. Repositories keep distinct conditional commands and queries; application readback follows a confirmed command. |
| 2.21/2.23 | Preserve valid existing tests. Add behavior/barrier/registration tests for actual risks, not tests of documentation strings. Implementation stays internal unless an extension author needs it. |
| 2.22 | PublicAPI files, domain/root catalogs, glossary and module-author guidance change with their implementation. |
| Framework4.2, ElsaE5, ADR0080D4 | Preserve approved Major semantic classification and current computed pre-GA4.0 line; no project-authored versions, release or new magnitude engine. See research decisionR9. |
| ElsaE2.2/E2.5 | Design-only, Runtime-only and combined shapes remain supported; no required application DbContext base or cross-side dependency. |
| ElsaE6/E7 | New names follow domain rules. Reuse test projects; any justified new project must be assigned one real CI tier. |

No constitutional exception is planned. Independent correctness and standards round2 found no P1/P2 or material P3 findings on the complete corrected plan. Recheck this table before implementation; final-head review and delivery gates still apply.

## PD1: feature and package composition

1. Add WeaverEffectsFeature in Elsa.Weaver, feature identity WeaverEffects, inheriting WeaverFeature. It registers common behavior and immutable metadata of the actual feature instance. It declares no EF-family requirement.
2. Add WeaverEfEffectsFeature in the existing Weaver EF package. It inherits common behavior, has its own identity WeaverEfEffects, directly declares RequiresSchemaVersion Weaver1.1.0, and requires the existing Weaver EF store feature. It does not depend on the common sibling identity.
3. Select exactly one variation. Workbench/probe ordinary composition selects common; each durable overlay removes common and selects EF along with the existing store. Existing base/API/store features gain no static requirement.
4. Extend the existing owned record-store selection, retaining its two-argument binary surface, with an additive overload accepting the store's required effects feature Type. Every AddWeaverStores path supplies the EF effects Type; the private built-in memory marker supplies the common Type. An explicit legacy two-argument/null selection has undeclared effects support and remains legacy-only; selecting effects alongside it is a configuration error. Custom stores declare their supported variation through the new overload. Common code never references EF or infers a runtime store type.
5. Validate the final collection, not registration order: reject both siblings, duplicate selections, wrong-store selection and unowned replacement. Legacy-only Weaver/store compositions may omit effects; they cannot accept new behavior.
6. A specialized feature must directly repeat its required/base static declarations. Extra stricter declarations are allowed. Use SchemaVersionRequirement.DeclaredBy and exact declaration inclusion; do not create another semantic-version engine. This keeps actual-type catalog and execution checks aligned.

CShells0.0.31 rejects distinct classes with one feature name and resolves dependency identities exactly. Class inheritance does not substitute a dependency identity. Verify actual discovery/composition in tests.

Leaves 1 and 2 use Elsa.Weaver.Secrets / FoundationSecretsWeaverFeature and Elsa.Weaver.Connections / FoundationConnectionsWeaverFeature, each inheriting Weaver abstractions and requiring its owning API composition. Neither may auto-resolve the memory effects sibling. Owning Studio packages register their contributions through the SDK. Exact later-leaf features are decided in their own specifications.

## PD2: contracts and admission

[Privilege policy](contracts/privilege-policy.md) owns the exact generic policy/principal/feature surface. [Outcomes](contracts/outcomes.md) owns status, receipt and code contracts. [Owner operations](contracts/owner-operations.md) defines the first owner seams and revision mechanisms. [Data model](data-model.md) lists persisted and ephemeral fields.

### Policy and identity

Bind WeaverPrivilegePolicyOptions from Elsa:Weaver:Privileges using live host configuration. The three values default false; Workbench base configuration enables all three and Production does not override them. Bootstrap reports a read-only snapshot. Never use a session policy or startup-captured feature value as current authority.

Normalize a new preparation's class set, add Administrative for effective Admin risk, then freeze/hash it. Null/empty classes preserve the exact legacy digest shape. A legacy Admin preparation receives a live administrative veto and effective review label without payload or digest rewriting. New Admin preparations are new-format classed data; they are distinct from legacy records.

IWeaverPrincipalSource returns the genuine current authenticated principal for the exact actor/tenant, or null. Weaver API implements it; direct/non-HTTP hosts may replace it. No principal enters prepared arguments, records, audit, provider data or digests. Approval calls no principal source or module. Current permission checks remain owner API decisions.

### Selected-feature admission

WeaverEffectsAdmission checks the selected feature's static requirements through the shared checker. Empty requirements need no invented family observation. Missing/unfinalized EF observation refuses with schema-version-not-finalized; an absent effects selection refuses new behavior. No descriptor declares a schema version or selects a fleet.

Apply this admission at new-behavior entry/acceptance sites: tool discovery/preparation/execution, classed preparation creation, fresh and retained context disclosure, and both executor receipt boundaries. The descriptor only declares its optional behavior; the requirement always belongs to the selected feature. Legacy unclassed behavior remains available, subject to current authority and the independent legacy-Admin veto.

Catalog, Attention and capabilities continue using actual feature metadata and the same shared mechanism. No separate availability calculation or host-owned domain list is introduced.

### Live support inventory

Use the existing safe Studio module-registry endpoint, current loader state and actual SDK registrations. Project only bounded identities/display labels/status/support facts; exclude feeds, credentials, URLs, runtime paths and raw exception reasons. Preserve failed discoveries so an incomplete inventory cannot falsely prove absence.

Registration origin is distinct from resource ownership, including satellites. Filter disabled, failed, removed and obsolete-generation Weaver contributions before calling them, including nested context providers. An earlier partial registration cannot establish usable support. Select the smallest loader/registry ownership mechanism actual tests require.

Expose a bounded backend support projection from existing tool/context registries, with declared ModuleIds and current admission/authority limits. Manifest capability hints are not actual registrations. Keep Studio and selected-backend host/side/generation observations distinct. Refresh on prompt collection, not just session creation.

Browser inventory is client-asserted and never grants server authority. An unavailable/failed inventory means unknown, not an empty successful list. Identify a module through its registry identity/display name or module-owned selection metadata; never infer its identity or guidance from a route or the historical M0 list. An unresolved route contributes route-only context.

## PD2: Weaver durable format

Add Weaver1.1.0 to the existing family with an identity upcaster over protected content. Default new nullable members only after authenticated unprotect. Preserve purpose strings, canonical disclosure bytes and old approval digests. No relational migration is needed for these fields/enums.

Use chain readability/upcasting for every declared content row and readable-version checks for contentless snapshot, key, owner and counter records. Keep minimal plain owner/identity lookups before authorization; validate owned full rows/projections only after the exact owner filter. A foreign unreadable record retains shared403/404 and causes no hydration/settlement.

Choose the admitted WriteVersion inside each actual metadata attempt using the existing context's shared module gate. Do not stamp latest1.1 merely because the assembly supports it. Held1.0 permits only representable legacy content; new behavior refuses before effects. Unknown/unreadable state never falls back to1.0.

The session payload ExecuteUpdate path currently bypasses SaveChanges interception and does not stamp SchemaVersion. Keep the initial owner/fence-guarded raw row lock and version increment, load the tracked session after that lock, then save every content-bearing session/paused/pending-audit change and its admitted stamp with the children through the existing interceptor in the same metadata transaction. Preserve the existing conflict predicates and lease behavior; do not increment Version twice. Raw lease/fence/counter-only bookkeeping may retain its stamp. This needs no new shared raw-write guard API.

Use existing EfSchemaFinalizationGates.FindModuleGate(actualContextType).StateOf("Weaver") for EfSchemaFamilyWriteState.WriteVersion/WritesRefused. Keep the existing IEfSchemaFleet/ClusterSchemaFleet and EfSchemaFinalizationObservations/readability composition; no parallel fleet. Existing EfSchemaActivationRefusedException refuses an old host after unreadable finalization; EfSchemaWriteRefusedException/EfSchemaFamilyWritesRefusedException remain the write refusals. Record their exact reason and source, not a new availability algorithm.

On a verified forward gate-adoption mismatch, rollback and retry only the bounded metadata attempt through the existing retry seam, selecting a fresh stamp/entities. Never retry owner dispatch. Other schema failures/corruption are refusals, not retry guesses. Existing audit counter1.0 is never restamped and remains readable forever; new rows use that attempt's admitted stamp.

## PD3: non-atomic intervals

| Interval | Required bound and evidence |
| --- | --- |
| Live policy observation → dispatch | Recheck after awaited authority where possible; document the last read as admission, not a distributed revocation fence. Barrier before/after that read; no global I/O lock. |
| Schema stamp selection → content save | Shared authority remains final. Adoption barrier proves metadata-only retry and zero owner replay. |
| Permission/availability observation → owner call | Current principal/authority at preparation and explicit execute. No claim that external configuration remains frozen through I/O. |
| Reviewed owner revision → own write | Carry the exact expected identity/revision into the same owner load and atomic repository command. No preliminary comparison followed by a name-only reload. |
| Secret version check → store test | Pass reviewed identity/version to the store context; distinguish did-not-run from ran/failed. Document independent external/configuration mutation windows. |
| Rebind secret validation → connection commit | Separate owners; late validation is an observation, not serialization of secret rotation/deletion. |
| Rebind commit → cleanup | Confirmed binding plus failed cleanup is persisted with warning; never replay the binding. |
| Connection test → result save | Check resolved incarnation/version before external call. Failure after the call is uncertain, even if final metadata CAS loses. |
| Connection delete review → preflight → cleanup → row delete | Freeze exact cleanup identities/dispositions, recompute before effect and refuse an observed change; never expand the approved set. Complete bounded enumeration before effect. Final own-row conflict after cleanup is partial/uncertain; native3779 cleanup remains best-effort. |
| Paged enumeration → cleanup | Deterministic bounded pages expose truncation/incompleteness, not an immutable snapshot or universal cleanup guarantee. Exercise page shifts and post-enumeration creation. |
| Consumer comparison → Secret mutation | Bounded advisory observation; changed observed identities/page/count are stale. Barrier for a later binding proves residual window, warning and no unused-at-deletion claim. No global consumer hash or cross-owner coordinator is promised. |
| Secret value deletion → metadata CAS | Failure/conflict after possible value removal throws → uncertain/no replay. Own metadata CAS is not a value-store transaction. |
| Commit → readback/receipt retention | Read owner state; report later changes truthfully. Lost response/retention never causes a second domain invocation. Retain genuine accepted receipts after policy changes. |

The owner-operations contract assigns these intervals to leaves1–3 and defines their safe revision mechanisms. A composition unable to honor a prerequisite returns unsupported/owner-unavailable before effect; it never writes unconditionally.

## Ordered delivery and evidence

1. Review/approve this plan and proposed contracts. Add dated amendment pointers to1186 session-policy and1189 recovery without rewriting their historical qualification claims.
2. Create native children0–3 under3443, initially needs-triage; refresh dependencies, claims and readiness cap before delegated promotion. Generate/review leaf0 tasks before code. Keep one implementation leaf plus one integration/review lane.
3. Leaf0 Core/lifecycle: principal/policy/classes, exact legacy digest, performed, typed code normalization and both receipt boundaries. Preserve cancellation, duplicate delivery, accepted receipts and explicit recovered execution.
4. Leaf0 durable/composition: selected variations, all row-kind readers, per-attempt writes, held/adoption/unreadable proofs, permanent counter readability and direct consumer contexts.
5. Leaf0 Studio/discovery: current policy/classes/outcomes, accurate numeric status mapping (#3761), live safe inventory and nested removal. Preserve workflow atomic working-state edits, whole-batch undo and explicit save/publish.
6. Leaf0 docs/catalogs/records and FR020 follow-ups, with current overlap claims. Run affected complete suites and current full native/package qualifier. Source-bind every receipt and preserve failed/superseded evidence.
7. Fresh independent review until no P1/P2, exact-head hosted gates, normal merge and resulting-main verification. Freeze only the demonstrated generic contract.
8. Leaves1–3, then4–21 and M5, each through its scoped specification/plan/tasks and qualification path. No issue/board Done until its full acceptance is verified.

## Qualification gates

Use [quickstart.md](quickstart.md) for commands and evidence inputs. Required leaf0 proof includes:
- live off/on/bootstrap and three-class combinations; effective/raised/legacy Admin; module-free approval;
- missing/duplicated/mismatched real principal and owning authorization, current tenant and selected backend isolation;
- correct/wrong performed scope/status/descriptor, custom-executor controls and owner-observed ran/no-persistence evidence;
- closed-code/message canaries in every typed channel, owner provenance spoof controls, module-owned opaque allowlists and explicit undeclared compatibility;
- memory/EF absent/held/unobserved/finalized/wrong/both/direct-consumer compositions and catalog agreement;
- all old/new row kinds, corruption, unchanged canonical/digest bytes, old counter and metadata gate-adoption barriers;
- current inventory including optional/failed modules, partial registration, disabled nested source and unknown route without hardcoded modules;
- real composed Studio/backend plus complete immutable package witness on both actual adapters, retaining original/current-authority/autonomy/workflow/restart inventories.

Separate deterministic component, deterministic provider-protocol and live-provider evidence. No live success is implied by the protocol factory. No credentials are extracted or providers purchased.

## Source structure and complexity

Implementation stays in existing Weaver Core/implementation/API/EF and Studio Hosting/SDK projects, plus existing owning module projects for their prerequisites. New generic contracts, feature classes and the selection overload are indexed in their package PublicAPI.Unshipped and catalogs.

Documentation here: plan.md, research.md, data-model.md, quickstart.md and contracts/privilege-policy.md, outcomes.md, owner-operations.md. Tasks are generated only after this plan is reviewed. Glossary changes belong to leaf0 implementation under FR021.

No new production project, session owner, schema observer, persistence engine, domain registry, provider specialization or constitutional exception.
