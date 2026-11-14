# Workflow testing contracts

**Status**: Proposed version-one contracts for [spec 1220](../spec.md). Paths, names and payloads below are planned product contracts, not evidence of implemented endpoints or a packaged runner. The [data model](../data-model.md) owns identities/state; [research](../research.md) records reusable source and runtime proof obligations.

## Composition and authority

The optional `Elsa.Workflows.Testing` capability exposes the library, common scenario runner and deterministic assertions. Studio and the CLI call the same backend service. Runtime gains only the generic trusted target/setup/effect/time seams the runtime proofs justify. It never resolves a Testing or Design implementation; model modules own their execution semantics. Ordinary Test Run/Test Drive keep their existing effects and contracts.

Planned permission keys are `workflows/tests:read`, `workflows/tests:manage` and `workflows/tests:run`. Manage covers authoring/import/rebind; Run covers preflight/admission/delivery/cancel; Read covers authorized discovery/history/reports. These supplement, rather than bypass, underlying workflow design, runtime inspection/value and connection permissions. Requests derive backend/tenant/actor scope from the selected authenticated host. A JSON field or imported reference never supplies authority.

Authorization is checked on each operation and again before a new runtime step/effect admission. Revocation stops new privileged actions and initiates supported scoped closure; it does not grant the caller privileged cleanup or evidence access. The host retains narrowly scoped responsibility to reconcile resources it already owns. Reports remain permission-filtered on every read.

API capabilities advertise shell-relative `/workflow-tests` links through normal selected-backend SDK resolution. Fixed endpoints use query selectors for opaque read identities and typed JSON selectors for writes. Neither client concatenates untrusted case/source IDs into URL paths or bypasses origin/base-path protections.

## Library and candidate API

All list/history endpoints are bounded and return continuation metadata. The table omits the selected shell base path.

| Method and path | Request/response and guarantee |
|---|---|
| `GET /workflow-tests/capabilities` | Authorized target/setup/step/assertion/effect/time profiles, schema versions and availability reasons; discovery is read-only |
| `GET /workflow-tests/cases` and `/suites` | Search, skip/take or opaque continuation; safe library summaries |
| `GET /workflow-tests/case` and `/suite` | Query `id`, optional exact `revisionId`; scoped working copy or immutable revision |
| `GET /workflow-tests/case-history` and `/suite-history` | Query owning ID and bounded cursor; revision metadata, not unbounded content |
| `POST /workflow-tests/cases` and `/suites` | Create typed working copy; returns ID and working revision |
| `PUT /workflow-tests/case` and `/suite` | Body contains ID, expected working revision and complete desired testing content; stale save is `409` |
| `POST /workflow-tests/case-captures` and `/suite-captures` | Owning ID, expected revision and operation ID; immutable case content or authored suite membership/selection rules; same operation/material replays. A suite capture alone does not resolve/pin its latest-saved members. |
| `POST /workflow-tests/rebindings` | Exact old case revision, deliberate new target, candidate manifest and expected working revision; validates and saves a new case revision without modifying old captures |
| `POST /workflow-tests/preflight` | Exact saved/ad-hoc case content, explicit candidate request and environment mappings; read-only diagnostics/preparation digest, no scope, external fixtures, production publication or execution |
| `POST /workflow-tests/exports` | Logical case/suite selection resolved/captured consistently once, or an exact retained run/manifest, plus optional retained candidate material; versioned bundle fixes exact member revisions and safe requirements |
| `POST /workflow-tests/import-analysis` | Bundle plus explicit local mapping/conflict policy; read-only validation and planned changes |
| `POST /workflow-tests/imports` | Operation ID, bundle digest and accepted analysis/mapping digest; atomically installs the authorized library changes or reports conflict |

Creating/saving a case may preserve incomplete authoring with visible validation diagnostics. Capture and execution require their respective complete invariants; saving a broken fixture reference cannot make a case runnable. Authored suites reference logical case IDs with latest-saved selection by default. Admission/export resolves and captures their exact versions once under provider-owned concurrency validation and persists an immutable member manifest; saving the suite does not pin it to old member content. Discard/delete operations obey pins and retention, rather than removing evidence required by admitted or retained runs.

