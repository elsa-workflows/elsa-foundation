# Tasks: Durable Messaging

**Input:** [spec](spec.md), [plan](plan.md), [data model](data-model.md), [contracts](contracts/delivery-design.md), [API](contracts/submission-api.md), [matrix](acceptance-matrix.md).
**State:** Delivery in progress; no runtime/provider/UX qualification yet. All selected capabilities are first-release scope.

Elsa declares no blanket greenfield test cadence; framework §2.21.2 leaves that choice to the derived application, and the gap is recorded here. Use risk-based behavioral tests and causal controls required by the current org review rules, without inventing a repository-wide TDD gate. Never substitute a zero-test filter, mock-only fixture or build for composed acceptance.

## Phase 1 — Setup and design

- [x] T001 Refresh rules, claim #3583, preserve isolation and record owner authorization in `docs/program-goals/durable-messaging.md` and ADR0080.
- [x] T002 Record owner Q1–Q4, refresh actual gaps and independently review requirements in `specs/1216-durable-messaging/spec.md`, `research.md` and `acceptance-matrix.md`.
- [ ] T003 Finalize/review concrete shared standalone-source and checkpoint participant signatures in `specs/1216-durable-messaging/contracts/runtime-mapping.md`; reconcile on #3904/#2892/#3820 before dependent shared ABI edits.

## Phase 2 — Foundations

T004 and the pure identity/submission SDK portions of T005 are independent of T003; they do not register a producer, persist a new format or change the shared source/actor ABI. All physical/delivery story work requires the corresponding reviewed source/capability mapping.

