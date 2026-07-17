# Implementation Plan: Deterministic and Bounded Workflow Dispatch

**Branch**: `codex/dispatch-677` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/097-dispatch-dependency-hardening/spec.md`

## Summary

Harden `DispatchWorkflow` so a published parent carries a canonical, content-addressed dependency on the exact child executable, validates child inputs against an immutable runtime input contract, retains the complete executable closure, starts retained pins without a live historical child publication, and bounds recursive dispatch with durable depth lineage. The implementation extends the existing artifact, publication, start-dispatch, checkpoint, and garbage-collection seams; it does not add WorkflowDefinitionActivity behavior, Studio UI, a broker transport, waited completion, or distributed placement.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: Existing Elsa Workflows Publishing/Runtime Core and Api modules, DispatchWorkflow Design/Runtime modules, Elsa Events contribution pipeline, Microsoft.Extensions dependency injection/options

**Storage**: Existing in-memory runtime stores plus the Groundwork document-backed `WorkflowExecutable` store; dependency edges and declared-input contracts are embedded immutable artifact data

**Testing**: xUnit project tests, architecture guards, Groundwork provider/conformance tests, compiler golden tests, focused end-to-end DispatchWorkflow tests

**Target Platform**: Cross-platform .NET server/runtime hosts

**Project Type**: Modular library/runtime feature set

**Performance Goals**: Canonical hashing remains linear in executable nodes plus direct dependencies and declared inputs; dependency closure traversal is O(V+E), de-duplicates diamonds, and uses one store snapshot per GC sweep; start-time input/depth/policy validation is linear in supplied inputs and otherwise constant

**Constraints**: Artifact-only runtime; no Runtime-to-Design reference; behavioral hashes exclude per-publication facts; old serialized artifacts/start payloads remain readable but legacy artifacts are ineligible as new strict dispatch targets; publication access is current tenant scope; default maximum dispatch depth is 32; provider-neutral retention safety must hold under concurrent publication, execution-root creation, and collection

**Scale/Scope**: One work unit spanning Publishing, Runtime, DispatchWorkflow, in-memory and Groundwork persistence, documentation, ADRs, maps, and tests; no new project and no Studio repository changes

## Constitution Check

*GATE: Passed before Phase 0 and re-checked after Phase 1.*

- **Draft status acknowledged**: both constitutions are draft/provisional. This plan applies their current gates and records artifact/hash/lifetime changes in ADR amendments rather than silently treating the decisions as final.
- **Three-layer/domain ownership (§2.1, Elsa §E2.1)**: runtime artifact/input/dependency/start contracts remain in `Elsa.Workflows.Runtime.Core`; compilation orchestration remains in Publishing; DispatchWorkflow-specific resolution and validation remain in its Design/Runtime modules. No implementation-to-unrelated-implementation reference is introduced.
- **Design/Runtime split and artifact-only runtime (Elsa §E2.2, §E2.6)**: runtime consumes only `WorkflowExecutable`, typed start/checkpoint payloads, and runtime-owned source/retention records. It never loads `WorkflowDefinitionState` or references `Elsa.Workflows.Design.*`.
- **Behavioral artifact identity (ADR 0038 / Elsa §E2.6)**: only canonical declared-input behavior and canonical exact child artifact identities enter the parent hash. Live reference IDs, publication IDs, timestamps, access context, and retirement facts remain outside the artifact hash.
- **Contribution topology (§2.6.1)**: compile-time cross-feature fan-in uses a named Sequential event with one Publishing-owned aggregating handler and typed sources; DispatchWorkflow registers a source, not a peer handler. Existing metadata contribution is evolved without introducing an anonymous mediator dependency.
- **Replacement contract (§2.6.2)**: `IWorkflowExecutableStartPolicy` is explicitly documented as single-implementation/replacement. The default permits starts; hosts may replace it to return a typed deny decision. No `IEnumerable<T>` policy chain or silent last-write-wins behavior is introduced.
- **Naming (§E6)**: new types stay within the four-component budget and use `Source`, `Validator`, `Policy`, `Decision`, `Dependency`, and `Contract` according to role. Logic-bearing implementations are public sealed; feature classes remain public and unsealed.
- **Persistence/provider neutrality (§2.9)**: dependency/input invariants are defined on the runtime model. In-memory and Groundwork stores persist the same immutable artifact shape; no provider-specific model leaks into Core.
- **CQS (§2.10)**: existing read/write store operations remain separated. No combined query/mutation contract is added.
- **Test continuity (§2.21.1)**: existing compiler, publication, dispatch, checkpoint, retention, and Groundwork tests remain. Public compatibility overloads and legacy JSON defaults preserve existing test objectives.
- **Test obligations (§2.23)**: every new logic-bearing implementation receives branch-complete focused tests; every changed feature registration is covered. Infrastructure serialization/store exceptions are wrapped at feature boundaries where applicable.
- **Documentation (§2.22)**: changed contributor/replacement contracts update the owning `EXTENSION_POINTS.md`, feature READMEs, root index if necessary, ADR 0038, ADR 0040, and generated maps in the same work unit.
- **Sanctioned patterns (§2.24)**: named event contribution, replacement contract, CQS persistence, and existing provider decomposition cover the design. No uncatalogued structural pattern is required.
- **Scope guard**: no WorkflowDefinitionActivity, Studio, wait/resume lifecycle, cancellation, redrive, test-scope dispatch, MassTransit, broker selector, or distributed-placement implementation.

## Project Structure

### Documentation (this feature)

```text
specs/097-dispatch-dependency-hardening/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── publication-contract.md
│   └── runtime-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Core/
├── Configuration/                 # dispatch-depth options
├── Contracts/                     # start policy and existing stores/lease manager
├── Exceptions/                    # classifiable validation/start rejections
└── Models/                        # executable dependencies/input contract and depth lineage

