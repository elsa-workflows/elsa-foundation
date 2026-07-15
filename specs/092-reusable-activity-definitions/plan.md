# Implementation Plan: Reusable Activity Definitions

**Branch**: `598-reusable-activity-definitions` | **Date**: 2026-07-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/092-reusable-activity-definitions/spec.md`

## Summary

Replace Foundation's construct-only workflow-as-activity surface with first-class reusable `ActivityDefinition` drafts and immutable versions. The first provider compiles an authored `ActivityGraph` into a content-addressed `ExecutableActivityTemplate`; workflow publication places exact templates with deterministic namespacing; Runtime executes a Runtime-owned `GraphActivity` as an ordinary composite inside one workflow execution.

The work extends the existing Activity Catalog, content-addressed artifact/Source Reference bridge, execution pipeline, scheduler-boundary checkpoint, activity-execution inspection, and Groundwork seams. It makes API, diagnostics, version diff, dependency/upgrade, and hierarchical inspection contracts explicit before Studio UX design starts. Runtime remains artifact-only and gains no Design or Publishing dependency.

## Technical Context

**Language/Version**: C# / .NET `net10.0`

**Primary Dependencies**: Existing Elsa Activities Design/Runtime Core contracts, Workflows Design/Publishing/Runtime contracts, FastEndpoints API base, mediator/event pipeline, runtime scheduler and checkpoint pipeline, expression/input binding compiler, content-addressed workflow executable and Source Reference stores

**Storage**: Provider-neutral store contracts in Core; Groundwork is the first-party durable implementation. In-memory stores support focused conformance tests. No new EF migration or schema surface.

**Testing**: xUnit unit and integration tests, Groundwork SQLite full-host restart tests, existing executable compiler golden/characterization tests, Runtime scheduler/checkpoint/resumption tests, API contract tests, architecture guards, and Elsa 3 import fixtures

**Target Platform**: Elsa Server and independently composable Elsa Activities/Workflows packages in Runtime-only, Design-only, and combined deployments

**Project Type**: Modular backend/domain feature spanning Activity Design, a graph Design provider, Publishing bridge, Runtime graph consumer, Runtime inspection API, persistence, and one-way Elsa 3 import

**Performance Goals**: Iterative O(nodes + direct edges) template traversal; bounded cursor pages for at least 10,000 committed descendants; no eager descendant/layout/value hydration on workflow-instance summary; deterministic compilation and placement without call-stack-dependent recursion

**Constraints**: No Runtime -> Activity Design, Workflow Design, or Publishing implementation references; exact version ids only; artifact-only Runtime; atomic publication and boundary checkpoints; no arbitrary Foundation depth/node/artifact-size defaults; tenant and structure/value authorization on every read; no new EF persistence work

**Scale/Scope**: First safe vertical slice supports one graph provider, exact nested reusable activity placement, one mapped output, natural `Done`, native bookmark suspension, full restart/resume, and hierarchical inspection. The backend model also fixes versioning, dependencies/upgrades, lifecycle, preflight, and Elsa 3 conversion contracts; Studio UX and additional providers are deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

The Elsa and framework constitutions are still draft/provisional. This plan treats their current hard rules as binding. A later ratification change that contradicts these contracts requires explicit replanning rather than silent drift.

| Gate | Status | Plan evidence |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Design/provider contracts remain in domain Core seams; graph Design and Runtime implementations are separate feature projects; Publishing is the bridge. |
| Framework §2.2 and Elsa §E6 naming | PASS | Proposed names stay domain-led, avoid vague suffixes, and remain within the component budget. Durable wire keys are explicitly exempt from CLR renames. |
| Framework §2.5 replaceable registrations | PASS with implementation work | New feature registration methods remain virtual; logic collaborators register/inject contracts. |
| Framework §2.6.1 / §2.24 sanctioned composition | PASS | Provider/consumer lookup uses the sanctioned Strategy plus Registry + StartUp Task pattern; contribution collection remains event-driven. No ad-hoc dispatcher is introduced. |
| Framework §2.6.4 Design/Runtime contract split | PASS | Provider manifest/compiler contracts and Runtime consumer/descriptor contracts are separate; Publishing alone bridges them. |
| Framework §2.9/§2.10 persistence neutrality and CQS | PASS | Core declares entities, invariants, and separate read/write ports; Groundwork supplies the durable implementation without leaking into Core. |
| Framework §2.21/§2.23 test preservation and branch coverage | PASS with implementation work | Existing behavior tests remain; all new logic-bearing classes and feature registration paths require focused tests. No existing test is approved for deletion by this plan. |
| Framework §2.22 feature/extension documentation | PASS with implementation work | Graph Design/Runtime, Activity Design, Publishing, Runtime, and Elsa 3 feature docs/extension catalogs must record registrations, events, providers, and tasks. |
| Elsa §E2.2 Design/Runtime split | PASS | Runtime graph consumer and execution path reference only Runtime/Core contracts. Architecture guards explicitly ratchet the boundary. |
| Elsa §E2.2.3 deployment shapes | PASS | Authoring/publishing and Runtime consumers can be composed separately; retained executable artifacts do not require Design stores. |
| Elsa §E2.6.1 executable-always-runs | PASS with operational guard | Publication records exact Runtime requirements; deployment preflight prevents incompatible hosts. A missing consumer is treated as a deployment/system incident and contract violation, not normal retryable activity behavior. |
| Elsa §E2.6.2 artifact-only Runtime | PASS | Templates and workflow artifacts contain closed Runtime execution material; Runtime never reloads drafts, versions, provider manifests, or current layout. |
| Elsa §E2.7 Elsa 3 import-only compatibility | PASS | Compatibility is one-way collection-aware plan/apply import. Foundation workflow-as-activity compatibility is intentionally absent. |
| Elsa §E2.8 Activity Catalog authority | PASS, constitution follow-up noted | Every reusable activity is a catalog definition/version. This feature replaces CLR `DescriptorType` durability with stable Design provider and Runtime consumer keys; the provisional descriptor wording should be reconciled during implementation documentation/constitution review, without weakening catalog authority. |
| Elsa §E2.9 authored/read/executable separation | PASS | Activity draft state, API read models, executable templates, workflow executables, Runtime state, and inspection projections are distinct. Workflow state is not reused for activity definitions. |
| Accepted ADRs 0038/0039/0040 | PASS | Behavior-only content hashes, hierarchical layout on Source References, and one artifact store/reference-derived lifetime remain intact. |
| Accepted ADR 0042 / Zero-EF direction | PASS | New durability targets Groundwork only; no EF schema or migration is added. |

Initial and post-design gate status: **PASS**. The only follow-up is aligning provisional constitution/glossary wording that still describes CLR-type-based descriptor identity; it is not permission for a Runtime -> Design dependency or a second catalog.

## Project Structure

### Documentation (this feature)

```text
specs/092-reusable-activity-definitions/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   ├── plan-review.md
│   └── requirements.md
└── contracts/
    ├── authoring-api.md
    ├── dependencies-and-upgrades.md
    ├── provider-runtime-seams.md
    ├── runtime-inspection.md
    ├── validation-errors.md
    └── version-diff.md
