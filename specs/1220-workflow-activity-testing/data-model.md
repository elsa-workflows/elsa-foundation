# Data model: Workflow and activity testing

**Status**: Proposed design for [spec 1220](spec.md); no implementation or qualification is implied.

## Ownership and storage

`Elsa.Workflows.Testing` is an optional production capability, separate from developer test utilities. Its `.Core` owns public case, suite, admission, result and contribution contracts. Feature orchestration, API and persistence adapters belong under `Essentials/src/Workflows/Testing/`. Normal Runtime consumes only generic trusted execution/setup/effect/time contracts in `Workflows.Runtime.Core`; Runtime never references Testing or Design implementations.

The Testing authoring adapter uses the delivered [source revision ports](../../Essentials/src/Persistence/Core/SourceRevisions.cs) and [shared EF engine](../../Essentials/src/Persistence/EntityFramework/EfSourceRevisions.cs). A typed adapter supplies the trusted scope, testing-specific profile and validated serialized content. It owns testing permissions; it does not expose arbitrary source-engine scope or storage rows to API clients. Capture is not workflow publication.

| Storage concern | Owner and invariant |
|---|---|
| Case/suite working copies and captures | Testing typed adapter over `ISourceRevisionReader`/`ISourceRevisionWriter`; CAS saves, immutable snapshots, bounded history and pins |
| Captured candidate material | Existing normal publishing/compiler and content-addressed executable store, with test-owned source provenance and retention references; no production trigger publication |
| Run admission and live progress | Testing run store; immutable admission plus monotonic, fenced state with an expected revision |
| Result evidence | Append-only Testing observations and final results; later runs never overwrite earlier evidence |
| Native executions, waits and test scope | Existing Runtime stores and normal checkpoint/admission/cancellation paths |
| Test fixtures, substitutes and clock | Attempt-owned state through qualified module contracts; no process-wide mutable scenario state |

Provider-neutral persistence contracts split commands from bounded reads. EF mappings support consumer-owned contexts; no `IQueryable`, provider SQL or EF entity escapes the boundary. The durable composed-host path must use durable authoring and run stores. An optional in-memory provider cannot satisfy durable history/restart acceptance.

## Identities

Names and ordinal positions are presentation, never identity. Comparisons use ordinal identity semantics and the existing length-framed hashing conventions; delimiter concatenation cannot construct authority or compound keys.

| Identity | Meaning |
|---|---|
| Library scope | Server-derived backend partition, tenant and testing authority; never accepted from a bundle as trusted context |
| Case/suite ID | Stable logical resource inside that authorized library |
| Working revision | CAS concurrency token; not a published version or immutable capture ID |
| Local revision ID | Immutable source snapshot reference in one authorized store; meaningful only with its owning case/suite and scope |
| Revision fingerprint | Shared source-engine fingerprint over the testing profile and deterministic serialized content; exact content evidence |
| Portable identity | Bundle namespace, logical resource key, revision token, profile and content digest; provenance and collision detection, not access authority |
| Candidate identity | Captured source/snapshot plus normal artifact ID/hash, compiler/model identity and exact dependency manifest |
| Run ID | Server-issued admission resource; not an execution ID or bearer permission |
| Case attempt ID | One selected case revision executed once within a run; owns fresh fixtures, interaction history and clock |
| Operation/step identity | Caller operation ID plus admitted fingerprint; steps additionally bind the attempt, stable step key and intended generation |

Portable imports map validated portable identities to local snapshots. They never copy foreign tenant/backend authority or assume a foreign snapshot key is a local row. Exported content digests are recomputed; hashes establish content consistency, not author permission or code trust.

## Authored records