The workflow designer's candidate request contains current editor state plus presentation/layout and a generated snapshot identity, including unsaved edits. Preflight and admission fingerprint that exact material. Neither operation silently saves the business draft. The run dialog identifies that captured editor snapshot and shows when subsequent editing makes its setup stale. Other authoring surfaces that require saved content must explicitly require Save and show the exact revision.

Pending or invalid unapplied code-editor changes block capture until the author explicitly applies or resets them. The UI retains the buffer and focus and explains the required action; it cannot submit old applied state under a current-code label. Qualification disables autosave, applies a changed input, captures/tests that new content and reads the stored business draft back to prove it was neither saved nor substituted.

Preflight compares the captured target's node/path/model/contract/dependency identity. It returns structured diagnostics with target/member/step references and safe corrective actions. Missing data, ambiguous waits/providers, unsupported boundaries or stale targets do not dispatch any activity or allocate an external fixture. Admission recomputes the preparation digest against current authority/capabilities and validates its resolved suite/member revision vector; changed candidate/configuration or stale prepared membership requires revalidation, not a partial silent refresh.

## Admission, progress and manual execution

| Method and path | Contract |
|---|---|
| `POST /workflow-tests/runs` | Operation ID, logical latest-saved case/suite selection or exact captured/ad-hoc revisions/manifest, explicit candidate manifest/request, environment mapping, selected entries and skip policy; returns `202` with durable admission ID, request/material fingerprints, exact resolved member manifest and progress/reconcile links |
| `GET /workflow-tests/run` | Query run ID; immutable manifests, live lifecycle, per-attempt execution/check/verdict/closure state and bounded observation cursor |
| `GET /workflow-tests/run-operation` | Query client operation ID in trusted scope; reconciles a lost admission response without submitting a fresh run |
| `POST /workflow-tests/steps` | Run/attempt, exact expected active step or manual wait selector, operation ID and typed stimulus; normal supported model delivery, duplicate reconciliation |
| `POST /workflow-tests/cancellations` | Run/attempt, operation ID and reason; durable cancellation intent and reconciliation status; acceptance is not settled cancellation |
| `GET /workflow-tests/run-observations` | Query run/attempt and cursor; ordered permission-filtered evidence |
| `GET /workflow-tests/reports` | Query run ID and `format=json` or `junit`; report identity/as-of sequence and complete status counts |

Manual and saved execution share admission, target/setup preparation, normal runtime dispatch and effect/time policies. A manual session uses an immutable ad-hoc case revision with no assertion verdict unless explicit assertions were authored. Interactive delivery/time advancement records its exact supported payload, order and operation identity. Save as case offers those steps and chosen assertions for author confirmation. Conversion replaces old runtime wait IDs with stable semantic selectors owned by the model/driver, retaining run IDs only as provenance; ambiguous or unsupported conversion is visibly unavailable. A saved waiting-composite scenario must replay its confirmed deliveries/time advances headlessly without another manual action. Saved scenarios drive the same supported step services automatically; the headless path does not simulate Studio clicks.

The server validates the complete selected execution before effects: exact candidate/dependency closure, target support, input/context, scenario/assertion profiles, fixture ownership capability, effects/time policy, permissions and finite deadlines. No activity is dispatched when any required boundary is unavailable. A preparation failure after allocating owned resources enters cleanup and retains its receipts; it is never presented as a clean preflight refusal with no resources.

Normal suite admission resolves the latest saved member cases once and freezes their exact revisions with authored membership, candidate/dependency material and mode. A provider-owned capture operation validates the suite and every member revision together, installs the complete manifest/pins/admission atomically, and either admits one coherent set or reports conflict before dispatch. A concurrent save is included wholly before that capture point or requires complete re-resolution/revalidation before admission; an edit after it affects only a later run. Qualification races both a member save and suite membership change against admission/export and proves conflict leaves no partial runnable manifest or pins.