```

### Source Code (repository root)

```text
src/Elsa/Activities/Design/Core/
├── Contracts/                 # provider registry/strategy, diff, validation, admission contracts
└── Models/                    # contract, manifest, draft/version, diagnostic/diff/dependency models

src/Elsa/Activities/Design/Persistence/Core/
├── Entities/                  # definition, draft, version, layout, validation, direct edge
└── Stores/                    # provider-neutral read/write ports

src/Elsa/Activities/Design/Persistence/Groundwork/
├── Services/                  # durable stores/commands
└── ActivitiesDesignStorageManifest.cs

src/Elsa/Activities/Design/Api/
├── Commands/
├── Requests/
├── Handlers/
├── Models/
└── Endpoints/                 # definition/draft/version/diff/dependency/upgrade/lifecycle routes

src/Elsa/Activities/Graph/Design/
├── Models/                    # graph provider manifest schema
├── Services/                  # propose, validate, compile, migrate
└── EXTENSION_POINTS.md

src/Elsa/Activities/Graph/Runtime/
├── Activities/GraphActivity.cs
├── Constructors/
├── Services/                  # scope/boundary helpers over Runtime contracts
└── EXTENSION_POINTS.md

src/Elsa/Activities/Runtime/Core/
├── Contracts/                 # stable-key constructor/consumer and admission contracts
├── Exceptions/                # domain-scoped activation failures
└── Models/                    # RuntimeActivityDescriptor

