<!--
Draft history moved to ../../docs/reports/constitution-draft-history.md.
This constitution file is the Elsa-specific quality-gate layer: gates, allowed exceptions,
ratification state, and governance. Canonical term lookup lives in ../../docs/glossary/.

Sync Impact Report (3.4.0 -> 3.5.0, 2026-08-07)
- Bump rationale: MINOR. Materially expanded Elsa-specific guidance; no rule removed or redefined
  incompatibly.
- Modified: §E5, retitled "Elsa packaging snapshot" to "Elsa packaging and versioning". Adds the
  workbench/product distinction, the two version lines, computed versions with selective publishing,
  magnitude enforcement, and Line A stability policy. Rewrites the Nuplane strategy paragraph, which
  named the removed `Elsa.Server` project and claimed the host pins all `.Core` libraries.
- Modified: §E2.4, "the host" clarified to "the workbench host" following the Elsa.Server rename.
- Modified: Pinned root, "Application instance = `Elsa.Server`" corrected to name `Elsa.Workbench`
  and note that the product host is not yet built.
- Source: ADR 0067, agreed on issue #1144 by Joey Barten, Sipke Schoorstra and Frans van Ek.
- Open: Line A membership is partial. Six candidate packages are deferred to the clean host
  specification (#1145).
- Templates requiring review: plan-template.md, spec-template.md, tasks-template.md for any
  Constitution Check text that assumes uniform package versions.

SYNC IMPACT REPORT — 3.5.0 -> 4.0.0 (2026-08-08)
Amendment: tracks framework constitution 4.0.0 — event types are never named with an `On` prefix.
  MAJOR because this document normatively PINS event names that are now redefined
  (§2.9 specialization and §E5 lifecycle/validation events), not merely illustrated.
Modified sections:
  §2.9 specialization - EF Core save/load event mirrors renamed
                        `OnEntitySaving`/`OnEntityLoading` -> `EntitySaving`/`EntityLoading`.
  §E2.9.7 (draft-mutation surface) - `OnDraftCreated`/`OnDraftDiscarded` -> `DraftCreated`/`DraftDiscarded`;
                        `OnDraftValidating`/`OnDraftValidated` -> `DraftValidating`/`DraftValidated`;
                        the dropped `OnDraftClonedFromVersion` reference follows the same shape.
  §E6 - ADDED an R4 cross-reference to the framework event-naming rule so the type-name gate
        surfaces it. Note the rename also brings the longest Draft-mutation event names back
        inside the R1 component budget (`OnActivityOutputRemovedFromDraft` was 6 components,
        over the hard cap of 5; `ActivityOutputRemovedFromDraft` is 5).
Added sections: none.
Removed sections: none.
Derives-from pointer: framework v3.2.0 -> v4.0.0.
Templates requiring updates: none (see framework report).
Ratification: RATIFIED 2026-08-08 by Sipke Schoorstra, on his authority alone; Joey Barten and Frans van Ek
  did not sign off, so this is open to revision if they dissent.
-->
# Elsa Workflow Engine Constitution

**Version:** 4.0.0
**Status:** Ratified 2026-08-08 by Sipke Schoorstra. Governance > Amendment process calls for consensus among Joey Barten, Sipke Schoorstra, and Frans van Ek; this ratification was taken on Sipke Schoorstra's authority alone and is open to revision if the other architects dissent. Section-level gates still marked draft, provisional, or pending architecture-review ratification — whether via their own `Status:` line (§E5) or inline wording (§E2.8 Model X, §E2.9, §E2.9.7) — remain so and are **not** covered by this ratification.
**Layer:** Elsa-specific specialization of the [Modular Software Design Framework Constitution](constitution-framework.md).
**Derives from:** framework constitution **v4.0.0**.

**Knowledge boundary note:** treat this document as the Elsa-specific
quality-gate layer. Canonical term lookup lives in `../../docs/glossary/`;
current findings and inventory live in `../../docs/reports/`. Planned work
routes through the shared program-goals planner.

---

## Table of Contents

- [Derivation](#derivation)
- [Glossary — Elsa specializations](#glossary--elsa-specializations)
- [§E1 Refactor baseline](#e1-refactor-baseline)
- [§E2 Elsa domain decomposition](#e2-elsa-domain-decomposition)
  - [§E2.1 The Elsa domain tree](#e21-the-elsa-domain-tree)
  - [§E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split](#e22-workflowsdesign--workflowsruntime-bounded-context-split)
    - [§E2.2.1 Design sub-domain](#e221-design-sub-domain--the-designed-contract) · [§E2.2.2 Runtime sub-domain](#e222-runtime-sub-domain--the-runtime-representation) · [§E2.2.3 Deployment-shape gate](#e223-deployment-shape-gate)
  - [§E2.3 `Elsa.Primitives` charter](#e23-elsaprimitives-charter)
  - [§E2.4 Elsa foundation repo composition](#e24-elsa-foundation-repo-composition)
  - [§E2.5 `ElsaDbContextBase` — opt-in capability](#e25-elsadbcontextbase--opt-in-capability-not-requirement)
  - [§E2.6 Runtime contract — executable-always-runs and artifact-only design](#e26-runtime-contract--executable-always-runs-and-artifact-only-design) · [§E2.6.1 Executable-always-runs](#e261-executable-always-runs) · [§E2.6.2 Artifact-only runtime](#e262-artifact-only-runtime)
  - [§E2.7 Elsa 3 backward compatibility — import-only](#e27-elsa-3-backward-compatibility--import-only)
- [§E3 Elsa-specific worked example references](#e3-elsa-specific-worked-example-references)
- [§E4 Elsa configuration — \[DEFERRED\]](#e4-elsa-configuration--deferred)
- [§E5 Elsa packaging snapshot](#e5-elsa-packaging-snapshot)
- [§E6 Type-naming rules](#e6-type-naming-rules)
- [Governance](#governance)

---

## Derivation

This document is the **Elsa-specific** layer of a two-layer constitution. It is read alongside `constitution-framework.md`, which carries the framework-neutral rules.

**Rules of derivation.**

- All rules in the framework constitution apply to Elsa by reference.
- Where Elsa specializes or overrides a framework rule, it does so explicitly with the convention **`framework §X — Elsa specialization: …`**.
- Where this document is silent, the framework constitution applies.
- This document pins Elsa's **root domain name**, **concrete domain decomposition**, **foundation repo composition**, and Elsa-specific architectural rules that have no framework-level analog (notably the Workflows.Design ↔ Workflows.Runtime bounded-context split, §E2.2).

**Pinned root.**

- `<App>` = `Elsa`. Every framework-level token of the form `<App>.<Domain>` resolves to `Elsa.<Domain>` in this constitution.
- Application instance = the host project. Today that is `Elsa.Workbench`, a development and demo host; the product host is not yet built (§E5).
- Foundation repo = `github.com/elsa-workflows/elsa-foundation` (created 2026-05-08).

---

## Glossary — Elsa specializations

Canonical framework terms live in [docs/glossary/root.md](../../docs/glossary/root.md). Canonical Elsa-specific terms and bindings live in [docs/glossary/elsa.md](../../docs/glossary/elsa.md).

This constitution uses those terms as gate vocabulary. Elsa-specific bindings that matter to interpretation are also pinned in §E2, especially the root domain, application host, foundation repository, and `Elsa.Primitives` charter.

---

## §E1 Refactor baseline

The historical `elsa-core` case study lives in
[docs/reference/elsa-worked-examples.md](../../docs/reference/elsa-worked-examples.md#elsa-core-baseline-case-study).
The Elsa refactor replaces those failure modes with the rules in framework §2
and the Elsa-specific decomposition in §E2.

**Refactor work in this constitution's scope is governed by framework §2.21.1** — the golden rule of refactoring. Existing tests on the implementations being refactored MUST continue to succeed across the reorganization; the *subject under test* and *objective* are preserved even when test setup, dependencies, or location change. Removing a test requires explicit recorded approval from at least one architect (unanimity reserved for constitutional amendments).

---

## §E2 Elsa domain decomposition

### §E2.1 The Elsa domain tree

Applying framework §2.18's methodology to Elsa, the root-level domains are
*(table refreshed 2026-07-02 to match the shipped tree — MD-4, Elsa 4
architecture review; the generated [domain map](../../docs/maps/domain-map.md)
owns always-fresh enumeration and project counts)*:

| Domain | Purpose (one verb-led sentence) | Surface package(s) |
|---|---|---|
| `Elsa.Activities` | Provides the activity system: design-time activity definitions, reconciliation, composition, runtime activity handling, and the built-in activity libraries. | `Elsa.Activities.Design.*`, `Elsa.Activities.Runtime{,.Core}`, `Elsa.Activities.Composition.*`, `Elsa.Activities.{ControlFlow,Flowchart,Primitives,Sequence,Testing}` |
| `Elsa.Agent` | Hosts AI-agent sessions and capabilities behind provider modules. | `Elsa.Agent.Core`, `Elsa.Agent.Api`, `Elsa.Agent.{Anthropic,GitHubCopilot,Workflows}` |
| `Elsa.Api` | Exposes application APIs through endpoint-framework adapters. | `Elsa.Api.FastEndpoints` |
| `Elsa.Caching` | Provides caching contracts and providers. | `Elsa.Caching.Core`, `Elsa.Caching.Memory` |
| `Elsa.Diagnostics` | Streams and persists diagnostics: console logs, structured logs, and OpenTelemetry ingestion. | `Elsa.Diagnostics.ConsoleLogStreaming`, `Elsa.Diagnostics.StructuredLogs{,.*}`, `Elsa.Diagnostics.OpenTelemetry{,.*}` |
| `Elsa.Events` | Publishes and delivers in-process events (framework §2.6 unified event model). | `Elsa.Events.Core`, `Elsa.Events`, `Elsa.Events.Strategies` |
| `Elsa.Expressions` | Evaluates expressions inside workflow steps. | `Elsa.Expressions.Core`, `Elsa.Expressions`, `Elsa.Expressions.JavaScript.*` |
| `Elsa.Foundation` | Provides application-foundation services such as identity. | `Elsa.Foundation.Identity.*` |
| `Elsa.Http` | Provides HTTP content, routing, download, and cache behavior. | `Elsa.Http.Core`, `Elsa.Http`, `Elsa.Http.JavaScript` |
| `Elsa.Locking` | Provides distributed locking. | `Elsa.Locking.Core`, `Elsa.Locking.FileSystem`, `Elsa.Locking.<Provider>` |
| `Elsa.Mediator` | Routes commands and requests in-process. | `Elsa.Mediator.Core`, `Elsa.Mediator` |
| `Elsa.Modularity` | Discovers, describes, enables, validates, and composes modules and features. | `Elsa.Modularity.Core`, `Elsa.Modularity.Api`, `Elsa.Modularity.Nuplane` |
| `Elsa.Persistence` | Persists application state (generic CQS-style commands and queries). | `Elsa.Persistence.Core`, `Elsa.Persistence.EFCore{,.Sqlite}`, `Elsa.Persistence.Groundwork.*` |
| `Elsa.Pipelines` | Defines pipeline contracts for composable middleware. | `Elsa.Pipelines.Core` |
| `Elsa.Primitives` | Provides dependency-free base primitives and hosting seams (charter in §E2.3). | `Elsa.Primitives`, `Elsa.Primitives.Hosting` |
| `Elsa.Secrets` | Stores and resolves secrets. | `Elsa.Secrets.Core`, `Elsa.Secrets`, `Elsa.Secrets.Api`, `Elsa.Secrets.Persistence.Groundwork` |
| `Elsa.Serialization` | Serialises payloads and workflow models. | `Elsa.Serialization.Core`, `Elsa.Serialization.Newtonsoft`, `Elsa.Serialization.SystemText` |
| `Elsa.Tasks` | Schedules background work inside the host. | `Elsa.Tasks.Core`, `Elsa.Tasks`, `Elsa.Tasks.Schedules` (helper) |
| `Elsa.Workflows.Design` | Designs workflow definitions: contracts, models, validations, reconciliation, and design-time persistence. | `Elsa.Workflows.Design.Core`, `Elsa.Workflows.Design.{Api,JavaScript}`, `Elsa.Workflows.Design.{Reconciliation,Validations}.*`, `Elsa.Workflows.Design.Persistence.*` |
| `Elsa.Workflows.Runtime` | Executes workflows: instances, execution pipeline, bookmarks, runtime persistence. | `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Runtime.{Api,Http,JavaScript}` |
| `Elsa.Workflows.Publishing` | Publishes executable workflow artifacts from designed definitions. | `Elsa.Workflows.Publishing.Api`, `Elsa.Workflows.Publishing.Api.Core` |
| `Elsa.Workflows.Primitives` | Shares workflow primitives used by both Design and Runtime. | `Elsa.Workflows.Primitives` |
| `Elsa3` | Imports Elsa 3 definitions one-way at the migration boundary (§E2.7). | `Elsa3.Models`, `Elsa3.Mapping`, `Elsa3.Activities.Design.Import` |

Three domains listed in earlier drafts — `Elsa.Scheduling`, `Elsa.Messaging`,
and `Elsa.Notifications` — do not exist in code and are removed from the pinned
tree; they may return as planned domains when work starts. In-process pub/sub
(formerly `Elsa.Notifications`) shipped as `Elsa.Events` under the framework
§2.6 unified event model.

Sub-domain decomposition follows framework §2.1's naming convention. Variation suffixes are added only when a domain hosts more than one implementation (e.g. `Elsa.Serialization.Newtonsoft` vs `Elsa.Serialization.SystemText`) or when a single implementation already implies a variation choice (e.g. `Elsa.Locking.FileSystem`).

### §E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split

**framework §2.18 — Elsa specialization:** `Elsa.Workflows.*` is split into **two dedicated sub-domains with separate persistence layers**: `.Design.*` (designs and persists workflow definitions) and `.Runtime.*` (executes workflows and persists runtime state). The asymmetry is load-bearing for Elsa's deployment shapes (§E2.2.3) and is the agreed boundary.

**Hard rule.** There **must be no direct dependency from `Elsa.Workflows.Runtime.*` to `Elsa.Workflows.Design.*`.** The two sub-domains are co-equal — neither owns the other; the dependency direction is enforced (or at least audited) in CI via project references.

**One tracked exception is currently allow-listed** *(amended 2026-07-02 — MD-2, Elsa 4 architecture review)*: `Elsa.Workflows.Runtime.JavaScript → Elsa.Workflows.Design.Core`, held in the architecture-guard allow-list (`ArchitectureGuardTests.DeferredRuntimeDesignReferences`) while JavaScript function-declaration ownership across designer and runtime surfaces is unstable. The follow-up is tracked in the [runtime-execution-seam program goal](../../docs/program-goals/runtime-execution-seam.md). Any new exception requires the same treatment: an explicit guard-test allow-list entry plus program-goal tracking — silent drift stays forbidden.

**The seam between Design and Runtime is deferred.** The mechanism by which a workflow flows from Design into Runtime for execution — the carrier type, the activity-contract surfacing, the role of publication, the implications for an `ActivityRegistry` — is **not pinned by this constitution**. It is scheduled for the workflow execution seam follow-up and resurfaces when the Runtime refactor begins; current repo-local findings live in [unfinished work](../../docs/reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

#### §E2.2.1 Design sub-domain — the designed contract

Design owns the *designed contract* of a workflow: input/output definitions, activity tree, expression bindings, plus the persistence layer that stores them.

Packages:

- `Elsa.Workflows.Design.Core` — contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, etc.
- `Elsa.Workflows.Design.Persistence.Core` — design-time persistence contracts.
- `Elsa.Workflows.Design.Persistence.EFCore` — EF Core implementation.
- `Elsa.Workflows.Design.Persistence.EFCore.Sqlite` — SQLite provider for the EF Core implementation.

#### §E2.2.2 Runtime sub-domain — the runtime representation

Runtime owns the *runtime representation* of workflow execution and its own dedicated persistence layer, separate from Design.

Packages (the specific runtime contracts and entities remain governed by the workflow execution seam follow-up recorded in [unfinished work](../../docs/reports/unfinished-work.md)):

- `Elsa.Workflows.Runtime.Core` — runtime contracts and the execution engine.
- `Elsa.Workflows.Runtime.Api`, `Elsa.Workflows.Runtime.Http`, `Elsa.Workflows.Runtime.JavaScript` — runtime surface modules.

Runtime does **not** reference `Elsa.Workflows.Design.Core`, except for the single tracked allow-list exception recorded in §E2.2.

#### §E2.2.3 Deployment-shape gate

The Design ↔ Runtime split exists so Elsa can support Design-only, Runtime-only,
and combined execution deployments. Any change to the split MUST preserve those
deployment shapes or explicitly amend this section.

### §E2.3 `Elsa.Primitives` charter

**framework §2.3 — Elsa specialization.** `Elsa.Primitives` is the narrow
domainless-primitives package that replaced the historical `Elsa.Common` leakage
surface identified by §E1.

**Current charter:**

- `Elsa.Primitives` carries only truly domainless building blocks: `Result<T>`, `Page<T>`, base entity abstractions, guard helpers.
- Zero external NuGet dependencies. Without exception.
- Three-repetition rule applies.

**Anticipated further decomposition.** As code reviews land, additional concerns are split out per framework §2.3:

- `Elsa.Serialization` — already present.
- `Elsa.Events.Core` / `Elsa.Events.Strategies` / `Elsa.Events` — the single in-process event concept over the shared `Elsa.Pipelines.Core` engine.
- `Elsa.Mediator.Core` / `Elsa.Mediator` — command + request dispatch only. It shares `Elsa.Pipelines.Core` with `Elsa.Events.Core`; the two do not reference each other.

**`Elsa.Foundation.Core` is held back.** Elsa does not eagerly create a framework-foundation `.Core` package. If a coherent set of framework-foundation contracts emerges that does not fit in existing packages, the package can be introduced at that point. 

### §E2.4 Elsa foundation repo composition

**framework §2.15 — Elsa specialization.** Elsa's foundation repo is this repository (`elsa-foundation`). It contains the workbench host (§E5), the baseline domain cores, and default implementations needed for local development. Heavy provider-specific or optional integrations remain candidates for standalone feature packages.

The current composition remains revisable as evidence accrues.

### §E2.5 `ElsaDbContextBase` — opt-in capability, not requirement

**framework §2.9 — Elsa specialization.** Framework §2.9 forbids the constitution from mandating a base `DbContext` type. Elsa documents an **opt-in** `ElsaDbContextBase` pattern that consumers may inherit from to receive Elsa's global entity save/load hooks (`IEntitySavingHandler`, `IEntityLoadingHandler`). The save hooks are invoked before `SaveChangesAsync` reaches EF Core; the load hooks fire on the read path through the named read stores (`EFCoreReadStore<TDbContext, TEntity>`) as entities are materialised. Both are useful for shadow properties, custom deserializers, and similar cross-cutting concerns. Each legacy hook now coexists with a `§2.6.1` domain event mirror — `EntitySaving` (Sequential, from `ElsaDbContextBase`) and `EntityLoading` (Sequential, from `EFCoreReadStore`) — that features may migrate onto; the legacy interfaces keep running until a feature migrates.

**`ElsaDbContextBase` is shared EF-Core infrastructure, not a model/entity-design requirement.** The persistence invariants Elsa enforces (immutability of Version entities, audit timestamps, etc. — see framework §2.9's "Persistence invariants are defined independently of the persistence provider") are defined independently of EF Core. An EF-Core-backed application MAY enforce those invariants through `ElsaDbContextBase`; another persistence provider MAY enforce the same invariants through interceptors, mappings, store logic, or whatever its native mechanism is. Inheriting from `ElsaDbContextBase` is one integration path, not the only one.

**Hard rules per framework §2.9:**

- The base context is **opt-in only**. Consumer-owned `DbContext` types remain first-class.
- The framework's only constraint at the EF Core contract layer is `where TDbContext : DbContext`. Never `where TDbContext : ElsaDbContextBase` or `where TDbContext : IElsaDbContext`.
- Consumers must be able to install Elsa's entity mappings and contracts **without** inheriting from `ElsaDbContextBase`.

The save/load handler hooks are documented as an opt-in feature in the relevant module's README. They are not a constitutional requirement.

### §E2.6 Runtime contract — executable-always-runs and artifact-only design

Elsa imposes two coupled invariants on its runtime contract. Together they make the Runtime sub-domain self-sufficient and predictable: given a published runnable artifact, the runtime always runs it; given that artifact, the runtime needs nothing else.

#### §E2.6.1 Executable-always-runs

If an artifact is published as a runnable representation of a workflow, the runtime MUST be able to load and execute it. **No condition internal to the runtime system** — missing activity types, missing module installation, in-memory registry drift, version misconfiguration of runtime infrastructure — may break this contract.

**Whether** an artifact is allowed to run in a given context — per tenant, per environment, per role, per workflow-business state — is a **domain/business gate**, implemented in domain code. The runtime's ability to load and run is a **storage/system contract** that is not subject to those gates.

The separation:

- Domain gates may deny execution; they may not destroy executability.
- System failures to execute (missing types, broken loaders, infrastructure errors) are bugs, not features. They violate the contract.

The runtime artifact format carries enough information to be executed independently of any non-runtime concern. The specific artifact name and shape are settled in the entity-design pass (follow-up `2026-05-08_entity_design.md`).

#### §E2.6.2 Artifact-only runtime

The Runtime sub-domain depends on **only** two things:

1. The **runnable artifact** (the entity carrying the structured runtime-oriented representation produced by Build/Compile).
2. The **configured runtime features** that interpret that artifact's format.

Source artefacts (the design-time workflow definition the artifact was built from), authoring history, draft revisions, designer layout metadata, and visualisation projections live in the Design sub-domain and adjacent application-layer projections. They are reachable from the runtime artifact via foreign keys, **but the runtime does not require them to execute**.

Visualisation of an executed instance happens at the application layer, traversing the FK chain from the executed-instance entity → runnable artifact → source-design entities. The runtime sub-domain is not aware of, and does not load, the source side.

**Hard rule.** A runtime that needs to load design-side data to execute is a §E2.2 hard-rule violation. The seam between Design and Runtime is the runnable artifact; nothing else crosses it at execution time.

**See also §E2.9** — `WorkflowExecutable` is named in the architectural triplet `WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`. State is the source; `WorkflowExecutable` is the derived runnable form the Runtime sub-domain consumes per this section's artifact-only contract.

### §E2.7 Elsa 3 backward compatibility — import-only

Elsa 4's compatibility with Elsa 3 is bounded to **import**. A dedicated adapter module — `Elsa3.Workflows.Import` (and analogous siblings as needed for activities, instances, or other Elsa 3 artefacts) — maps Elsa 3 workflow definitions, activity descriptors, and persistence schemas into the Elsa 4 entity model. Once imported, Elsa 4 runs them natively through its own runtime.

**In scope:**

- One-way one-time mapping: read Elsa 3 source, produce Elsa 4 entities, persist.
- Adapter modules named `Elsa3.<Domain>.Import` per the Elsa-3-side concern they map.

**Out of scope:**

- **Dual-run support.** Elsa 3 and Elsa 4 do not run side-by-side from the same process. A migrating consumer imports, then switches.
- **Ongoing viewmodel mapping** for Elsa-3-shaped endpoints in `elsa-studio`. The Elsa 4 API surface is the API; elsa-studio adapts to it, not the other way around.
- **Round-trip translation** back to Elsa 3 entity shapes after import. Imports are terminal.

The compatibility surface is **"one-way, one-time"** by design. Mapping details are tracked outside the constitution as migration/entity-design work.

---

### §E2.8 Activity catalog is the single source of truth for picker visibility

**Rule.** If an activity is visible in the design-time picker, it has a persisted catalog entry. The picker / design-time API surface MUST query the catalog store; it MUST NOT enumerate live providers, scan loaded assemblies, or otherwise produce picker entries that have no corresponding `ActivityDefinition` row.

**Cross-references:**

- Framework §2.6.4 (design-time vs runtime contract split): the picker reads design-time contracts; runtime construction happens elsewhere.
- Sipke item 7 (2026-05-26): "the catalog is the source-of-truth for picker visibility" — adopted verbatim.

**In scope of this rule (must follow):**

- Every entry the picker can return is a `IActivityDefinition` row, with provenance fields populated (`SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`).
- Activities contributed from a CLR module, a workflow definition, a JSON file, a script source, etc. all reach the picker through the catalog (the reconciler-with-source-modules pattern; Unit B implementation).
- Non-CLR activities (Workflow descriptors, script descriptors) are first-class catalog citizens — the descriptor's `Kind` discriminator on each version row is the runtime resolver lookup key (Unit B §E2.6.1-style domain-failure path on unknown kinds).

**Out of scope (deferred to a separate policy layer):**

- **Context-aware visibility filtering** — tenant scoping, role-based access, feature flags, licensing gates, instance-level overrides. These are visibility refinements over the catalog; they reduce the catalog's output for a given context. They do NOT generate picker entries themselves.

**Removed surface:**

- `IsBrowsable` on `ActivityDefinition` is **not** the visibility mechanism. It does not exist. Visibility = catalog presence. The "should this row appear in the picker?" question has no per-row toggle; it is structurally derived from catalog membership.

**Activity versioning *(Unit 3 2026-06-04; draft pending ratification)*.** The activity version is an author-controlled **string semantic version (SemVer 2.0.0)**, not an engine-assigned integer. The author owns it: it is sourced from the declaring assembly's version and may be overridden per-activity by a `[Version("…")]` attribute. A CLR assembly-scanning `IActivityReconciliationSource` (in `Elsa.Activities.Design.Reconciliation.Clr`) reads the attribute (falling back to the assembly version) and supplies it as the version when the reconciler calls the source — the same DI-source pattern Unit B established (framework §2.6.1). The scanner reads no UI metadata (no display name, description, category from CLR); the only author-intent attribute it honours beyond `[Version]` is `[Required]` on an input (→ `InputDefinition.IsRequired`). The `[Version]` attribute and the version-resolution contract live in the zero-dep `Elsa.Activities.*.Core` so an author can annotate without a heavy dependency.

**Reconciliation policy — Model X *(Unit C 2026-05-28; pending 2026-06-01 architecture review)*.** The activity catalog is reconciled from trusted sources at creation time only. There is **no operational sibling entity**, no `LastSeenAt` heartbeat, no `IsStale` drift flag, no `RemovedAt` source-disappearance tracking. The immutable content hash for a version lives directly on `IActivityDefinitionVersion.ReconcilliationHash` and is the basis of the duplicate-detection path:

- Lookup by `(DefinitionId, Version)`, **build-metadata-insensitive**: the match is on the normalised SemVer sort key (`SemVer.ToSortKey` — a zero-padded comparable form that excludes build metadata), so `1.0.0` and `1.0.0+build` resolve as the same logical version. If absent → create with immutable provenance.
- If present and hash differs → throw `ActivityVersionHashMismatchException` (the source is broken — same identity, different content).
- If present and hash matches → skip or throw per the reconciliation source's duplicate-handling configuration.

"Latest version" / ordering queries sort by the same SemVer sort key descending (a release sorts above its prereleases); there is no integer ordering. Source disappearance is intentionally not tracked at the entity layer; versions are never deleted. Context-aware visibility (tenant / role / feature-flag) is a separate policy layer that filters the catalog for a given context; it is not a reconciliation concern. This codification is **provisional** pending architecture-review ratification.

This section codifies the rule for the activity catalog. The same shape generalises to other catalogs as Elsa accrues them (workflow catalog, script catalog, expression-evaluator catalog); each will get its own catalog-as-source-of-truth section as that catalog matures.

---

### §E2.9 `WorkflowDefinitionState` scope policy + architectural triplet *(Unit C 2026-05-28; pending 2026-06-01 architecture review)*

`WorkflowDefinitionState` is persisted as the `StateSource` shadow JSON on `WorkflowDefinitionVersion` (immutable) and `WorkflowDefinitionDraft` (mutable) inside the `Elsa.Workflows.Design` sub-domain. It is **the canonical authored document of a workflow definition** — the structured shape an author produces and the system promotes through Draft → Version. Pinning its scope explicitly prevents the god-object failure mode flagged in Sipke's 2026-05-26 entity-design review (item 2): as Units D–G crystallize, `WorkflowDefinitionState` is the natural dumping ground for any workflow-related concern unless its boundary is constitutional.

#### §E2.9.1 In scope of `WorkflowDefinitionState`

Members of State carry **authored content** — the structured representation of what the author drew, declared, and configured:

- Variables (the workflow's variable declarations).
- The workflow root activity: one authored `RootActivity`. The root can be any activity kind, including a primitive activity, `Sequence`, `Flowchart`, `StateMachine`, or another composite activity. Child activities, flowchart edges, state transitions, start nodes, and similar composition details belong to the owning activity's authored contract; they are not workflow-level members.
- Workflow-level input/output declarations (`Inputs`, `Outputs`).
- Workflow-level authored options (`WorkflowActivityOptions`, `StrategyOptions`).

This root-activity rule supersedes the earlier provisional `Activities` + `ActivityConnections` workflow-level graph shape. That earlier shape accidentally made every workflow definition a flowchart and is corrected by the Runtime Execution Seam root-activity work unit.

`ActivityNode` itself is not a universal composition carrier. It MUST NOT expose a generic child collection, generic connection list, generic start-child member, or generic executable composition mirror. Activity-specific child ownership is expressed through the activity's own contract: for example `Sequence` owns ordered child activity slots, `If` owns `Then` / `Else`, `ForEach` owns `Body`, `Composite` owns `Root`, and `Flowchart` owns its activities, connections, start selection, and join rules. Core may provide opaque named child slot records so infrastructure can traverse persisted activity trees, but core MUST NOT define framework-wide slot-name constants, metadata keys, port endpoint records, connection records, or executable edge records for activity-specific concepts such as `Body`, `Then`, `Else`, `Root`, or flowchart start/edge selection. Design validation, publishing, and runtime lookup tables may discover children through opaque slot records or activity-specific adapters, but they must not make every activity look like a flowchart-shaped container.

#### §E2.9.2 Out of scope of `WorkflowDefinitionState`

Members that are NOT authored content live elsewhere. Categories explicitly excluded:

- **Instance / runtime / operational state.** Workflow instances, execution log, current activity-execution state, runtime variable bindings, scheduled activations. Owned by the Runtime sub-domain per §E2.2 + §E2.6.
- **Executable / build metadata.** Compiled runtime artifact, build pipeline outputs, materialised executables. Owned by `WorkflowExecutable` (see §E2.9.3) — Units E/G's territory.
- **Publication / deployment state.** Publication status, deployment target, environment-specific configuration overlays. A separate concern with its own entity surface; never folded into State.
- **Search / listing-projection types.** Listing views, dashboard projections, full-text indexes. Derived read models (see §E2.9.3); never fields on State.
- **Security / ownership types.** Tenant ownership, permission grants, audit-of-author identifiers. Carried by ambient `TenantEntity` columns and a separate security model; not authored content.
- **Designer layout metadata.** Canvas positions, sizes, visual node grouping, designer-only annotations. Owned by the sibling entities `WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout` (Unit C FR-006), unified by `IWorkflowDefinitionLayout` (FR-007); never nested into `ActivityNode` and never reachable through `WorkflowDefinitionState`.
- **Validation errors.** Owned by the sibling entity `WorkflowDefinitionDraftValidation` (Unit C FR-021) — derived from State, not part of it.

A property newly proposed for `WorkflowDefinitionState` whose category is genuinely ambiguous between authored content and one of these out-of-State categories surfaces as an architecture-meeting escalation; resolution is constitutional (amend this section), not silent.

#### §E2.9.3 Architectural triplet

`WorkflowDefinitionState` participates in an irreducible triplet that names the three artefacts every workflow definition produces in the system:

1. **`WorkflowDefinitionState`** — the canonical authored document (above).
2. **Read models / projections** — derived views over State for listing, search, dashboarding, and any non-authoring read need. These live in `Elsa.Workflows.Design.Api` or downstream domains' query layers; they are projection-shaped, not authoring-shaped. They are never persisted back into State.
3. **`WorkflowExecutable`** — the compiled runtime artifact (substance owned by Units E/G; named here so the triplet is complete). Build/Compile produces it from an immutable `WorkflowDefinitionVersion.State`; the Runtime sub-domain executes it per §E2.6's artifact-only contract. State is the source; `WorkflowExecutable` is the derived runnable form.

The three sit at separate scopes — **authoring**, **reading**, **executing** — and **must not be merged**. Conflating authoring and projection collapses the read side back into State and creates the god-object Sipke flagged. Conflating authoring and executable conflates source with output, breaking §E2.6's artifact-only rule. The triplet is the load-bearing structural rule that lets Design and Runtime stay separable per §E2.2.

#### §E2.9.4 Enforcement

The in-State / out-of-State boundary is enforced by:

1. **The XML documentation header** on the `WorkflowDefinitionState` record (Unit C FR-003), quoting the scope and pointing at this section.
2. **PR review discipline** against this constitutional rule — reviewers reject creep.

Automated compile-/build-time enforcement (scope-policy static analyser) is **deferred to a future *Code Analysers* epic** that approaches the platform's static analysis as a unified bundle, rather than shipping ad-hoc per-rule micro-validators. The list of categories in §E2.9.2 will inform the eventual analyser when that epic opens; current repo-local findings live in [unfinished work](../../docs/reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

#### §E2.9.5 Reconciliation policy applies here too

`WorkflowDefinition` / `WorkflowDefinitionVersion` / `WorkflowDefinitionDraft` reconciliation follows **Model X** — the same immutable-provenance, skip-or-throw-with-hash-safety-net policy codified for the activity catalog at the end of §E2.8. No per-pass mutating fields (no `LastSeenAt`, no `IsStale`, no `RemovedAt`) live on any workflow-design entity; reconciliation is transactional at creation time only. Where the provenance fields (`SourceKind` / `SourceId` / `SourceVersion` / `ProvisioningHash` / `ProvisionedAt` / `ProvisionedBy`) ultimately live on workflow-design entities is Unit D's allocation pass; Unit C codifies the policy and leaves the field allocation to Unit D.

#### §E2.9.6 Status

**Provisional pending architecture-review ratification.** If the review revises the surrounding activity-reconciliation or workflow-definition scope rules, this section revises in tandem.

**Cross-references:** §E2.2 (Design ↔ Runtime split — the triplet operates within Design and seams into Runtime via `WorkflowExecutable`); §E2.6 (artifact-only runtime — the seam terminates at `WorkflowExecutable`, not at State); §E2.8 (Model X reconciliation policy, applies symmetrically per §E2.9.5).

#### §E2.9.7 Draft-mutation command surface *(Unit 2 2026-06-03; provisional, pending architecture-review ratification)*

The canonical command surface for **mutating** a `WorkflowDefinitionDraft` is a **single coarse, diff-based command** — `IUpdateDraftCommand` — not a family of granular per-concept mutation commands.

- **One mutation command.** `IUpdateDraftCommand.Execute(UpdateDraftRequest)` accepts the **complete desired** `WorkflowDefinitionState` (+ its layout sibling, carried beside State per §E2.9.2 — never inside it). Full-state-always: there is no patch API. Inside the per-Draft distributed lock (`workflow-draft:{DraftId}`) it loads the stored state, wholesale-assigns the desired state (last-writer-wins — no version check), **diffs** stored vs desired per concept (Variables/Inputs/Outputs by `ReferenceKey`, Activities and layout by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple), runs the in-lock validation gate, persists atomically, then publishes **one event per detected difference**.
- **The event surface is preserved, not collapsed.** The diff emits the same 20 per-concept mutation events the former granular commands published (catalogued in the Events section of `Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`); their *types* and catalog headings are unchanged — only the publication site moved onto `IUpdateDraftCommand`. This keeps the event-sourcing seam open for a later event-sourcing unit (Unit H): subscribers observe the per-diff stream regardless of whether the mutation arrived via 20 commands or one.
- **Lifecycle commands remain distinct.** `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, and `IPromoteDraftToVersionCommand` are **not** mutations of an existing Draft's content and stay as separate commands with their own lifecycle events (`DraftCreated`, `DraftDiscarded`). `IUpdateDraftCommand` emits none of these.
- **One origination event, not two.** A cloned Draft and a fresh Draft share the single origination event `DraftCreated`; there is **no** separate `DraftClonedFromVersion`. `ICloneDraftFromVersionCommand` delegates to `ICreateDraftCommand` (the single origination path), and clone-vs-fresh is distinguished solely by the immutable optional `WorkflowDefinitionDraft.SourceVersionId` — a plain provenance column (no navigation property) surfaced on `DraftCreated.SourceVersionId` (`null` for a fresh Draft).
- **Reads route through the named read ports.** Commands that only read (no change tracking) — e.g. `ICloneDraftFromVersionCommand` loading the source Version + layout — use the named per-aggregate read ports (`IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionVersionLayoutStore`, …) rather than a hand-rolled `DbContextFactory` + loading-handler loop. The EF default impls run on the shared `EFCoreReadStore<TDbContext, TEntity>`, which runs the read-side hydration pipeline (legacy `IEntityLoadingHandler` + the `EntityLoading` Sequential event, the read-side mirror of `EntitySaving`) and disposes its own short-lived context. A command opens its own tracked context only when it queries, mutates, and saves the *same* entity.
- **Validation pair unchanged.** The `DraftValidating` (Sequential, in-lock gate) / `DraftValidated` (Background, outcome) pair is published by the command exactly as before.

This supersedes Unit C's Phase-7 granular-command surface for Draft mutation. The generic CQS command-per-operation guidance elsewhere in this constitution (and the framework's `Elsa.Persistence` CQS row) is unaffected — this rule narrows only the **Draft-mutation** surface within the Design domain.

**Status:** Provisional pending architecture-review ratification. Current repo-local findings live in [unfinished work](../../docs/reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

**Cross-references:** §E2.9.1/§E2.9.2 (what State carries — the diff operates over exactly those in-scope fields, layout stays beside State); §E2.6.6 (Sequential vs Background delivery strategies the command uses for the gate vs the per-diff stream).

---

## §E3 Elsa-specific worked example references

Elsa-specific worked examples instantiate framework rules using concrete
`Elsa.*` names and live in
[docs/reference/elsa-worked-examples.md](../../docs/reference/elsa-worked-examples.md).
They are reference material, not additional gates. Framework sections link
to the reference material only when a gate needs a concrete example.

---

## §E4 Elsa configuration — [DEFERRED]

The Configuration & Settings classification (framework §2.12) is deferred to the **Configuration & Infrastructure follow-up meeting**. Pending Elsa-specific findings are recorded in [unfinished work](../../docs/reports/unfinished-work.md). Planned durable work routes through the shared program-goals planner. This section will be revised when that follow-up closes.

---

## §E5 Elsa packaging and versioning

**framework §2.13 — Elsa specialization.** Elsa's current packaging is the snapshot in §E2.4 above. The framework rule (packaging cohesion follows dependency cohesion; packaging is application-level and revisable) governs. Framework §2.13 places the choice of how versions are shared with the application; Elsa's choice is recorded below.

**The workbench is not the product.** `src/Apps/Elsa.Workbench` is a development and demo host used to build out modules. It is not the deliverable executable, and no product host has existed in any Elsa major version. A clean host is therefore a product specification rather than a refactor of the workbench, and the `Elsa.Server` name and the `elsaworkflows/elsa-server` image are reserved for it. The workbench publishes as `elsaworkflows/elsa-workbench`.

**Two version lines.** Elsa uses neither whole-version sharing nor fully independent versioning, both of which framework §2.13 permits.

- **Line A, the host baseline.** The cross-cutting contract packages a host pins share one version that moves only when the contract surface changes, governed by framework §4.2. Membership begins with `Elsa.Primitives`, `Elsa.Events.Core`, `Elsa.Tasks.Core`, `Elsa.Serialization.Core`, `Elsa.Mediator.Core`, `Elsa.Persistence.Core`, `Elsa.Attention.Core` and `Elsa.Expressions.Core`. Membership is recorded in the dependency map and is not final; packages whose cross-domain reach and churn do not decide the question are settled by the clean host specification.
- **Line B, everything else.** All features and all domain `.Core` packages share `major.minor` across the repository, with the patch digit per package. A domain's `.Core` ships with its domain, because a domain is a delivery unit.

The rule this expresses: **a dependency floor must state what a package actually needs.** Uniform versioning inflates floors, which under Strategy B prevents a feature from being installed into a host pinned at an older contract version.

**Versions are computed, not authored.** The patch digit is derived from the count of commits touching a package's own files since the release tag, with file ownership resolved through the dependency map. No `<Version>` element is hand-maintained. Publishing is selective: a package whose own files did not change is not republished, so its floors are never restamped for a change it did not take part in. Preview builds carry no run-number counter, and a release promotes the artifact already built rather than rebuilding from a tag.

**Version magnitude is enforced, not asserted.** Public API additions and breaks are detected mechanically against the last released baseline, and a gate fails when the version bump is smaller than the detected delta requires. A compatible dependency update does not oblige dependents to republish; only a major change propagates. Dependency ranges carry an upper bound at the next major.

**Line A stabilizes by policy after 4.0.** Breaking contract changes batch into planned majors. Without this the contract line churns and the two-line split degrades to uniform versioning.

Rationale, rejected alternatives and the supporting measurements are recorded in [ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md).

**Reversibility.** If, e.g., `Elsa.Serialization.Newtonsoft` and `Elsa.Serialization.SystemText` become demanded by applications outside Elsa, they could graduate into separately published features that Elsa's other features pull in via NuGet. The packaging is reversible per framework §2.16 (refactor-cost test) — preserving NuGet identity insulates consumers from the restructuring.

**Minimum project size (framework §2.16.1 — Elsa interpretive note).** Elsa's tree intentionally contains many sub-100-LoC projects; the 2026-07-04 audit ([MD-5 amendment report](../../docs/reports/elsa-4-w21-md5-minimum-project-size-amendment.md)) found all 13 of them exempt under the §2.16.1 exemption test, so the guidance ratifies the current shape rather than triggering a merge campaign. Worked examples per exception class: contracts-only `.Core` seams (`Elsa.Locking.Core`, `Elsa.Caching.Core`), primitives projects (`Elsa.Workflows.Primitives`, `Elsa.Expressions.JavaScript.Primitives`), provider leaves (`Elsa.Locking.FileSystem`, `Elsa.Persistence.EFCore.Sqlite`), the §E2.7 migration boundary (`Elsa3.Activities.Design.Import`), Layer-2 helpers (`Elsa.Serialization.Newtonsoft`), and independently-composable `[ShellFeature]` units / cross-domain contribution seams (`Elsa.Expressions.JavaScript.Libraries`, `Elsa.Http.JavaScript`). New sub-100-LoC projects that fit none of the six classes need the one-sentence justification of §2.16.1.

**Nuplane strategy.** Elsa adopts **Strategy B** per framework §3: the host pins the Line A baseline contracts; Nuplane dynamically loads Layer-3 implementations, helper libraries, and optional features. A domain's `.Core` is not host-pinned, because a clean host carries no domains; Nuplane promotes it to a shared assembly within that domain's subtree when the domain is installed, for first-party and third-party domains alike. Strategy A is not adopted as Elsa's default, but is not hard-excluded for specific deployment contexts.

---

## §E6 Type-naming rules

**framework §2.2 — Elsa specialization.** Framework §2.2 fixes the *namespace/domain* naming convention (domain-only namespaces, no layer-marker buckets). This section specializes it into a **mechanical type-name style guide** for Elsa-owned types. It is a gate: plans, specs, and code that introduce or rename Elsa-owned types are checked against R1–R8 below. The rules and their supporting analysis originate in the Elsa 4 architecture review (`docs/reports/elsa-4-architecture-review-2026-07/review-naming.md`, findings NM-1..NM-14); this section is the ratified, enforceable extract.

**Scope.** R1–R8 govern **Elsa-owned** type names. Names that mirror an external framework contract are explicitly exempt (see R3). Persisted/wire identifiers (JSON type discriminators, Groundwork document-kind strings, `nameof()`-derived handler names, checkpoint-name constants, serialized member keys) are **not** renamed to satisfy these rules — the literal wire value is preserved and the divergence is commented at the site. Behavior preservation outranks naming.

- **R1 — Component budget.** A type name carries at most **4** CamelCase components; **5** is a hard cap. Beyond that, a component is almost always redundant with the namespace — drop it.
- **R2 — Don't repeat the namespace in the type name.** Inside e.g. `Elsa.Workflows.Runtime.Core`, a type does not need both `Runtime` and `WorkflowExecution` prefixes. Leading `Workflow`/`Runtime`/`Activity` qualifiers are allowed only when they *disambiguate* from a sibling type without them.
- **R3 — Banned vague words for Elsa-owned types:** `Manager`, `Helper`, `Util(s)`, `Info`, `Data`, `Object`, `Service` (when a more specific role fits), `Processor` (prefer a concrete verb). **Exception:** names that mirror an external framework contract (ASP.NET Core Identity `UserManager`, OpenIddict `IApplicationManager`, `IRoleManager`, `ILiquidTemplateManager`) keep the external name.
- **R4 — One suffix, one meaning.** Codified role suffixes, pick exactly one per layer and never use two synonyms for adjacent steps:
  - `…Source` = pull/returns; `…Contributor` = push/mutates context; `…PreProcessor`/`…PostProcessor` = phased contributor; `…Validator` = returns findings.
  - `…Store` = persistence over one aggregate.
  - `…Provider` = yields impls/descriptors; `…Factory` = constructs; `…Resolver` = maps key→value; `…Registry` = holds registrations.
  - `…Executor`/`…Runner` = *does* the work (terminal); `…Router`/`…Dispatcher` = *selects a target and forwards*; `…Orchestrator`/`…Coordinator` = *sequences a multi-step operation*.
  - Reserve `…Handler` for (a) mediator handlers and (b) sanctioned entity-lifecycle handlers. The scheduler `…WorkHandler` family is grandfathered.
  - **Event types carry no `On` prefix** (framework §2.6.6). `On` belongs to the handling side; the event is named for the fact — `DraftValidating` / `DraftValidated`, not `OnDraftValidating` / `OnDraftValidated`. Word order is subject-first. Note this also keeps the longest Draft-mutation names inside R1: `OnActivityOutputRemovedFromDraft` was 6 components, over the hard cap.
- **R5 — Prefer concrete domain nouns over borrowed infra metaphors** unless the metaphor is glossary-documented. Favor `HoldState`/`LivenessState` over `ControlPlaneState`/`OperationalState`. Terms kept as jargon (`Quiesce`, `Passivation`, `ControlPlane`, `Groundwork`, `Nuplane`) MUST have a `docs/glossary/elsa.md` entry.
- **R6 — One concept, one head-noun.** If several types share a `…State` (or similar) head, the *head* must make the distinction obvious (`Hold`, `Liveness`, `Scheduler…`) rather than leaning on a vague adjective.
- **R7 — Default-impl prefixes are fixed and good:** `Default…`, `InMemory…`, `Noop…`, plus provider prefixes `EFCore…`/`Groundwork…`/`Sqlite…`. Keep using them.
- **R8 — Reserve `Agent` for the AI-assistant domain.** Workflow-execution "agents" use `Actor`/`Worker`/`Host` to avoid the cross-domain homonym.

**Protected names (NM-14, do not "fix").** `Bookmark`, `Trigger`, `Incident`, `Outbox`, `Checkpoint`, `WorkItem`, `Hold`, `PauseGate`, `Envelope`, `Slot` are concrete, evocative domain nouns — they are exempt from any shortening pressure. The extension-point grammar (R4 first bullet), `.Core`/Feature layering, `…Store`, Command/Request/Result, and the R7 prefixes are strengths to protect, not simplify (NM-13).

**First application.** The W14 naming pass (Elsa 4 review remediation; `docs/program-goals/elsa-4-review-remediation.md`) applied R1–R8 to the runtime cluster and vague-word offenders (rename families A–E). Subsequent type introductions/renames are expected to conform or record an explicit exception.

---

## Governance

### Amendment process

This constitution is amended together with the framework constitution where the change affects both layers. Elsa-only amendments follow framework Governance > Amendment process:

1. **Propose** as a numbered decision in `ARCHITECTURE_v2.md` (or its successor `DECISIONS.md`) in the meta-repo.
2. **Discuss** with Sipke + Frans.
3. **Ratify** by consensus; fold into this document with the next version bump.
4. **Propagate** to speckit templates (`plan-template.md`, `spec-template.md`, `tasks-template.md`) and any runtime guidance.

### Sync rule with framework constitution

This document declares the framework constitution version it derives from in the header (currently **v4.0.0**). When the framework constitution bumps:

- **PATCH** — re-pin the version; review for clarification impact; no Elsa SemVer bump unless wording downstream of an Elsa specialization is affected.
- **MINOR** — re-pin the version; review every Elsa specialization for compatibility with new framework guidance.
- **MAJOR** — re-pin the version; full review pass; Elsa constitution typically bumps MAJOR in sync.

### SemVer of this constitution

Same rules as framework §4.2 applied to constitutional content:

- **PATCH** — clarifications, wording, typo fixes.
- **MINOR** — new section added or materially expanded Elsa-specific guidance.
- **MAJOR** — backward-incompatible removals or redefinitions of Elsa-specific rules.

### Compliance and review

- Plans and specs generated against this constitution must satisfy a Constitution Check that loads **both** this file and `constitution-framework.md`.
- CI is expected to enforce naming conventions, dependency-envelope assertions (notably the Workflows.Design ↔ Workflows.Runtime asymmetry of §E2.2), and namespace-segment forbids.
- Where AI cannot apply a rule cleanly, that is the signal to escalate — Joey + Sipke + Frans intervene, analyse, decide on a new rule. The constitution matures via this loop (Definition of Done point 2).

---

**Version:** 4.0.0 | **Ratified:** 2026-08-08 | **Last Amended:** 2026-08-08 | **Derives from framework constitution:** v4.0.0