Retry/reconciliation uses the original frozen material, including when cases, current draft or configuration have since changed. A deliberate normal rerun uses a new operation ID, latest saved cases and current chosen candidate, followed by compatibility validation. Historical replay explicitly selects a retained resolved member/candidate manifest. A bundle consumes its exported exact revisions; it never refreshes mutable library cases. Missing retained content/modules/dependencies is unavailable, not permission to choose newer material.

Idempotency keys bind the trusted scope, operation kind, intended resource and client request fingerprint. That fingerprint includes latest-saved selection intent where requested; a separate admitted material fingerprint records its one-time resolved revisions. A duplicate compares the original request and returns the existing receipt/manifest without re-resolving latest. Changed request material is `409 operation-conflict`. A dispatch/delivery response loss never authorizes a new execution. Step delivery binds the exact attempt/step/generation and resolved wait identity. Same-name or multiple active waits require a better semantic selector; they are not resolved by first-match ordering or old run IDs.

Expected faults/waits are assertions over committed normal runtime observations. An expected wait can satisfy its check before normal teardown cancels the owned execution. Both facts are retained. A user-requested cancellation yields Cancelled after settled closure; if closure remains unknown or fails, verdict is Incomplete with cancellation intent preserved. Scope expiry/interruption is not automatically a pass or a successfully settled cancellation.

## Context and execution boundary

An activity target retains the full pinned executable and runs only the selected occurrence and its semantically owned work through a trusted test-entry contract tied to the finite `WorkflowTestScope`. It never overloads `TriggerNodeId`, changes the production artifact root or copies a subtree into a standalone definition. Normal parentless target completion/fault/cancellation is the test boundary, including zero-outcome completion. Context setup supplies exact supported scope/value frames without executing ancestor control flow or inventing predecessor execution records. Original configured bindings remain in effect except for visible deliberate input overrides. Nested lexical shadowing, iteration identity, causal prior-output selectors and reusable closed boundaries are validated. Missing versus explicit-null data remains distinct even where ordinary production binding would accept null.

The runtime-owned durable test frame/capture owner supplies qualified lexical reads/writes/closure, rematerializing callbacks and configured output capture without fabricating missing ancestor invocations. It propagates across normal invoke/resume, fresh activations and owned children. Supplied iteration frames remain read-only unless normal owned execution creates a writable frame. Fixtures for producers within selected owned work are rejected; actual target/child results remain authoritative. One typed result-reference path covers both direct bindings and expression parameters before normal envelope/protection-policy evaluation. Parent-dependent control instructions without a meaningful isolated boundary are explicitly unavailable.

The selected native leaf/composite/nested/reusable profiles are mandatory delivery witnesses. BPMN and other model-bound profiles use their owning model's entry, waiting, settlement and teardown semantics. A generic native selector cannot extract a BPMN-bound task and pretend token/claim/incarnation semantics do not exist. Runtime witnesses must validate the full-executable test-entry contract; if they disprove it, revise/review the design rather than silently adopting another execution model or deferring required support.

## Effects, time and fixtures

Controlled mode is a closed qualified profile. Every potentially reachable effect/time boundary in the selected owned work must have declared support and an explicit substitute/configuration, including declared dynamic requirements. Initial qualified support includes the native witnesses, supported Event/User Task actions, scoped `Delay` and HTTP's ordinary transport boundary. Unknown custom code, hidden I/O or incomplete closure makes that profile unavailable; metadata is not a universal sandbox. Runtime admission also refuses an undeclared boundary reached after preflight, without invoking a real adapter. The resulting inability is retained, never hidden by an assertion pass.

An interaction records an attempted call before execution, its admitted substitute or integration mapping, safe operation/request digest and completed/refused/unknown disposition. Non-invocation fails on an attempted call, not merely on completed transport. It can pass only after the declared observation interval and qualified asynchronous settlement establish no outstanding responsibility. An empty journal with pending work is Incomplete, not proof of absence.

Integration mode requires explicit safe test-system connection mappings and a supported time policy, using wall time by default. It uses the real integration path and records actual transport evidence. A mixture of real/substituted boundaries or controlled integration time requires explicit per-boundary configuration and qualification of that combination; the result retains which boundaries were real. It never substitutes a production/default connection when a mapping is absent or unusable. Permission and secret access remain with the owning connection/integration domain.