src/Elsa/Workflows/Publishing/Api/
├── Services/                  # activity publish coordinator, template store/hasher/placer, preflight
├── Models/                    # publication/test-run/preflight views
└── Endpoints/                 # activity draft publish/test-run/preflight bridge routes as allocated

src/Elsa/Workflows/Runtime/Core/
├── Models/                    # executable template, Source Reference hierarchical layout, scope/attempt facts
├── Contracts/                 # template/source/reference/inspection stores
└── Services/                  # iterative placement/runtime requirement resolution contracts

src/Elsa/Workflows/Runtime/Api/
├── Models/WorkflowExecutionViews.cs
├── Requests/
├── Handlers/
└── Endpoints/                 # descendant hierarchy and pinned layout

src/Elsa/Persistence/Groundwork/
├── Stores/                    # Runtime template/hierarchy/read projections as needed
└── ElsaRuntimeStorageManifest.cs

src/Elsa3/Activities/Design/Import/
└── reusable-activity collection plan/apply contracts and implementation

tests/Elsa/Activities/Design/
tests/Elsa/Activities/Graph/
tests/Elsa/Activities/Runtime/
tests/Elsa/Workflows/Publishing/
tests/Elsa/Workflows/Runtime/
tests/Elsa/Persistence/Groundwork/
tests/Elsa3/Mapping/
tests/Elsa/Architecture/
```

**Structure Decision**: Replace the narrow `Elsa.Activities.Composition.Design/Runtime` workflow-backed pair with explicit `Elsa.Activities.Graph.Design/Runtime` provider/consumer projects. Their separation is a real Design/Runtime capability boundary (framework §2.16.1 exemption), not a layer-marker bucket: Graph Design owns authored graph provider behavior; Graph Runtime owns the executable composite. General draft/version/API/persistence contracts stay in existing Activity Design packages, and workflow artifact/runtime concerns stay in existing Workflows packages.

## Design Decisions

1. **Canonical terms**: `ActivityDefinition`, `ActivityDefinitionDraft`, `ActivityGraph`, `GraphActivity`, `ExecutableActivityTemplate`, `activity execution scope`, and `Runtime consumer`. Remove Runtime-level “custom activity,” “workflow as activity,” “child workflow,” and special “invocation scope” vocabulary.
2. **Three-shape split**: public `ActivityContract`, opaque Design `ActivityProviderManifest`, and Runtime `RuntimeActivityDescriptor` are independent contracts with stable keys/schemas.
3. **Full-state drafts**: Activity draft mutation is complete desired state + layout with `ExpectedRevision`, mirroring the successful workflow draft command style while retaining a separate aggregate.
4. **One content authority**: source-owned lineages reconcile through trusted commands and are read-only through authoring APIs; customization forks to a new Design-owned identity.
5. **Atomic publication**: expected draft revision + expected definition head guard one transaction that creates template/reference/version/direct edges and advances head.
6. **Platform version diff**: public contract and behavior changes yield structured changes and a minimum SemVer; provider rules can only strengthen it.
7. **Caller-side defaults**: literal and expression defaults compile into consuming workflow bindings and are evaluated/captured once at activity entry.
8. **Template closure**: activity versions point to deterministic behavior-only templates with exact dependencies and Runtime requirements; workflow publication places templates rather than recompiling source.
9. **Deterministic placement**: full SHA-256 of a length-framed invocation origin namespaces nodes, resume targets, and layout boundary segments; readable provenance is separate.
10. **No arbitrary limits**: iterative traversal emits measurements; a replaceable admission policy decides for host/tenant context.
11. **Ordinary Runtime scope**: the outer activity execution owns durable inputs, local variables, outputs, attempt lineage, and boundary lifecycle; no invocation entity/blob is introduced.
12. **Checkpointed boundary**: entry and exit are atomic scheduler-boundary checkpoints; descendants/bookmarks/incidents remain native Runtime records.
13. **Fresh retry**: retry creates a new execution scope and descendants while retaining exact template/effective captured inputs and linking first/previous attempts.
14. **Hierarchical inspection**: extend spec-079 detail, add scope-rooted cursor pages, and lazy pinned layout; nested boundaries recurse by click-through.
15. **Dependency truth**: immutable direct edges are authoritative; incoming/transitive/draft views expose projection watermark and never drive execution.
16. **Lifecycle split**: retirement affects new selection; revocation is stronger; neither mutates closed templates or historical evidence.
17. **Groundwork-only new durability**: add provider-neutral contracts plus Groundwork/in-memory implementations; do not expand EF.
18. **Clean break**: remove `WorkflowDefinitionActivity`, `UsableAsActivity`, workflow-backed catalog reconciliation, and CLR-full-name durable descriptor dispatch after the replacement vertical slice is green. Retain explicit `ExecuteWorkflow`.

## Initial Delivery Plan

This is sequencing, not a generated `tasks.md`. Each slice must preserve a runnable build and can be decomposed into tasks after this plan is accepted.

### Slice A - Contract and catalog foundation

- Add draft/version/public-contract/provider-manifest models, content authority, diagnostics, version diff, direct dependency facts, and provider-neutral stores.
- Evolve durable Runtime construction identity from CLR descriptor type name to stable consumer key/schema.
- Add API read/write contracts and Problem Details diagnostic mapping.
- Land in-memory conformance tests and architecture/naming ratchets before behavior.

Exit: a Design-owned graph draft can be created, updated under revision, validated, and diffed; a source-owned definition rejects authoring mutation.

### Slice B - Atomic graph publication

- Add graph provider schema 1, contract fidelity validation, exact dependency resolution/cycle detection, deterministic compiler, template hasher/store, Source Reference hierarchical layout, and publication coordinator.
- Persist version/template/reference/direct edges/head atomically in Groundwork.
- Enforce expected head/revision and SemVer; expose version/dependency reads.

Exit: a valid graph draft publishes one immutable exact version/template; failures at every phase expose structured diagnostics and zero partial state.

### Slice C - Workflow placement and minimal Runtime execution

- Teach the workflow compiler to resolve exact activity version templates, expand them iteratively, namespace nodes/resume targets/layout segments, and preserve an explicit outer `GraphActivity` node.
- Add the Runtime graph consumer and entry/exit checkpoint behavior with isolated durable inputs/local variables, one output mapping, and natural `Done`.
- Route all work through existing activity pipeline/scheduler/checkpoint/post-commit contracts.

Exit: a published workflow executes one graph-backed activity synchronously to completion inside one workflow execution and propagates one durable output exactly once.

### Slice D - Suspension, recovery, faults, cancellation, and retry

- Exercise a native descendant bookmark, complete host teardown, exact resume, required-output/capture failures, causal boundary incidents, fenced cancellation cleanup, and fresh retry attempt lineage.
- Add Runtime requirement preflight and artifact-activation incident classification.

Exit: the mandatory Groundwork SQLite restart gate passes with no Design store available; race tests have one committed winner and no leaked descendant work.

### Slice E - Hierarchical inspection and operational read models

- Extend activity execution detail with boundary/attempt facts.
- Add bounded hierarchy pages, cursor binding/watermark behavior, derived aggregate, and separate executed-reference layout read.
- Add structure/value authorization and redaction tests on every page/expansion.

Exit: operators can click through nested boundaries, loops, and retries with stable historical layout and bounded responses.

### Slice F - Lifecycle, upgrades, and draft test runs

- Add retire/restore/revoke policy, reverse/transitive dependency projections, upgrade plan/apply, provider manifest clone/migration, synthetic wrapper activity test runs, and API contract tests.
- Keep multi-stage bottom-up upgrades explicit when a parent cannot select a child until the child draft publishes.

Exit: selected dependency-closed draft edits apply atomically under pinned revisions/heads; published artifacts remain unchanged.

### Slice G - Elsa 3 conversion and legacy removal

- Add collection-aware analysis/apply fixtures, deterministic ids, wrapper workflow generation, exact rewrites, atomic selected closure, and cycle/missing/unsupported diagnostics.
- Remove the Foundation workflow-as-activity marker/activity/reconciliation/catalog surface and rename/refine remaining terminology/docs.
- Retain and test explicit separate-workflow execution.

Exit: repository search and architecture guards show one reusable-activity model, Elsa 3 import is deterministic/idempotent, and all release gates pass.

## Validation Strategy

### Mandatory black-box gate

1. Create and publish an activity definition with one public input, one graph-local value, one required output mapping, and an internal suspending activity.
2. Publish a workflow that places the exact activity version twice to prove namespacing.
3. Start one workflow execution and suspend inside one placed graph boundary.
4. Dispose the entire host and all in-memory services while retaining only Groundwork SQLite state.
5. Start a fresh host without Activity/Workflow Design stores, resume the exact descendant bookmark, and complete.
6. Assert one workflow execution id, visible outer boundaries, deterministic distinct descendants/resume targets, one-time input capture, no replay, exactly-once boundary output propagation, atomic completion, and pinned layout.
7. Page and click through the completed hierarchy with structure-only permission, then with value permission.

This scenario and the Runtime -> Design architecture guard are release gates; lower-level tests cannot replace them.

### Focused coverage

- **Authoring/API**: authority, revisions, full-state update, contract proposals, validation ordering, RFC 7807 statuses, payload disclosure.
- **Publication**: stale head/revision, atomic failpoints, SemVer matrix, exact dependency cycles, tenant rules, provider determinism/fingerprint, runtime requirements, layout reference.
- **Compiler**: repeated placement, full-hash collision rejection, subtree-local identity changes, unrelated-subtree stability, iterative deep traversal, resume-target namespace, behavior-only hashes.
- **Runtime**: entry/exit atomicity, absent/null/present defaults, capture failure, required output, native bookmark, inner/outer incident causation, cancellation/resume orderings, fresh retry, activation incident.
- **Inspection**: snapshot watermark, page continuation, cursor mismatch/expiry, nested boundaries, loop/retry grouping, aggregate status, layout choice, authorization/redaction.
- **Dependencies/upgrades**: direct-edge authority, projection rebuild/lag, lifecycle, dependency-closed selection, stale plan, bottom-up staging, no published mutation.
- **Migration**: exact references, wrapper direct starts, missing dependencies, unsupported triggers, deterministic rerun, atomic closure, version-level cycles.
- **Provider conformance**: determinism, exact dependencies, contract fidelity, atomic failure, manifest round-trip/migration, Runtime consumer declaration, no Runtime -> Design dependency. Extract a public harness only when a second provider exists.

### Required verification commands after implementation

```bash
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj -c Release
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj -c Release
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -c Release
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj -c Release
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release
dotnet build Elsa.Server.slnx -c Release
```

New graph-provider/import test project paths are added to this list when their projects are created; `quickstart.md` owns the plan-stage validation walkthrough.

## Frontend Grilling Handoff

The right time to grill the Studio/frontend design is **after this spec/plan is accepted and before Slice A API models or Slice E inspection endpoints are frozen in code**. The frontend session should treat runtime identity, exact pinning, atomic publication, and durability semantics as fixed, while challenging:

- definition/draft/version navigation and parallel-draft visibility;
- contract editor states for absent/null/present values and expression defaults;
- validation location rendering and provider-specific source focus;
- version-diff grouping and minimum-bump explanation;
- dependency graph, lifecycle badges, and multi-stage upgrade-plan review;
- click-through runtime hierarchy, retries/loops, aggregate versus outer status, cursors, layout fallback, and redaction;
- test-run status and comparison with published behavior.

Feedback that changes wire/read-model usability can still amend the contracts at that point without reopening Runtime execution semantics.

## Complexity Tracking

No constitutional violations are required. The two graph projects are justified by the explicit Design/Runtime deployment and dependency boundary and qualify as independently composable feature/cross-domain seams under framework §2.16.1.