| Record | Fields and constraints |
|---|---|
| `TestCaseContent` | Profile/version, name, purpose, target binding, setup, fixtures, mode, dependency/time policy, ordered steps, finite real duration and cleanup policy; no plaintext credentials |
| `TestCaseWorkingCopy` | Stable case ID, typed content, working revision and author metadata; outdated saves conflict |
| `TestCaseRevision` | Immutable captured profile/content/fingerprint and provenance; referenced by resolved run/export manifests |
| `TestSuiteContent` | Name/purpose, workflow associations and ordered entries: stable entry key, logical case ID with `latestSaved` selection by default, required flag and optional deliberate skip reason |
| `TestSuiteRevision` | Immutable captured authored membership/selection rules; saving it does not pin members to their then-current case content |
| `ResolvedSuiteManifest` | Exact suite revision plus ordered entry/case revision/fingerprint list, resolution revision vector and capture operation; immutable and pinned for its run or export |
| `TestEnvironmentBinding` | Authorized selected backend/tenant context, safe test-system connection references and qualified module profiles; credentials resolve server-side |

The owner selected latest saved cases, frozen per run. Saving a suite stores logical member IDs and selection rules; a later saved case edit is eligible for the next normal suite run without refreshing the suite. At admission, the provider resolves those latest saved versions once, captures immutable case revisions and atomically validates the suite/member revision vector while committing the exact `ResolvedSuiteManifest`, pins and admission. Concurrent membership/member edits either fall wholly before that capture point or cause a conflict and complete re-resolution before admission; a mixed, partially validated set cannot dispatch. Edits after capture do not alter the admitted run. Unsaved case-editor buffers are not latest saved case content.

Export performs the same consistent resolution/capture when exporting a logical suite selection. The resulting bundle fixes exact revisions; running/importing that bundle never refreshes its cases to mutable library latest. Exporting an existing run/manifest preserves its original revisions instead. Historical replay explicitly names a retained resolved manifest and candidate; it does not repeat latest-saved selection. Concurrency qualification must race a case save and a suite membership edit against admission/export, prove one coherent validated manifest with no partial pins/admission on conflict, and show later edits leave both running execution and exported bundle unchanged.

The version-one content profiles are `elsa.workflow-test-case/1` and `elsa.workflow-test-suite/1`. The typed serializer emits deterministic property ordering and retains semantically significant array order and omitted/null distinctions. The source engine remains the owner of its fingerprint algorithm. Portable serialization and its digest use an explicitly versioned profile; an unknown profile cannot be interpreted as the nearest supported version.

An exploratory manual run uses the same case shape, captured as an immutable ad-hoc revision in its admission evidence. It need not create a named library case. Its revision identity/digest remains available in the result. Saving from that run creates a named case with copied supported setup, author-confirmed ordered event/action deliveries and time advances, and deliberately selected assertions. Recorded runtime wait IDs remain diagnostic provenance only: conversion derives stable model-owned semantic selectors and rejects missing/ambiguous mappings before saving a runnable case. Observations are not automatically converted into expected values. A manually resumed waiting composite must replay those saved steps headlessly without another manual action.

## Candidate and target

`CandidateRequest` has one explicit source: current editor snapshot, exact retained source/capture, or exact published artifact/version. A workflow designer sends its current state, activity presentation and layout, including unsaved edits. Capturing that material does not save or publish the business draft. Other editors that require a saved revision present an explicit Save action and that exact revision. A current-candidate request cannot silently fall back to a stored draft or the last successful artifact.

The code editor's unapplied buffer is not the applied designer state. Pending or invalid unapplied code blocks candidate capture until explicit Apply or Reset; the UI preserves that buffer and identifies the required action. It cannot silently capture an older applied state while pending code appears current. An applied unsaved change is eligible even with autosave disabled, and its test must leave persisted business-draft content unchanged.

`CandidateManifest` retains the submitted snapshot identity/content digest, normal compiled artifact identity/hash, source reference, model/compiler identities and required exact activity/module/dependency identities. Presentation/layout are retained for inspecting the tested occurrence but are not introduced into normal executable behavior hashing. A dependency manifest records safe configuration revision/digest and runtime profile as well as reusable activity version identity; credentials themselves are absent.

`TargetBinding` is one of:

- **Workflow**: exact candidate association plus an explicit direct or module-owned start/trigger configuration.
- **Activity occurrence**: candidate association, stable node ID, ordered owning structural path, owning model identity and expected input/output/outcome contract identity. Reusable occurrences also retain the exact referenced activity version and its closed-boundary contract.