Controlled time is attempt-local and monotonically advanced. An advance operation records prior/current scenario instants, revision, operation fingerprint and supported due-work responsibility. Equal retries converge; backwards time or reuse with another instant conflicts. Advancing time queues due work through normal supported timer/resumption paths, then awaits explicit settlement with a finite wall deadline. Virtual due-time selection is distinct from wall-clock claim/fence/lease ownership; passing virtual now into an infrastructure lease clock is invalid. The ordinary pump excludes controlled timers before due-query/claim, and advancement racing timer persistence/bookmark commit must retain idempotent pending delivery rather than lose the wake-up. It does not advance infrastructure leases, host clock, real timeout, expiry or retention. Unqualified timers cannot silently run on real time in a controlled scenario.

Fixture preparation allocates fresh mutable instances for each attempt, including substitute cursors and interaction histories. A durable receipt claims each created resource before/atomically with its creation. Partial preparation retains teardown responsibility. Cleanup uses exact ownership and operation identities, never inferred name prefixes or broad deletion of shared test-system data. Unknown creation outcomes reconcile the exact operation; they do not permit guessed cleanup.

Cancellation, timeout, expiry and interruption first close future admission through the normal scope fence, then reconcile active roots, children, detached work, ingress, due timers and owned fixture resources. Cleanup has its own finite wall deadline and durable continuation responsibility. Host restart/CLI disconnect cannot erase that responsibility or initiate blind redispatch. Final Passed requires settled owned cleanup; otherwise the report retains Incomplete and precise remaining work.

## Portable bundle and collisions

The bundle profile is `elsa.workflow-test-bundle/1`. It contains case/suite authored profiles, an exact resolved portable member/revision manifest, candidate source/artifact identities, required extension profiles and safe dependency/connection requirements. Export resolves latest saved cases once with the same concurrency discipline as admission, unless the caller chooses an existing exact run/manifest. The bundle always executes those fixed revisions without resolving latest; importing it may also create logical library memberships for deliberately requested future library runs. It includes original content fingerprints and versioned transport digests; export does not smuggle credential values into literals, faults, fixtures or reports. Imported extension names remain data, not permission to execute arbitrary code.

Import analysis verifies supported format/profile versions, declared digests, unique logical/revision keys, exact suite-member closure and required schemas. It resolves explicit destination workflow/candidate/connection mappings through current authority. Missing requirements stay visible and cases remain unavailable; they are never implicitly rebound to a similarly named node, module or test system.

| Collision | Default behavior |
|---|---|
| Same portable logical/revision identity and same profile/content digest | Reuse mapped local immutable revision; idempotent no-op |
| Same portable immutable identity with different profile/content digest | Reject entire import as identity conflict; never overwrite history |
| Existing editable local case/suite differs | Reject unless the caller explicitly selects a new revision with expected CAS or an explicit fork to a new logical identity |
| Conflicting/duplicate suite keys or missing referenced revision | Reject; never choose an entry by order or resolve latest |
| Foreign backend/tenant/source snapshot fields | Preserve safe origin provenance only; never grant authority or treat foreign row IDs as local identities |

An accepted import analysis digest includes every mapping and conflict decision. Apply uses one provider-owned atomic library operation; concurrent changes invalidate that digest/CAS rather than publishing a partial runnable suite. A retry with the same scoped operation/bundle/mapping fingerprint replays its receipt. It does not create another fork or another case revision.

## Reports and headless runner

The planned packaged command is `dotnet-elsa test run`. It accepts an explicit backend and a library selection, exact retained manifest or portable bundle, an explicit current-candidate manifest/mapping or retained replay selection, and report destinations. Normal library selection delegates latest-saved case resolution to admission; bundles/historical manifests consume fixed revisions. Authentication uses existing supported host credentials/secret references; secret values are not command arguments or report properties. The CLI is a client of the common API; it does not own an alternate workflow engine or separately resolve mutable dependencies/candidates.