src/Elsa/Workflows/Runtime/
├── Services/                      # dispatcher policy/authority checks, closure GC and leases
└── EXTENSION_POINTS.md

src/Elsa/Workflows/Publishing/Core/
├── Contracts/                     # compilation contribution source
├── Events/                        # named Sequential fan-in event
└── Models/                        # compilation contribution/context

src/Elsa/Workflows/Publishing/Api/
├── Handlers/                      # single aggregation handler
└── Services/                      # compiler, hasher, dependency closure validation

src/Elsa/Activities/DispatchWorkflow/Design/
└── Services/                      # exact dependency/input revalidation source

src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Models/
└── Services/                      # dynamic input/depth validation and retained child start

src/Elsa/Persistence/Groundwork/
└── Stores/                        # durable artifact/lease behavior as needed

tests/Elsa/
├── Workflows/Publishing/Api/Tests/
├── Workflows/Runtime/Tests/
├── Activities/DispatchWorkflow/Tests/
├── Persistence/Groundwork/Tests/
└── Architecture/

docs/adr/
├── 0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md
└── 0040-one-artifact-store-with-reference-derived-lifetime.md
```

**Structure Decision**: Extend the existing Publishing, Runtime, DispatchWorkflow, and Groundwork projects because each concern already has an owning domain and package. No new package is warranted. Runtime-facing declared inputs are projected into the executable at compile time; no Design dependency crosses into Runtime.

## Delivery Sequence

1. Add characterization tests for current hash, input-channel, provenance, GC, and dispatch-depth compatibility behavior.
2. Add immutable declared-input and executable-dependency models, canonical hashing, serialization defaults, compiler goldens, and inspection projections.
3. Evolve compile contribution into the constitutional named-event/single-aggregator shape and make DispatchWorkflow resolve one exact live Published child, validate static inputs, and contribute a canonical dependency.
4. Add full ID/hash dependency-graph integrity validation, malformed exact-cycle diagnostics, and persisted retained-dependency provenance.
5. Make GC and root-write leases traverse the transitive closure with deterministic acquisition order and conservative failure behavior in memory and Groundwork.
6. Add the replacement start policy and classifiable pre-materialization denial.
7. Thread dispatch nesting depth through request, command, checkpoint, state, record, and child-start payloads; enforce both before checkpoint staging and at start dispatch.
8. Add end-to-end replacement/unpublication, input, retention, recursion, replay, policy, and boundary-depth tests.
9. Update ADRs, READMEs, extension-point catalogs, compatibility snapshots, architecture guards, and generated maps; run focused, domain, provider, architecture, and full-solution gates.

## Post-Design Constitution Re-check

Phase 1 artifacts preserve every pre-design gate:

- The runtime contract is self-contained and Design-free.
- Ephemeral publication facts are excluded from behavioral hashing.
- Dependency contribution uses the sanctioned named-event fan-in pattern.
- Host-specific denial is one explicit replacement contract.
- Retention safety is provider-neutral and fail-closed.
- Compatibility behavior is explicit: legacy reads/runs remain valid, while new strict parent publication requires upgraded child artifacts.
- Scope exclusions are repeated in contracts and quickstart validation.

No constitutional exception or new pattern is required.

## Complexity Tracking

No constitution violations require justification.