The case associates with a logical workflow and expected target contract, rather than forcing every future run to use the case's original artifact. Each rerun deliberately selects current candidate content and validates that binding. Compatible edits to the selected activity's configuration are precisely what regression tests exercise. Removal, ambiguity, scope movement, incompatible contract/model change or required unavailable dependencies refuse admission. Matching by label, index or a different node is prohibited. Deliberate rebinding records old/new bindings and creates a new saved case revision; future latest-saved suite admissions may select it, while captured run/export manifests and history retain their original revision.

Minimum delivery includes qualified native leaf, selected native composite, nested-scope and reusable-occurrence profiles with representative normal-execution proofs. BPMN-bound targets retain model-owned semantics and have a separate qualified/unavailable matrix. Advertising truthful unavailability cannot replace the required native witnesses.

## Explicit setup

| Setup component | Identity and semantics |
|---|---|
| Workflow inputs | Stable declared member identity and JSON value; omitted, explicit null and supplied values stay distinct |
| Occurrence input override | Explicit override of a declared member; original configured binding remains visible and the case records override provenance |
| Variables | Exact declaration/scope/iteration-frame identity and typed value; lexical shadowing is preserved |
| Prior output fixture | Exact producer node/structural path, result/output member, causal slot or iteration identity and typed value; never a fabricated predecessor execution |
| Expressions | Existing supported engine and binding semantics; required visible inputs/context are declared and validated through the owning profile |
| Connections | Explicit safe reference/mapping plus mode; resolution obeys current permissions and never exports secret values |
| Fixture definition | Stable fixture key, qualified provider/profile and safe configuration; mutable instance state is allocated per attempt |
| Test context frame/capture owner | Attempt-owned frame ID, declaring scope/iteration/contract, visibility and qualified read/write/close policy; durable value/capture state separate from activity execution rows |

The native context adapter creates only supported trusted runtime frames/value seeds. It does not synthesize successful predecessor activity histories or activate ancestor control flow. The runtime-owned test frame/capture owner supports qualified normal variable operations, rematerializing callbacks and configured output capture even when the selected entry lacks an ordinary enclosing invocation. Its durable identity propagates across invoke/resume/fresh activation and child work. A reusable activity's closed input/capture boundary is honored. Supplied iteration frames are read-only unless normal owned execution creates a writable frame. Fixtures may name only producers outside the selected owned work; they cannot shadow actual target/child results, which remain authoritative. Missing required context is unavailable before execution, including missing prior outputs that ordinary runtime might otherwise materialize as null. Explicit captured-contract defaults remain allowed and visible. The typed result-reference path serves both bindings and expression parameters before normal protection-policy evaluation.

## Steps and assertions

Each step has a stable key, kind/profile version, safe authored data, exact intended target/wait selector, real timeout and optional assertion list. Step kinds include start, deliver supported event/action, advance controlled time, await supported settlement and assert. A saved wait selector is resolved to exactly one active wait in the admitted attempt; the resolved runtime wait/model incarnation is retained. No arbitrary business sleep is a synchronization step.

An assertion has a stable key, versioned assertion profile, explicit subject selector and expected value/predicate. Initial subjects include outputs, outcome keys, faults, active waits, relevant paths and qualified dependency interactions. Missing or withheld evidence differs from JSON null; an unavailable observation cannot satisfy equality or non-invocation. Fault matching uses safe declared codes/types, not secret exception text. Model-quality rubrics are outside this deterministic assertion model.

Interaction assertions declare a bounded observation interval and settlement condition. The interaction journal records attempted, admitted, completed, refused or outcome-unknown calls against their owning boundary. “Never invoked” is not equivalent to “no successful response”: an attempted call violates it. Empty observations with outstanding/unknown responsibility cannot pass.

## Effects, time and resources

`ExecutionPolicyManifest` freezes mode, target/setup/step/assertion profiles, complete qualified effect closure, substitutes or explicit integration mappings, and time policy. Preflight covers the selected semantically owned work, including potential branches/children and declared dynamic dependency requirements. An incomplete closure refuses execution. Runtime effect admission also refuses undeclared dynamic boundary use; it never contacts a real dependency as fallback.