- [x] T004 [P] Implement internal immutable submission validation, versioned canonical fingerprint and fixed lifetime snapshot in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Messaging/MessageSubmissionRules.cs`; prove equality/conflict/limits/default changes and independent clocks in `Essentials/tests/Workflows/Runtime/Tests/MessageSubmissionRulesTests.cs`. Evidence: `evidence/t004/README.md`, 52 focused/3,403 complete Runtime passing; no producer enabled.
- [ ] T005 Define justified public domain/provider contracts under `Essentials/src/Workflows/Runtime/Core/Messaging/`, with scope/logical identity and exact capability/reservation/outcome versions; review `PublicAPI.Unshipped.txt` and test identity collision boundaries in Runtime tests.

T005 component progress: immutable identity/submission SDK and unregistered common source/preparation contracts are implemented; [retained evidence](evidence/t005/README.md) records 3,469 complete Runtime passes and causal controls. Exact Message capability/reservation/outcome participants, physical source ownership and registration remain open. The new contracts enable no producer or durable capability by themselves.
- [ ] T006 Implement material-only declared interest extraction and canonical native/direct BPMN provenance in Runtime `Messaging/` and `Essentials/src/Activities/Bpmn/`; test receive-only/no-bookmark roots, bounded dynamic names and native/BPMN model-neutral identity.
- [ ] T007 Extend the selected Runtime source/backend family, verified ingress context and existing `Services/Checkpoints/RuntimePostCommitOutboxProcessor.cs`; preserve old constructors/fingerprints/Event cohorts and test actual registration rollback/foreign replacement.

T006/T007 component progress: [canonical BPMN provenance](evidence/t006-provenance/README.md) and [dormant finalization preparation/refusal](evidence/t007-preparation/README.md) pass their affected projects and retained causal controls. Native/model interest lifecycle, full physical source/participant adoption and actual registration remain open. Concrete C2 r3 reconciliation now preserves its canonical ingress row and the actual native outbox; shared work resolves exact family/source addresses without a required generic-source FK or header copy.

T006 material mechanics progress: [unregistered extraction and opaque identity compatibility](evidence/t006-interest/README.md) pass 97 focused cases, one BPMN compatibility case and the complete 3,540-case Runtime project. Missing/ambiguous/wrong-version selectors have retained causal failures. Server identities and receiver addresses preserve opaque UTF-16; client names/keys/IDs retain strict UTF-8. No domain provider or interest lifecycle is registered by this component.

Native T006/T027 preparation: hidden `ReceiveMessage` data/authoring contracts and an unregistered Primitives-owned whole-native provider now read finite declarations from hash-covered bindings across every node. Ordinary invocation/resume refuses before effects. The [retained Azure originals](evidence/t027-native/README.md) at `0246b4ecf` record 31 focused and 769 complete Activities Runtime passes, zero failures/skips; mutation gives 23 expected assertion failures and exact restoration passes 31 again. Full archive/hash/counters/cleanup/source reconciliation passed root inspection and independent component audit. Source review corrected a fixture constructor and separated name/start/declaration hash controls. No wait, start, lifecycle or production Message capability is enabled.
- [ ] T008 Add typed optional Message checkpoint participation to Core `Models/RuntimeCheckpointCommit.cs`, validator/fingerprint, physical stores and coalescing forwarding; prove null old fingerprints, actual store-tuple refusal and physical boundaries.
- [ ] T009 Add Runtime message/source entities/staging/schema family and all four migrations in `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/`; coordinate in-memory state in its existing checkpoint write gate and run provider migration/atomic rollback tests.

T007/T009 component: the existing processor forwards typed preparation to atomic claim completion only after checking the selected full-store tuple and both source/completion capabilities. [Retained Azure originals](evidence/t007-forwarding-r2/README.md) establish 55 focused cases, an exact dropped-finalization causal failure, eight restored forwarding cases, 16 SQLite discovery cases and 3,595 complete Runtime passes. Exact retained discovery context filters actual source/work SQL before pagination. The internal two-table EF metadata separates immutable header revision from mutable provider/work CAS; four additive migration pairs and snapshots pass rebuilt offline model parity. The complete EF run timed out without a result and remains FAILED/INCOMPLETE. Full provider transactions/upgrade preservation, source writer, adapter registration and full physical participation remain open. Later local header/read/claim drafts are outside this qualified source.

T003 discovery preparation: the preserved claim constructor now means unspecified/unsupported, while an additive partition captures exact tenant/run/execution/test/authority facts. Full raw context comparison accompanies the existing coarse authority hash; no caller DTO grants authority. Forty-seven structural/JSON cases remain uncompiled/unexecuted. A local EF-only lossless full-context projection, SQL predicates before `Take`, narrower indexes and 16 proposed SQLite/projection cases are also uncompiled/unexecuted. The default claim JSON reader's existing collection-type limitation is retained explicitly; narrow test metadata isolation does not establish a host serializer policy. Canonical family joins, claim liveness, retention, supported version filtering and physical capability qualification remain open.

## Phase 3 — US1 keep early messages

**Independent proof:** real API acceptance before a receive-only wait, restart, matching late receive; effective default/override/immediate-only outcomes.

- [ ] T010 [US1] Maintain publication/active-material interests and epochs in `Services/Executables/WorkflowActivationCoordinator.cs`, activation switches and initial/terminal native/model checkpoints; qualify B01/B27/B28 without Design reads.
- [ ] T011 [US1] Implement complete-cohort/capacity/source acceptance transactions and standalone source claim/reconciliation in Runtime `Messaging/` and EF staging; qualify B02/B18/B20/B26, including commit uncertainty and no clipping/residual reservations.
- [ ] T012 [US1] Implement capability-gated submission/receipt/policy endpoints under `Essentials/src/Workflows/Runtime/Api/Messaging/`; wire current identity/resource authorization and metadata-only receipts.
- [ ] T013 [US1] Add owned real-HTTP early/default/override/immediate-only/restart journeys under `e2e-tests/messages/` and corresponding EF/API behavioral tests; retain actual B01/B02/B20/B26/B27 results.

## Phase 4 — US2 one recipient within each definition

**Independent proof:** two definitions receive independently while workers/versions/instances compete; retries keep exact winner and Event broadcast remains unchanged.

- [ ] T014 [US2] Implement per-definition winner selection plus exclusive one-shot wait reservation in Runtime `Messaging/`/EF provider transactions; two-message losers stay unselected under the correct buffer mode.
- [ ] T015 [US2] Extend exact native resume/model input admission and consumption guards/checkpoints with trusted Message capability/reservation epochs; preserve model receipt ownership and qualify B03/B08/B09/B19/B21/B25.
- [ ] T016 [US2] Implement immutable partial-fanout progress, closed failure/abandonment proof and stale-command fencing in Runtime `Messaging/`; test selected incompatible wait, delayed old resume and closed reservation reuse.
- [ ] T017 [US2] Extend source/model/consumer composed journeys for independent definitions and competing waits in `e2e-tests/messages/`; run existing Event correlation/broadcast objectives unchanged and retain B08–B10/B13–B16 evidence.

## Phase 5 — US3 start keyed conversations

**Independent proof:** two workers/versions admit one active message-started run per key, independent lanes proceed, terminal settlement releases a pending successor and stale work cannot undo it.

- [ ] T018 [US3] Implement Message-specific saved-start authority/idempotent target and transactional key admission under Runtime `Messaging/` and existing `Services/Executions/WorkflowStartDispatcher.cs`, preserving frozen old identity derivations.
- [ ] T019 [US3] Integrate native terminal/model-settled release and retained refusal/retirement proofs into owning checkpoints; test pre-header death, terminal deletion, stale release and delayed old start in EF/provider tests.
- [ ] T020 [US3] Implement published Message start and native/direct BPMN start/catch adapters in `Essentials/src/Activities/Primitives/` and `Essentials/src/Activities/Bpmn/`, exposing exact envelope key/name with payload/receipt and preserving signals/Events.
- [ ] T021 [US3] Execute actual two-worker/version/process-death admission/release journeys in `e2e-tests/messages/` and provider projects for B04–B07/B22–B24/B28; retain passed/failed/not-run modes explicitly.

## Phase 6 — US4 retry submissions safely

**Independent proof:** same ID reconciles original before/after delivery/completion; content conflict and independent window cleanup never reset outstanding work.

- [ ] T022 [US4] Implement producer-ID address generation, acceptance-time policy/fingerprint comparison and independent expiry in Runtime `Messaging/`/EF staging; retain original receipt and pending definitions through reuse/cleanup.
- [ ] T023 [US4] Implement stable canonical-user and configured authenticated-application producer resolvers under `Api/Messaging/`, with current permission/mapping rechecks and no arbitrary app claim/namespace trust.
- [ ] T024 [US4] Qualify duplicate/content/expiry/retention/quota behavior through real EF/API/provider journeys for B11/B12/B18/B26/B29, including credential rotation/revocation and cross-app/tenant isolation.

## Phase 7 — US5 author and operate Messages

**Independent proof:** real catalog authoring/publication and API/activity sends lead to receipt and per-definition operator diagnosis/recovery with protected payloads.

- [ ] T025 [US5] Implement SendMessage invocation staging/private receipt wait and actual committed source handler in `Essentials/src/Activities/Primitives/` plus `Services/WorkHandlers/WorkflowInvokeActivitySchedulerWorkHandler.cs`; commit intent/wait together, reset transient staging on faults and preserve physical source identity.
- [ ] T026 [US5] Implement create-only Accepted/Rejected/Cancelled native SourceOutcome and exact guarded sender continuation in Runtime `Messaging/`; fault permanent rejection without fictitious receipt, serialize cancellation with acceptance through the retained sender control in their actual checkpoint/completion transactions, retain callback/retirement proof, and qualify B30/B32 with no ID/beyond dedup expiry and both cancellation race orders.
- [ ] T027 [US5] Implement ReceiveMessage wait/start authoring/catalog metadata, declared names and exact typed output under `Activities/Primitives/`; test persisted authoring, key binding and native/direct-model result mapping.
- [ ] T028 [US5] Implement scoped list/detail/per-definition/payload/retry/abandon endpoints and permissions in `Api/Messaging/`; open Selected attempts alone offer Retry, closed terminal failures remain closed; test stale operations/unknown outcomes/access in API tests.
- [ ] T029 [US5] Add domain-owned `Studio/src/essentials/Elsa.Studio.Messages/` contribution using current module packaging, capability links and shared collection/detail UI; retain backend/tenant/focus/page state appropriately and clear protected values on scope changes.
- [ ] T030 [US5] Add receipt links to send/receive run evidence through public `workflowRunRelatedLinks` contributions; never decode Message-private state in the Workflows module.
- [ ] T031 [US5] Add real API-seeded Message authoring/operator journeys to `Studio/tests/acceptance/` and the existing owned runner, including U01–U07, legal transient recovery/permanent diagnosis and direct restricted-user API checks.
- [ ] T032 [US5] Implement/run `tools/messages/Consumer/Consumer.csproj` and configuration/examples against public packages; update `quickstart.md` with actual successful commands and retained immutable consumer inputs, not project-internal substitutes.

## Phase 8 — Cross-cutting qualification and closeout

- [ ] T033 Implement bounded pending/retry/release/cleanup contributions through existing `Services/Recovery/RuntimeResumptionService.cs`/`Resumption/RuntimeResumptionPumpTask.cs`; prove lost-wakeup recovery and fairness with no new hosted pump.
- [ ] T034 Complete source/material/payload/test-scope retention and independent audit/dedup cleanup, including retained SourceOutcome/receipt capacity, in Runtime/EF/garbage-collection ownership and provider tests.
- [ ] T035 Execute all B01–B32 under native/direct BPMN and every advertised SQLite/PostgreSQL/SQL Server/MySQL and two-worker server-provider mode; retain source/schema/topology/actual outcome in `specs/1216-durable-messaging/evidence/`.
- [ ] T036 Run whole affected backend/Studio projects, applicable architecture/registration/API/migration/maps checks and relevant backend source e2e on rebuilt owned hosts; update actual `EXTENSION_POINTS.md`, public API manifests and configuration documentation.
- [ ] T037 Run composed Studio on the committed candidate with downloadable current-source screenshots/traces; obtain independent acceptance reviewer inspection and fix/retest U01–U07 until UX is passed.
- [ ] T038 Complete independent correctness/standards/acceptance review of full current #3904 diff, fix findings in new commits and satisfy exact-head required checks under current org/repo merge rules.
- [ ] T039 Merge through the applicable authorized path, qualify resulting main and reconcile #3583/program/spec lifecycle/configuration/examples to actual evidence; close only after every agreed success criterion passes.

## Dependencies and execution

T001/T002 → T003 and independent T004. T003/T004 → T005–T009. US1 → core US2/US3; US4 builds on acceptance but its identity/fingerprint tests start with T004. US5 authoring/operations require the corresponding acceptance/delivery APIs. T033/T034 are integrated alongside their owning story before that story is qualified, then T035–T039 close the full product.

Parallel examples inside the one active leaf: T004 rules versus source mapping; native versus model pure extraction after shared identity contracts; API access tests versus EF transaction implementation after stable contracts; UI component work versus backend failure tests after wire contracts. Never edit the same shared contract concurrently. Source/schema/ABI mutations, migrations, builds, approvals and adaptive follow-ups remain ordered.

There is no reduced release/MVP substitution: each independent journey is an integration checkpoint, and starts/waits, native/BPMN, activities/API, operator Studio and adverse-path/provider evidence all remain required. All runtime qualification currently remains NOT RUN.
