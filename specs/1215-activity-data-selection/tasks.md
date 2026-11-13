# Tasks: Activity Data Selection

**Input:** [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[binding](contracts/binding-contract.md) and [authoring](contracts/authoring-contract.md) contracts.
**Status:** Approved M2 task plan. M1 research and M2 closeout are complete; #3922 has begun the shared
foundation implementation. Checked setup items have receipts below. Source changes alone do not complete
test-bearing tasks or accept product journeys. No pending CI is a pass.

Use existing projects and separate worker worktrees. Paths below are repository-relative; named new files
are proposed locations within the plan's fixed package ownership. Coordinate shared files serially.
`[P]` means separate files allow parallel preparation after the named prerequisites, not permission to
skip dependencies or run competing composed builds. Every task carries its owning issue/PR evidence.

Tests follow ADR 0080 D1 and framework §2.23: meaningful risk-based behavior and composition proof in the
same delivery slice. This extends existing features rather than creating a greenfield application.
Elsa declares no test-cadence gate; no failing-first/TDD requirement is inferred. New service logic needs
behavior tests; logic-bearing registration needs registration proof, and plain registration uses the
existing all-feature composition test. No tests assert these documents' text.

## Phase 1: Setup and gates

- [x] T001 Revalidate M1 closeout, the exact prerequisite heads and open main-red trackers; record the authorized production start and current dependency graph on #3882/#3885 using `docs/contributing/merging.md` and `docs/program-goals/activity-data-selection.md`. Receipt: [M2 qualification/closeout](https://github.com/elsa-workflows/elsa-foundation/issues/3885#issuecomment-6676152380), qualified f6; fresh tracker/claim checks clear. Newer fd0 integration is not qualified by that receipt.
- [x] T002 Review and adopt the M2 spec→plan→tasks sequence; create only the next executable native children/dependencies under #3882, reuse #3094 unchanged in scope, and record worker/file ownership in `specs/1215-activity-data-selection/tasks.md`. Native child [#3922](https://github.com/elsa-workflows/elsa-foundation/issues/3922), closed blocker #3885, winning [claim6676170172](https://github.com/elsa-workflows/elsa-foundation/issues/3922#issuecomment-6676170172); no Ready promotion/assignment.
- [ ] T003 Establish the finite criterion/evidence ledger from `specs/1215-activity-data-selection/quickstart.md`, including all M1 failures, exact host/head ownership and nine journey families; allocate disposable owned stores/ports before any product proof.

**Checkpoint:** M1 and M2 merge/resulting-main receipts exist; workers have concrete claims and boundaries.

Current #3922 ownership: one implementation worker owns the integrating Foundation leaf; the coordinator
sequences qualification and review. Shared Core contracts precede dependent code. The bounded owned
Azure development run is closed; see its [build and retained-failure receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3948#issuecomment-6728243018).
Approved local qualification runs owning whole projects sequentially after fresh capacity checks, using
two processors, preserving the build-slot wrapper when present and unchanged stock retry/hang guards;
see the [source-bound qualification receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3948#issuecomment-6728799519).
T003 remains open until its owned proof-host/store allocation obligations are met. T004–T010, M1 journey
failures and downstream acceptance remain open; protected CI, formal review and resulting-main
qualification remain required.

## Phase 2: Shared analysis and static admission

**Goal:** Discovery, diagnostics and executable admission share authoritative source facts, independent
of optional expression languages. This phase blocks production story integration.

- [ ] T004 [P] Extract the unchanged activity-value profile into a thin definition in `Essentials/src/Activities/Runtime/Core/` and retain existing completion/hydration behavior in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityValueJson.cs`; add only serialized-shape fact contracts required in `Essentials/src/Activities/Design/Core/` and track public API changes.
- [ ] T005 Implement effective serialized names, ignore/conditional-omission, type/cardinality and honest nullable/presence/unknown bounds in `Essentials/src/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs` and its owning metadata service; preserve MetadataLoadContext isolation and add owning `Essentials/tests/Activities/Design/` metadata/profile parity controls (depends T004).
- [ ] T006 [P] Define the shared source-analysis port/facts in `Essentials/src/Workflows/Design/Validations/Core/` and minimally extend `Essentials/src/Workflows/Design/Core/Contracts/IActivityStructureHandler.cs`/structure service for availability reasons; implement Sequence/Flowchart-owned entry/order/join/backedge facts in `Essentials/src/Activities/Flowchart/Internal/FlowchartStructureHandler.cs` and the existing Sequence structure handler.
- [ ] T007 Implement the scoped shared analyzer in `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/` and refactor `Validators/ValueFlowValidator.cs` to consume it; retain all legal Flowchart entries, projected Switch inputs, scope/optional-result rules and separate concurrent-write logic (depends T005–T006).
- [ ] T008 Add the independent language-neutral capability and authorized persisted/current-snapshot endpoint in `Essentials/src/Workflows/Design/Api/`; resolve untouched inputs, exact resource/base/hash/target and provider/permission revisions, bound metadata/no-store responses, and translate safe failures without browsing persistence writes (depends T007).
- [ ] T009 Require WorkflowDesignValidations in `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/WorkflowsPublishingFeature.cs` and enforce shared direct/portable static admission in `Handlers/PublishWorkflowRequestHandler.cs`, `Services/WorkflowExecutableCompiler.cs`, `Services/ActivityTemplateCompiler.cs` and `Essentials/src/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs`; cover all executable-producing paths before artifact/publication/activation writes (depends T007).
- [ ] T010 Prove analysis/composition/admission behavior in `Essentials/tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`, `Essentials/tests/Workflows/Design/Api/Tests/` and `Essentials/tests/Workflows/Publishing/Api/Tests/`: untouched/current unsaved targets, denied/stale/unknown facts, all entries/joins/backedges/Switch, preserved compensation-handler own/prior forward-result visibility, direct/portable missing producers and no-write rejection; include endpoint-free Publishing and Design-only/combined resolution (depends T008–T009).

**Checkpoint:** Real authorized API and headless publication agree; no optional validation fallback remains.
Update owning extension-point catalogs with the new analysis port in the same delivery slice.

#3922 / PR #3948 foundation-leaf receipt (this branch; not task completion): source on
`agent/codex/3922-data-selection-analysis` implements T004–T010 shared analysis/static admission
and CLR whole-`$result` metadata, including Evaluate Decision's typed `DecisionEvaluation` result
(`status`/`result`/`trace`, Result as `activity.shape.dynamic-json`, outcomes Done/NoMatch/Refused,
automatic producer still `binding: null` / `RuntimeScopeBindingUnavailable`). Tasks stay unchecked
until owned proof-host qualification and resulting-main. This leaf does not add runtime member
binding (T011–T017) or Studio chooser/inspector work (T018–T026).

#4053 receipt (not task completion): the analyzer returns the complete scope-free `ActivityResult` binding
wherever publication admits it unchanged, and every first-party structure handler states
`EarlierProducerRunCanReachConsumer`. Rule: [binding contract](contracts/binding-contract.md#1-authored-wire);
reason codes: [authoring contract](contracts/authoring-contract.md#source-identity-members-and-compatibility);
design and evidence: [research](research.md#activity-result-bindings-without-a-scope); delivered in PR #4055.

#4059 receipt (not task completion): admission of a scope-free binding now also requires every structure above
the one holding producer and consumer to state the fact `false` for its child holding both, so a Sequence on a
BPMN gateway loop, in an unbuildable Flowchart or in a third-party structure is no longer offered or admitted.
Evidence: analyzer, BPMN compiler and publish-handler no-write rows; a real BPMN loop run showing the Ambiguous
read; per-handler answers for a child with itself; one offer → publication compiler → run round trip. Scoped
bindings and portable parameters are unchanged (#4056).

## Phase 3: User Story 1 — Choose earlier data at an input (P1)

**Independent test:** Quickstart journeys 1, 7 and 8: untouched pre-run Sequence/Flowchart inputs choose
whole Customer and nested ID, save/Code Apply/reopen/publish/run with zero synthetic variables/captures/
handwritten source, including a real minimal host. Unknown literals and source families use the same path.

- [ ] T011 [P] [US1] Add immutable `RuntimeReadSelection` and appended member sources in `Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBinding.cs` and owning model files; require exactly one role payload plus valid nonempty selection, reject paths on old kinds, preserve enum ordinals/constructor signatures and omit absent selection from old serialization.
- [ ] T012 [US1] Implement new authored member kinds and server validation in `Essentials/src/Workflows/Design/Core/` and `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs`; preserve exact literal segments, explicit scoped identities and reusable InputMember lowering in `Services/ActivityTemplatePlacer.cs` (depends T011).
- [ ] T013 [US1] Pin effective selected type/presence/nullability through root and every prefix before conversion; update the existing conversion linker, `Services/WorkflowExecutableHasher.cs` and `Essentials/src/Workflows/Publishing/Core/Services/ExecutableActivityTemplateBehaviorHasher.cs` for all behavioral selection fields and unchanged whole-value hashes (depends T012).
- [ ] T014 [US1] Implement bounded exact-member selection after existing role/causal/projection resolution in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Resolvers/RuntimeInputBindingResolver.cs`; retain missing-producer/absent-member/null provenance and source protection without nested write semantics (depends T011–T013).
- [ ] T015 [US1] Integrate inline/external selection and admitted target fit before snapshot/consumer construction in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs` and `Services/Values/WorkflowIntrinsicExecutor.cs`; reuse bounded alias/fit logic with the unchanged profile, reject unsupported opaque targets and preserve destination externalization/conversion (depends T014).
- [ ] T016 [US1] Advertise schema-1 member-selection support and propagate template/runtime requirements; reuse `IRuntimeRequirementChecker` before execution in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/WorkflowStartDispatcher.cs`, `Services/Bookmarks/BookmarkResumeDispatcher.cs`, pinned queued/start work and `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs` (depends T015).
- [ ] T017 [US1] Add focused role/model/compiler/hash/fit/admission controls in `Essentials/tests/Workflows/Publishing/Api/Tests/RoleOwnedInputBindingContractTests.cs`, `ActivityTemplatePlacementTests.cs` and `Essentials/tests/Workflows/Runtime/Tests/`; prove old named/numeric readers refuse new kinds, old bytes/hashes remain, nullable/optional prefixes compose, invalid deferred data reaches neither consumer constructor nor committed snapshot, and local/resume/recovery requirements fail closed (depends T016).
#4078 slice A receipt (not task completion; checkboxes stay open): the member-read model only, inert. T011: the three
appended member sources with fixed names and numbers (`WorkflowRequestMember = 6`, `VariableMember = 7`,
`ActivityResultMember = 8`), `RuntimeReadSelection`, `RuntimeInputBinding.Selection` (left out when null), a new
constructor overload beside the unchanged one, and the invariants and path bounds. T013, hashing part only: framed
member arms in the node-payload hash with a throwing default, selection covered by the behavioural, template and
input-snapshot fingerprints, and whole-value bytes and hashes pinned unchanged against the commit before. T016,
derivation part only: executables and templates holding a member read derive `elsa.runtime.input-member-selection@1`;
nothing advertises it. T017, model part only: invariant, serialization, hash, requirement and old-reader stand-in
controls, and refusal of a hand-built member executable at its reader on start, resume and queued work. Not done:
pinning facts at publication (T013), selection at run time (T014, T015), the capability and local guards (T016), the
compiler, admission and placement controls (T017). Design and evidence: [research](research.md#member-bindings-and-runtime-selection-4078-design-note).

#4078 slice B receipt (not task completion; checkboxes stay open): runtime selection, still unreachable for authors
because nothing publishes a member binding. T014: an exact-segment selector run after the unchanged whole-value
resolution of the same role, with missing producer, absent member and null kept distinct. T015: inline and external
selection in the input materializer and the intrinsic executor, an always-on fit check before the snapshot, the
selected value recorded in the snapshot, and `VF-SEL-001`..`004` through one exception type with the code in the
poisoned item's metadata. T016, capability and guards: `elsa.runtime.input-member-selection@1` advertised only while
the built-in materializer is composed (decided from its service registration, so a factory, instance or decorator
counts as replaced), and refusal through `IRuntimeRequirementChecker` at activity start, bookmark resume and a
structural parent's re-materialization on child completion. T017, runtime part: selector table, materializer and intrinsic rows inline and external, real engine runs for
all three roles in a Sequence and a top-level Flowchart, failure codes, occurrence parity, re-materialization after
resume, protection and guard controls, and a pinned member fingerprint. Not done: pinning facts at publication and
the compiler, analyzer, placement and linker work (slice C, T012/T013), the other families' authoring (slice D), and
a real engine run with externally stored values (exercised at the materializer and intrinsic level only).

#4078 slice C receipt (not task completion; checkboxes stay open): `ActivityResultMember` publishes, behind the
`WorkflowDesignValidations` setting `MemberReads`, off by default; while it is off nothing is offered or admitted and
every executable-producing path refuses before any write, as before. T012, result member reads: the authored parse
(1–64 string segments, at most 4 096 characters, no scope, findings `ActivityData/InvalidSource` and
`ActivityData/InvalidMemberPath`), analyzer offer and admission by the same decision as the scope-free whole read
(same reason codes, owning restrictions that admit `ActivityResult` admit it), static refusal of an undeclared member
(`ActivityData/UnknownMember`) and of a member looked up in a value without members
(`ActivityData/MemberParentNotObject`), a compiler branch emitting a deferred selection, and the compiler's own
refusal of a member path on a whole-value kind. T013, result member reads: a pin pass after the analysis in the
workflow compiler upgrades typed reads in the workflow's own body to known, the linker plans conversion from the
selected type and refuses an explicit conversion on a deferred read, and placement remaps the producer and keeps the
selection. A read inside a placed reusable body is published deferred and checked at run time (research §4). T017,
result member reads: analyzer, compiler, pin, linker and remapper rows, publication through the real publish handler
with the setting on and off, a reusable body compiled, placed and published (deferred, producer remapped, explicit
conversion refused), and real publish → run in a Sequence and a top-level Flowchart, typed and deferred. Not done:
pinning reads inside a placed body (follow-up), a run of a placed body's member read, the other families (slice D),
and Studio.

#4078 slice D1 receipt (not task completion; checkboxes stay open): `VariableMember` publishes under the same
`MemberReads` setting. T012, variable member reads: the authored parse (explicit `declaringScopeId` required; `path`
refused; the one shared member-path reading, `ActivityDataMemberPath` in Validations Core, used by the analyzer and
the binding compiler), analyzer offer and admission exactly as the whole `Variable` read (same
`Variable/UnresolvableReference` code; owning restrictions that admit `Variable` admit it), and a compiler branch that
emits a deferred selection and refuses an explicit conversion (`SelectedMemberContractUnknown`). T013, variable
member reads: always deferred, no pin; the linker and the intrinsic source-plan step pass it through; placement
remaps its scope as for `VariableRead`. Also: a result member read the whole-read rules refuse is no longer typed,
so it reports one finding. T017, variable member reads: analyzer, compiler, linker, remapper and bounds rows, publish
with the setting on and off, a reusable body's read placed with its scope remapped, a CLR-seeded variable read by its
stored PascalCase name and absent by its camelCase name (`VF-SEL-001`), and real publish → run in a Sequence and a
top-level Flowchart. Not done: a run of a placed body's variable member read, request and input member reads (D2).

#4078 slice D2 receipt (not task completion; checkboxes stay open): `WorkflowRequestMember` and `InputMember`
publish under the same `MemberReads` setting; every member role is now parsed, admitted and compiled, and with the
setting off every one is `ActivityData/UnsupportedMemberBinding`. T012: `WorkflowRequestMember` (`memberKey`, a
legacy `path` refused) admitted exactly as `WorkflowRequest`, open requests included; `InputMember` (`referenceKey`)
admitted exactly as `Input`; both offered for a focused query with a member path (a workflow offers
`WorkflowRequestMember` by the input's name, a reusable body `InputMember`); an explicit conversion on any member
read publication cannot type is a design-time finding (`ActivityData/SelectedMemberContractUnknown`); role names
(`ActivityDataReadRoles`) and the declared-input lookup (`DeclaredInputIndex`) are shared by the analyzer and
publication. T013: `InputReadLowering` lowers `InputMember` to `WorkflowRequestMember` with the member path unchanged,
by the input's name at workflow level and by the boundary input's reference key in a placed body; the request member
read is always deferred, refuses an explicit conversion in the compiler, and passes the linker, the intrinsic
source-plan step and the remapper unchanged. Code-first `WorkflowRequestMember<T>` is renamed `WorkflowRequestRead<T>`
(decision 12). T017: analyzer, compiler, lowering, remapper and linker rows; publish with the setting on and off; an
input member read in a placed reusable body published as the boundary's request member read; a CLR-seeded request
member read by its stored name and absent by its camelCase name (`VF-SEL-001`); and real publish → run of request and
input member reads in a Sequence and a top-level Flowchart, declared and open requests. Not done: a run of a placed
body's member read (the Behavioral project has no template-publication fixture), pinning reads inside placed bodies
(follow-up), Studio authoring (T018–T022).

- [ ] T018 [P] [US1] Add the language-neutral source client in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/` using existing capability discovery; integrate exact acknowledged autosave/current-snapshot acquisition with `workflow-editor/useWorkflowPersistence.ts` and `ActivityDefinitionDraftEditor.tsx`, discard late identities and preserve manual/held edits (depends T008).
- [ ] T019 [P] [US1] Repair structured whole/result/member preservation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityInputWire.ts` and add canonical save/Code Apply/reopen controls in `__tests__/activityInputWire.test.ts`; preserve unknown unsupported source definitions without JSON stringification.
- [ ] T020 [US1] Build the shared Choose data/browser/reference contribution in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/` with existing Studio controls, stable authorized identities, readable provenance/navigation/replace/clear and keyboard/cancel/focus behavior; capability-gate member authoring while allowing #3094's qualified whole-output slice (depends T018–T019).
- [ ] T021 [US1] Integrate the chooser at untouched eligible ordinary inputs through `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx` and `module.tsx`, retaining the existing document/history owner, fixed-value/explicit-expression modes and secret/provider/masked/read-only constraints (depends T020).
- [ ] T022 [US1] Complete whole/nested selection for activity results, workflow inputs, scoped/shadowed variables and owner-exposed current items in the shared contribution; support literal unknown/truncated object/map segments, effective path compatibility, collections/sets/maps and clear known-incompatible reasons in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/` (depends T013, T021).
- [ ] T023 [US1] Add behavior tests beside `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityInputOptionsUi.test.tsx` and `workflowInputPersistence.test.ts` for untouched selection, structured wire, one-edit history, literal escaping, stale/canceled/revoked requests, no hidden save, loading/empty/error/Retry and preserved protected values (depends T022).
- [ ] T024 [US1] Run focused owning backend/Studio suites and a real catalog→save→publish→execute source-family/known/deferred/type-fit proof using the existing lifecycle in `e2e-tests/`; retain invalid-publication no-write and consumer-input/producer-completion evidence (depends T017, T023).
- [ ] T025 [US1] Extend `Studio/tests/acceptance/journeys.spec.ts` and `tools/ux-acceptance/run.py` capture inventory for basic Sequence/Flowchart/current-draft selection; run normal/minimal owned hosts and retain canonical wire, real committed inputs and independently inspected exact-head captures under `specs/1215-activity-data-selection/quickstart.md` (depends T024).

#3094 whole-output receipt (not task completion): Studio offers Choose data on eligible ordinary inputs of
workflow drafts and stores the server's `ActivityResult` binding unchanged as one edit (T019 wire; whole-output
parts of T018, T020, T021, T023). The source client covers both advertised relations; only the workflow editor
uses it, so reusable-graph (T041), intrinsic/Switch (T042) and BPMN (T043) surfaces offer nothing yet. Member
selection is not offered. Whole-output subset of T024–T025: the owned-host journey "choose data for activity
outputs" in `Studio/tests/acceptance/journeys.spec.ts` and the runner's capture inventory cover Sequence and
top-level Flowchart choice, reopen, publish and a run checking the consumer's committed input, a loop producer's
shared reason with nothing stored, and keyboard choice with Replace and Clear, in the four standard profiles.
Not covered: nested members, optional conditional reads, other source families, a minimal host, 320px captures,
the `e2e-tests/` source-family/type-fit proof of T024 and independent review. Optional reads of conditional
producers and other source families (T022) remain open. Evidence is on the delivering PR.

#4309 S1 source receipt (not task completion): the existing whole-output query remains unchanged. The language-neutral
client and workflow host accept an optional focused whole-source/literal-path query and a bounded member-shape query;
they retain server shape, target, capability and envelope identity facts. Only a current supported/empty analysis can
enable member reads; invalid findings remain explainable without offering its candidates or learning its flag. A nested
answer must carry the requested member role/path/source identity. The workflow host withholds late results after
edits, base changes, disablement or context changes, without saving. Page aggregation checks shared identity and
capability facts but does not equate query-specific context revisions. Existing wire code needs no change: canonical
result/variable/request/input member bindings and unknown future kinds retain structured values through Code Apply
and reopen. Source tests are supporting evidence, not a substitute for S4/S5 real persistence/runtime/UI qualification.
The S0 generic `InvalidSource` wording nit is explicitly deferred; its safe stable code is unchanged and both S0 client
safety notes are handled here. S2 picker model, S3 UI, S4 real runtime proof and S5 composed acceptance remain pending;
no T018–T025 box is closed from this bounded client/wire slice.

#4309 S2 source receipt (not task completion): the new, currently unwired `activityDataPickerModel.ts` groups only
server-published facts into result/input/request/variable families, preserving within-family server order and canonical
binding objects. Explicit variable scopes disambiguate same names and mark visible outer shadows without graph
traversal or identity inference from labels. Pure literal-path helpers retain empty/punctuation-bearing segments,
JSON-array breadcrumb identity and the server's 64-segment/4096-UTF-16-unit bounds. Four member-kind readers,
reference descriptors and provenance helpers are available to S3 but are not registered with any UI. Browsing requires
an enabled capability, the current unambiguous source packet and its exact focused path; protected/restricted sources
stay excluded or disabled. A non-null admitted binding remains the selection authority, including optional reads
whose conditional availability/reason is informational; this adds no optional-authoring toggle. Member readers and
provenance reject any own lowercase `path` property, including null, while leaving the stored payload untouched.
Unknown/truncated/map metadata permits only exact literal key queries; no array index,
fan-out, runtime key discovery or target compatibility is inferred. The existing whole-output model/UI is unchanged.
S3 UI, S4 real runtime proof and S5 composed acceptance remain pending; this source slice closes no T022 or program
acceptance box. The S0 cosmetic backend wording nit remains deferred.

**Checkpoint:** US1 passes actual persisted journeys; #3094 can close only on its narrower own evidence.
Whole-output UI completion does not close nested binding/runtime obligations.

## Phase 4: User Story 2 — Transform selected data explicitly (P1)

**Independent test:** Quickstart journey 2: compact/expanded installed syntax transforms and combines
selected data, handles escaping/alias collisions and one-step Undo/Redo, saves/reopens/runs; unavailable
modules preserve exact source and parameters. Depends on the shared chooser/document seam from US1.

- [ ] T026 [US2] Expose the shared source chooser through installed expression contributions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx` and `expression-tooling/`, retaining compact/expanded editor-session continuity (depends T020–T023).
- [ ] T027 [US2] Implement source-identity parameter reuse/collision-free aliasing and adapter-escaped member access in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityExpressionDocument.ts` and owning syntax adapters; retain existing whole-source portable parameter wire and manual options/profile (depends T026).
- [ ] T028 [US2] Commit source/bindings/cursor as one existing document edit and preserve unsupported definitions/module-loss fallbacks in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/objectExpressionEditor.tsx` and existing expression session integration (depends T027).
- [ ] T029 [US2] Prove compact/expanded continuity, escaped names/reserved words, collisions/reuse and atomic history in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityExpressionDocument.test.ts`, `activityExpressionTooling.test.tsx` and owning installed-adapter tests (depends T028).
- [ ] T030 [US2] Add actual composed source-insertion/transformed-run and missing-module preservation evidence to `Studio/tests/acceptance/journeys.spec.ts` and `tools/ux-acceptance/run.py` captures; retain exact source/portable bindings and committed consumer result (depends T029).

## Phase 5: User Story 3 — Edit and repair connected activities (P1)

**Independent test:** Quickstart journey 3: same-label rename, move/delete/replace, pair/consumer-only/
cross-resource copy, Undo/Redo and reopen preserve intended identity or provide a working repair; invalid
direct/member/portable references reject publication before writes. Depends on shared canonical bindings.

- [ ] T031 [US3] Add one role-aware reference walker/remapper used by `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/duplicateActivities.ts`; cover whole/member reads and portable parameters, internal producer/scope remap, reusable/structure-projected inputs and valid/invalid external identities (depends T012, T019).
- [ ] T032 [US3] Integrate label-only rename/provenance refresh, affected-input diagnostics and working replace/clear navigation with the existing document transaction in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx` and shared reference contribution (depends T020–T021, T031).
- [ ] T033 [US3] Preserve move/delete/replace and Undo/Redo atomicity through `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorDocumentAdapter.ts` and graph operations; never match display labels or rewrite literal member names (depends T032).
- [ ] T034 [US3] Extend `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/duplicateActivities.test.ts` and graph/document tests for pair/consumer-only copy, shadow/same-label scopes, Switch/reusable locations, undo and lossless reopen; recheck shared backend direct/portable invalid-source admission from T010 (depends T033).
- [ ] T035 [US3] Add composed edit/repair/reopen/run and rejected-publication no-write receipts to `Studio/tests/acceptance/journeys.spec.ts` and `tools/ux-acceptance/run.py` capture inventory, using repair actions rather than screenshots alone (depends T034).

## Phase 6: User Story 4 — Preserve causal data through control flow and recovery (P1)

**Independent test:** Quickstart journeys 4 and 5: A/B loop/branch/explicit return data and producer-once
suspension→actual process restart→resume→consumer completion, preserving existing retry input identity.
Depends on member interpreter/admission from US1; the backend proof need not wait for US2/US3.

- [ ] T036 [US4] Extend `Essentials/tests/Workflows/Runtime/Tests/CausalActivityResultResolverTests.cs` and `CanonicalRuntimeInputBindingResolverTests.cs` for member reads across A/B completion order, branches/joins, explicit return/collection boundaries and all source roles; verify no latest/other-iteration fallback (depends T015–T017).
- [ ] T037 [US4] Extend `Essentials/tests/Workflows/Runtime/Tests/ActivityInputSnapshotCheckpointTests.cs` and owning preparation/demand tests for missing producer/absent member/null, nullable/optional prefixes, inline/external parity, ordinary/intrinsic selection and retry reuse; preserve successful producer completion and immutable input identity (depends T036).
- [ ] T038 [US4] Implement a feature-specific owned restart driver under `e2e-tests/durability/` using `e2e-tests/_ServerLifecycle.ps1`; stop/relaunch only owned processes on the same store, actively resume and assert producer count one plus completed consumer selected value, with no KNOWN ISSUE zero-resume success path (depends T037).
- [ ] T039 [US4] Run real loop/conditional/explicit-return and durable restart/retry journeys under `e2e-tests/`, retain store/process/execution/invocation/checkpoint IDs and pre/post inputs, and fix/retest any changed selection/admission seam before accepting (depends T038).
- [ ] T040 [US4] Integrate actual causal/recovery receipts into the finite ledger from `specs/1215-activity-data-selection/quickstart.md`; distinguish supporting resolver tests from completed persistent-process evidence and retain failed logs (depends T039).

## Phase 7: User Story 5 — Cover equivalent and restricted surfaces (P2)

**Independent test:** Quickstart journeys 6–9 reconcile every spec inventory row with functional evidence
or its actual contract reason. Depends on US1 and the stable document/reference APIs; no new model-owned
BPMN mapping or repeater-element storage is implied.

- [ ] T041 [US5] Integrate ordinary input selection in reusable implementation graphs through `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor.tsx` and `ActivityDefinitionDraftEditor.tsx`; prove provider/base revision authority, public-output consumption and immutable template preservation (depends T022, T031).
- [ ] T042 [US5] Integrate consuming-value inputs in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/IntrinsicInspector.tsx` and structure-owned Boolean conditions in `workflow-editor/CaseConditionEditor.tsx`, preserving destination selectors and actual case locations (depends T022, T031).
- [ ] T043 [US5] Audit actual BPMN bound ordinary input composition and `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/BpmnServiceBindingEditor.tsx`; reuse the shared contribution only where eligible and record closed model-owned service/event/gateway boundaries (depends T021).
- [ ] T044 [US5] Cover scalar/object/JSON/Any/list/array/set/map input families, unwrapped repeater boundary, secret/Connection/options/custom controls and masked/denied/revoked/source-owned/read-only/captured-run states in existing shared controls and `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/`; assert actual mutation/value-visibility contracts (depends T041–T043).
- [ ] T045 [US5] Extend `Studio/tests/acceptance/journeys.spec.ts` and `tools/ux-acceptance/run.py` for reusable public-output run, intrinsic/Switch/BPMN eligible inputs, restrictions, manual/held snapshot analysis, stale/error recovery and missing optional modules; record observed composition and zero hidden manual writes (depends T044).
- [ ] T046 [US5] Run changed journeys in desktop/narrow light/dark plus focused 320px reference/picker states; independently open exact-head PNG/ARIA/trace evidence and exercise keyboard/select/replace/clear/cancel/focus/Retry through the composed hosts using `specs/1215-activity-data-selection/quickstart.md` (depends T045).

## Phase 8: M5 integrated acceptance, documentation and closeout

- [ ] T047 Complete customer mapping/transform/repair guidance in the existing authoring documentation under `docs/reference/` and relevant feature READMEs/`EXTENSION_POINTS.md`; link the canonical value-role glossary, include the typed Customer example and state metadata/unknown-shape/collection limits without exposing implementation details in the product flow.
- [ ] T048 Reconcile every FR-001–FR-025/SC-001–SC-008 and nine journey families in `specs/1215-activity-data-selection/quickstart.md` against current committed normal/minimal/restart/restricted results; rerun affected whole suites/journeys after fixes and retain exact source/host/runtime/visual receipts.
- [ ] T049 Complete independent architecture, functional and rendered review with downloadable current-head evidence on the actual delivery PRs; run affected required checks, public-API/dependency/maps/composition gates and CI according to `docs/contributing/merging.md`, preserving failed/skipped/canceled/not-run outcomes.
- [ ] T050 In the PR delivering the last scoped requirement, set `specs/1215-activity-data-selection/spec.md` to Implemented only after all scoped acceptance passes, update this task ledger with receipts and request normal review/merge; never use a separate status-only PR to assert delivery.
- [ ] T051 Verify exact reviewed heads and resulting descendant main, refresh if main advances, then reconcile native children, #3094's own completion, #3882 and `docs/program-goals/activity-data-selection.md`; mark the owner goal complete only with all M1–M5 obligations verified.

## Dependencies and worker slices

```text
M1 closeout + reviewed/merged/qualified M2
  → setup T001–T003
  → shared authority T004–T010
  → US1 binding/runtime T011–T017 ───────→ US4 causal/recovery T036–T040
  → US1 client/wire/picker T018–T023
  → integrated US1 proof T024–T025
  → US2 expression T026–T030
  → US3 reference editing T031–T035
  → US5 equivalent/restricted T041–T046
  → integrated M5 T047–T051
```

The graph shows delivery gates, not permission to skip finer dependencies. US3 may start after its stated
binding/wire prerequisites rather than waiting for US2. US5's independent BPMN audit can run alongside
reusable/intrinsic work; shared inspector/document edits remain serial. US4 backend proof can run while
expression/UI work proceeds. Final qualification waits for every story and cross-cutting criterion.

| Proposed executable slice after M2 | Scope and ownership |
|---|---|
| Shared authority | T004–T010; Foundation owner, API/validation/metadata/Publishing wiring. |
| Member binding/runtime | T011–T017; Foundation owner, coordinate shared Core/compiler commits before UI member enablement. Split only at a tested contract boundary. |
| Existing #3094 | Whole-output portion of T018–T021/T023–T025; retain compensation regression coverage and independent acceptance. Historical compensation M3 #3046 is completed, not a current blocker. Later member enablement is a separate leaf. |
| Member picker and equivalent inputs | Remaining T018–T025/T041–T046; Studio owner on committed source/binding contracts, with owned composed-host integration. |
| Expressions and reference editing | T026–T035; existing adapter/document paths, coordinate shared module/inspector edits. |
| Causal/recovery qualification | T036–T040; runtime/e2e owner, no unrelated recovery refactor. |
| Final integration | T047–T051; delivery lead plus independent reviewers. |

These are bounded proposals, not new Ready issues or assigned workers. Create only the next executable
leaves after M2 approval, within issue caps, with exact task ranges and claim/PR/dependency receipts.
When a leaf delivers only a task's whole-output subset, record that exact receipt and leave its checkbox
open until all stated member/source-family obligations pass. The open compensation Program #3035 is not
a dependency gate here; preserve its existing structure-handler visibility and causal/restart behavior.

## Requirement and journey traceability

| Requirements/outcomes | Tasks | Quickstart journeys |
|---|---|---|
| FR-001–006, 009, 012, 018, 020, 025; SC-001 | T004–T008, T018–T025 | 1, 7, 8 |
| FR-003, 007–014, 021; SC-003, 005 | T006–T017, T036–T040 | 1, 4, 5, 8 |
| FR-015–016; SC-004 | T031–T035 | 3 |
| FR-005, 017–018; SC-004 | T026–T030 | 2, 8 |
| FR-019, 022–023; SC-002, 006–007 | T041–T046 plus T023–T025 | 6–9 |
| FR-024; SC-008 and complete matrix | T001–T003, T047–T051 | All nine |

## Parallel examples and incremental delivery

- US1: profile/metadata contract T004 and analysis/structure contract T006 use distinct owners/files.
  After authority is committed, runtime models T011 and client/wire T018–T019 can proceed separately.
  Member UI enables only after T016–T017 prove the actual runtime capability.
- US2: after the shared insertion seam, different installed syntax adapters can add escaping controls in
  their own files. The common alias/document edit in T027–T028 has one owner.
- US3: one owner changes the reference walker/remapper; a separate reviewer can prepare backend
  invalid-publication controls without editing the same Studio files. Integrate before composed repair proof.
- US4: resolver/checkpoint regressions and owned restart-driver preparation can be investigated separately;
  acceptance execution remains after their stated dependencies and serial owned-host allocation.
- US5: reusable T041 and intrinsic/Switch T042 may be assigned separately once shared APIs settle, with
  explicit coordination of their common editor contribution. The BPMN audit T043 reads its own boundary.

The first useful independent increment is #3094's whole-output authoring path. The US1 MVP includes both
whole and nested Customer ID in Sequence/Flowchart and minimal composition; neither increment is program
completion. Add expression/editing and causal/equivalent journeys on qualified commits, then run the
complete M5 matrix. No release/deploy, runtime examples or unrelated program resumption is part of this plan.