Controlled mode allocates fresh substitute response cursors, interaction journals and virtual clock state per attempt. Clock state includes initial instant, current instant, monotonic advancement revision and supported due-work responsibility. Controlled timer due selection is scope/virtual-time owned, separate from wall-clock infrastructure claim/fence/lease ownership; the ordinary pump cannot query/claim controlled timers. Advancing business time does not advance the host clock, leases, real step/run timeouts, scope expiry, retention or cleanup deadlines. Integration mode records an explicit supported time policy and test-system mappings; controlled time or a mixture of real/substituted boundaries is allowed there only when every relevant boundary qualifies that explicit combination.

`FixtureResourceReceipt` records attempt/fixture identity, logical preparation operation, exact owned resource identifier, ownership proof, preparation state and idempotent teardown key. Preparation records cleanup responsibility before or atomically with resource creation. If creation outcome is unknown, teardown reconciles the exact operation rather than guessing ownership or deleting shared data. Partial preparation, dispatch refusal, timeout, cancellation and runner interruption all leave durable cleanup responsibility. Reusing a case never reuses mutable fixture state.

## Run state and evidence

`TestRunAdmission` is immutable: client request fingerprint, resolved material fingerprint, selected exact revisions/manifest, candidate/target/dependency manifests, selected skip policy, trusted actor/scope, attempt IDs, admitted profiles and real deadlines. The request fingerprint includes selection intent and submitted data; the material fingerprint includes the one-time resolved latest-saved content. A new rerun has a new operation ID. A duplicate operation first compares its request fingerprint and returns the original receipt/manifest without resolving latest again; changed request material conflicts even when the logical operation ID is the same.

The live run state is monotonic: `Admitted → Preparing → Executing → Closing → Finalized`; preparation refusal can go directly to Closing. Per-attempt progress records observation sequence and expected state revision. A store-owned lease/fence prevents two workers from independently dispatching the same step; lease expiry after possible dispatch requires reconciliation, never blind retry. The worker cannot rewrite admission material.

Results keep these axes separate:

| Axis | Values/meaning |
|---|---|
| Runtime execution | Original model/runtime states, including running, waiting, completed, faulted, cancelled or unavailable/unknown evidence |
| Assertion checks | Passed, failed, not evaluated or unavailable, each with safe expected/actual difference and observation references |
| Attempt verdict | `NotEvaluated`, `Passed`, `Failed`, `Skipped`, `Cancelled`, `Unavailable`, `Incomplete` |
| Closure | Open, admission closed, cancellation requested, reconciling, closed; cleanup receipts and remaining responsibility retained |

An expected business fault may pass assertions while preserving runtime Faulted. An expected wait may pass its check, then be cancelled normally for test teardown; the report retains both the observed wait and teardown cancellation. No final Passed exists until all required checks and owned cleanup settle. An exploratory case without assertions remains NotEvaluated and cannot produce CI success. Unexpected faults are failed expectations when authoritatively observed; missing infrastructure/evidence, uncertain dispatch/settlement or cleanup failure is Incomplete/Unavailable rather than a business assertion failure.

Append-only observations have a store-assigned sequence, wall timestamp, optional scenario timestamp, stable event key, safe payload and provenance. Finalization records an immutable result referencing those observations. Further reconciliation appends an explicit correction/superseding record linked to the original; it never rewrites what an earlier report claimed. Exported reports identify their report revision and as-of observation sequence.

## Retention and protection

Retained results pin exact case/suite/candidate/dependency material required by their declared replay policy. Cleanup removes attempt-owned ingress, resources and pending work, not historical evidence. Retention expiry can release those pins under the authorized policy; history then explicitly reports expired material and replay unavailability. A deletion/discard cannot silently invalidate an admitted run's pins.

Every list/read/save/capture/run/step/report operation uses current testing permissions and underlying workflow/connection/value permissions. Redaction retains identity/status without turning a hidden value into null. Imported source payloads, diagnostics, reports and JUnit files cannot carry plaintext credentials. Authoring, runtime evidence and result retention are separate stores/policies with explicit associations, not duplicated workflow business state.