JSON profile `elsa.workflow-test-report/1` is authoritative. It contains report/run/admission/attempt identities, exact candidate/case/suite/dependency profiles, mode/time policy, execution states, checks, per-case verdicts, skip reasons, interruption/cancellation facts, settlement/cleanup status, safe differences and runtime evidence references. Permission-redacted or expired evidence has an explicit availability field. A report read during execution says it is non-final and cannot authorize success.

JUnit is a CI projection: passed asserted cases map to successful test cases; assertion failures to `failure`; unavailable/incomplete/infrastructure/invalid cases to `error`; deliberate skips and settled cancellation to `skipped` with distinct reasons/properties. Properties retain the original verdict, mode, exact identities and cleanup status. These mappings never change the JSON status or process exit. A waiting exploratory run is not emitted as a successful assertion case.

Exit aggregation follows this precedence:

| Exit | Condition |
|---|---|
| `130` | User/CI requested cancellation; retain any earlier failures and all closure evidence. A lost CLI connection without confirmed cancellation is an inability-to-complete outcome, not a claim of settled cancellation. |
| `2` | Any unavailable, incomplete, invalid, infrastructure-failed or unasserted selected execution; required case skipped; disallowed skip; empty/all-skipped selection; or inability to obtain an authoritative final report. This takes precedence over simultaneous assertion failures. |
| `1` | One or more authoritative assertion failures, with no preceding inability/cancellation condition. |
| `0` | At least one asserted case ran, all required selected cases passed with settled cleanup, no failed/unavailable/incomplete/unasserted case remains, and any optional deliberate skips were explicitly permitted. |

All selected cases are required by default. Allowing authored optional skips requires an explicit run option captured in admission and the report; a failed/unavailable running case cannot be retroactively converted to a skip. Even allowed all-skipped or empty selections exit `2`. Suite summaries expose all counts and the skip policy, so “success with permitted skips” is visible. Studio uses the same aggregate policy and also exposes the original per-case verdicts.

On Ctrl+C the CLI sends idempotent cancellation and reconciles within a bounded grace period. It writes the last truthful report even if closure remains incomplete; the server retains finite expiry and cleanup responsibility. Reconnecting/reconciling an admitted run does not create another attempt. Exported bundle execution can create ephemeral run-owned imports/captures, but their revision identities and retention policy are explicit and they are not silently discarded before report/history obligations are met.

## Module contributions and failures

Design-time discovery and runtime actions use separate independently dispatched contracts with shared `.Core` shape records. Planned Testing contribution phases use named Sequential events such as `WorkflowTestCapabilitiesCollecting` and `WorkflowTestPreflighting`, with exactly one aggregating handler for each fan-in and typed target/setup/step/assertion/effect/time extensions. Each extension has a stable key and profile version. Discovery must report absent, unsupported and failed separately; it must not advertise support solely because a registration exists.

Runtime actions remain in the owning model/integration/timer module and are selected from the admitted exact profile. Existing trigger/wait drivers are adapted where suitable; a Testing module does not reimplement their transport/token semantics. Missing or multiple matching implementations yield a diagnostic before dispatch, rather than registration-order selection. Store and orchestrator replacement contracts declare their single-provider role and reject duplicates during composition. New public extension contracts ship a domain catalog and contributing modules' cross-domain notes.

Expected API refusals are typed: `400` malformed/schema-invalid request; `403` denied operation with privacy-safe detail; `404` resource absent from authorized scope; `409` revision, operation, target/preflight or import conflict; `422` validated but unsupported/unavailable execution profile; `503` unavailable owning infrastructure. Accepted run failures are retained in their run/result resource. Diagnostics contain stable code, safe message and resource/step/member reference, not protected content or raw provider exceptions. Raw I/O, serialization and persistence exceptions are wrapped at their feature boundary.

Before delivery, contract qualification must prove CAS/capture/pins and concurrent latest-member resolution at admission/export; admission/delivery retry after response loss; manual-step conversion/replay; pending-buffer capture refusal and applied-unsaved capture without a draft save; unexpected effect refusal; timer isolation; partial preparation cleanup; restart/expiry/interruption; mixed verdict aggregation; and package-client parity. Those proofs complement the mandatory composed Studio journeys and independent UX acceptance; this document itself supplies neither runtime nor acceptance evidence.
