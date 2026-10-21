<!--
SYNC IMPACT REPORT — 6.1.0 -> 6.2.0 (2026-10-21)
MINOR: adds optional Elsa.Rules to §E2.1 for unified-execution #2892/#3181, ADR 0088. No existing domain purpose, native runtime contract or framework pin changes.
Authority: the program lead adopts ADR 0088 after the bounded planning review on 2026-10-21 under Sipke Schoorstra's standing delegated architecture and self-review authority; no multi-architect consensus or provisional-section ratification is asserted.
Propagation: docs/reference/elsa-domain-tree.md and ADR 0088; Speckit templates have no pinned domain list and require no change. Framework remains v6.1.0.
-->
<!--
SYNC IMPACT REPORT — 6.0.3 -> 6.1.0 (2026-10-20)
MINOR: adds optional Elsa.Etl to §E2.1 for the approved unified-execution program (#2892/#3109, ADR 0087). Existing domain purposes, package boundaries, runtime contracts and framework pin remain unchanged.
Authority: Sipke Schoorstra reaffirmed delegated self-review/PR approval in the owning session on 2026-10-20. The program lead adopts this additive domain decision after the self-review loop under that owner exception; this does not claim a multi-architect vote or ratify provisional sections.
Propagation: docs/reference/elsa-domain-tree.md and ADR 0087; existing templates contain no pinned domain list and need no change.
-->
<!--
SYNC IMPACT REPORT — 6.0.2 -> 6.0.3 (2026-10-20)
PATCH: link only; the domain map is published on the generated-maps branch, not committed (spec 257). No Elsa rule changed.
Approval: Joey Barten, 2026-10-20 (spec 257, #3116).
-->
<!--
SYNC IMPACT REPORT — 6.0.1 -> 6.0.2 (2026-10-14)
PATCH: re-pins framework v6.1.0 (§2.23.3: same-domain production grants under the sibling test, #2591). No Elsa specialization restates §2.23.3, and no Elsa rule changed.
Approval: Sipke Schoorstra, 2026-10-14 (#2591).
-->
<!--
SYNC IMPACT REPORT — 6.0.0 -> 6.0.1 (2026-10-13)
PATCH: re-pins framework v6.0.1 (§2.23.3 wording, #2591). No Elsa rule changed.
Approval: Sipke Schoorstra, 2026-10-13 (#2591).
-->
<!--
SYNC IMPACT REPORT — 5.0.0 -> 6.0.0 (2026-10-12)
MAJOR: compaction to enforceable gates (#2578, ADR 0080 D6), tracking framework 6.0.0. Owner decisions D1–D9 per #2578.
Removed: §E1 (case study in docs/reference/elsa-worked-examples.md); §E3 (index only; the examples stay in docs/reference/elsa-worked-examples.md); §E4 (-> framework Deferred); §E2.4 (-> docs/reference/architecture-rationale.md#repository-organisation, D2); the §E5 Line A/B, commit-count patch digit, selective publishing and Line A stability rules (D5; computed versions, bounded ranges and the magnitude rule stay; ADR 0067 keeps the reasoning, #2576 retires Line A); the §E6 R4 `On` bullet (framework §2.6.6 owns it).
Redefined: §E2.1 pins domain names only, purposes -> docs/reference/elsa-domain-tree.md (D1); the generic §E2.3 charter merged into framework §2.3; amendment venue -> the framework ADR process (D7). R1–R8 stay Elsa-only (D8); no Elsa test-cadence gate (D9).
Moved: detail and history to docs/reference/ (elsa-worked-examples, elsa-naming, elsa-domain-tree); review-only gates to docs/contributing/review-checklist.md. Every surviving § number is kept. Reports before 6.0.0 are removed; git keeps them.
Templates requiring updates: .specify/templates/plan-template.md.
Approval: Sipke Schoorstra, 2026-10-12 (#2578), on his authority alone; open to revision if Joey Barten or Frans van Ek dissent, as with the 4.0.0 ratification.
-->
# Elsa Workflow Engine Constitution

**Version:** 6.2.0 | **Ratified:** 2026-08-08 | **Last Amended:** 2026-10-21 | **Derives from:** [framework constitution](constitution-framework.md) **v6.1.0**

**Status:** Ratified 2026-08-08 by Sipke Schoorstra. Governance calls for consensus among Joey Barten, Sipke Schoorstra and Frans van Ek; this ratification was taken on Sipke Schoorstra's authority alone and is open to revision if the other architects dissent. Sections marked draft or provisional (§E2.8 activity versioning and Model X, §E2.9) are **not** covered by it.

**Read both files.** This is the Elsa specialization; every framework rule applies by reference with `<App>` = `Elsa`, and a Constitution Check that loads only this file is incomplete (Governance).

## Gate index

The framework Gate index applies. Elsa-specific gates with an automated check (tests in `Essentials/tests/Architecture/Tests/`); every other gate is on [docs/contributing/review-checklist.md](../../docs/contributing/review-checklist.md).

| § | Gate | Check |
|---|---|---|
| §E2.2 | Runtime takes no Design reference beyond the allow-list | `ArchitectureGuardTests.Runtime_projects_do_not_add_design_references` (allow-list `DeferredRuntimeDesignReferences`), `.Activity_construction_runtime_path_has_no_design_reference`, `.Dispatch_workflow_modules_preserve_runtime_design_and_transport_boundaries` |
| §E2.3 | `Elsa.Primitives` has no external package | `ArchitectureGuardTests.Elsa_primitives_has_no_external_package_references` |
| §E2.7 | Elsa 3 stays behind the import boundary | `Elsa3MigrationBoundaryTests`, `ArchitectureGuardTests.Runtime_projects_do_not_reference_elsa3_compatibility_projects` |
| §E5 | No project hand-maintains its package version; own-package ranges are bounded at the next major | `NoLiteralProjectVersionGuardTests`, `PackageRangesBuildCheckTests` |

## Derivation

`<App>` = `Elsa`. The host today is `Elsa.Workbench`, a development and demo host; the product host is not yet built (§E5). The foundation repo is `github.com/elsa-workflows/elsa-foundation` (created 2026-05-08). A specialization is written `framework §X — Elsa specialization: …`. Terms: [docs/glossary/root.md](../../docs/glossary/root.md) and [docs/glossary/elsa.md](../../docs/glossary/elsa.md).

## §E2 Elsa domain decomposition

### §E2.1 The Elsa domain tree

The pinned root-level domains (D1): `Elsa.Activities`, `Elsa.Agent`, `Elsa.Api`, `Elsa.Attention`, `Elsa.Caching`, `Elsa.Diagnostics`, `Elsa.Etl`, `Elsa.Events`, `Elsa.Expressions`, `Elsa.Foundation`, `Elsa.Git`, `Elsa.Http`, `Elsa.Locking`, `Elsa.Mediator`, `Elsa.Modularity`, `Elsa.Persistence`, `Elsa.Pipelines`, `Elsa.Primitives`, `Elsa.Rules`, `Elsa.Secrets`, `Elsa.Serialization`, `Elsa.Studio`, `Elsa.Tasks`, `Elsa.Workflows.Dashboard`, `Elsa.Workflows.Design`, `Elsa.Workflows.Publishing`, `Elsa.Workflows.Runtime`, `Elsa3`. Purposes: [docs/reference/elsa-domain-tree.md](../../docs/reference/elsa-domain-tree.md); packages: the generated [domain map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/domain-map.md) (published on the `generated-maps` branch). `Elsa.Workbench`, `Elsa.Architecture`, `Elsa.Samples` and `Elsa.Testing` are host and test groupings, not domains.

### §E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split

**framework §2.18 — Elsa specialization.** `Elsa.Workflows.*` splits into two co-equal sub-domains with separate persistence: `.Design.*` designs and persists workflow definitions; `.Runtime.*` executes workflows and persists runtime state. Their packages (§E2.2.1 Design, §E2.2.2 Runtime): [elsa-worked-examples](../../docs/reference/elsa-worked-examples.md#design-and-runtime-packages).

**Hard rule.** No `Elsa.Workflows.Runtime.*` project depends on `Elsa.Workflows.Design.*`. **One allow-listed exception:** `Elsa.Workflows.Runtime.JavaScript → Elsa.Workflows.Design.Core` (MD-2, 2026-07-02), held in `ArchitectureGuardTests.DeferredRuntimeDesignReferences` while JavaScript function-declaration ownership is unstable, with its follow-up in the [runtime-execution-seam program goal](../../docs/program-goals/runtime-execution-seam.md). A new exception needs the same: a guard allow-list entry plus program-goal tracking. The Design → Runtime seam itself is deferred (framework Deferred).

**§E2.2.3 Deployment-shape gate.** The split exists so Elsa supports Design-only, Runtime-only and combined deployments. A change to it MUST preserve those shapes or amend this section.

### §E2.3 `Elsa.Primitives` charter

**framework §2.3 — Elsa specialization.** `Elsa.Primitives` is the framework's `<App>.Primitives`; it replaced the historical `Elsa.Common` leakage surface. Elsa delta: it also holds base entity abstractions, and `Elsa.Foundation.Core` is held back until a coherent set of foundation contracts needs it ([decomposition](../../docs/reference/elsa-worked-examples.md#elsaprimitives-decomposition)).

### §E2.5 `ElsaDbContextBase` — opt-in capability, not requirement

**framework §2.9 — Elsa specialization.** Elsa offers an **opt-in** `ElsaDbContextBase` whose save/load hooks (`IEntitySavingHandler`, `IEntityLoadingHandler`) run before `SaveChangesAsync` reaches EF Core and, on provider read paths, after materialisation; their §2.6.1 event mirrors are `EntitySaving` and `EntityLoading`. It is shared EF Core infrastructure and one way to enforce persistence invariants, never the only one.

**Hard rules:** consumer-owned `DbContext` types stay first-class; the EF contract-layer constraint is `where TDbContext : DbContext`, never `ElsaDbContextBase` or `IElsaDbContext`; consumers install Elsa's mappings and contracts without inheriting the base. The hooks are documented as opt-in in their module's README.

### §E2.6 Runtime contract — executable-always-runs and artifact-only design

**§E2.6.1 Executable-always-runs.** A published runnable artifact MUST load and execute. No condition internal to the runtime (missing activity types or modules, registry drift, runtime-infrastructure misconfiguration) may break that; such a failure is a bug. Whether an artifact may run for a tenant, environment, role or business state is a domain gate in domain code: it may deny execution, never destroy executability.

**§E2.6.2 Artifact-only runtime.** The runtime depends only on the runnable artifact (`WorkflowExecutable`, §E2.9) and the runtime features that interpret it. Source definitions, history, drafts, layout and visualisation stay on the Design side, reachable by foreign key but never needed to execute; a runtime that loads design-side data to execute violates §E2.2 ([detail](../../docs/reference/elsa-worked-examples.md#runtime-contract)).

### §E2.7 Elsa 3 backward compatibility — import-only

Elsa 3 compatibility is **one-way, one-time import**: adapter modules named `Elsa3.<Domain>.Import` (today `Elsa3.Activities.Design.Import`) map Elsa 3 definitions, descriptors and schemas to Elsa 4 entities, which then run natively. Shared support (`Elsa3.Models`, `Elsa3.Mapping`) is not an adapter and takes no suffix. Out of scope: dual-run in one process, Elsa-3-shaped view models for elsa-studio, round-trip translation back to Elsa 3.

### §E2.8 Activity catalog is the single source of truth for picker visibility

Every activity the design-time picker can show is a persisted `ActivityDefinition` row with provenance (`SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`). The picker MUST query the catalog store, never live providers or loaded assemblies; every source (CLR module, workflow, JSON file, script) reaches it through the catalog, and there is no `IsBrowsable` toggle. Context-aware filtering (tenant, role, feature flags, licensing) narrows the catalog's output and never creates entries ([detail](../../docs/reference/elsa-worked-examples.md#activity-catalog)).

**Activity versioning** *(draft pending ratification)*: an author-owned SemVer 2.0.0 string from the declaring assembly, overridable by `[Version("…")]`; never an engine-assigned integer.

**Reconciliation — Model X** *(provisional pending architecture review)*: catalogs reconcile from trusted sources at creation time only, with no operational sibling, `LastSeenAt`, `IsStale` or `RemovedAt`. Lookup is by `(DefinitionId, Version)`, ignoring build metadata: absent → create; present and the only content difference is input `tier` → the one pre-GA exception, rewriting `Hash` and `InputsSource` in place; present with any other `ReconcilliationHash` → throw `ActivityVersionHashMismatchException`; same hash → skip or throw per source configuration. Versions are never deleted and sort by SemVer.

### §E2.9 `WorkflowDefinitionState` scope policy + architectural triplet

**Status:** provisional pending architecture-review ratification; it revises in tandem with §E2.8. History: [elsa-worked-examples](../../docs/reference/elsa-worked-examples.md#workflow-definition-state).

`WorkflowDefinitionState` is the canonical authored document of a workflow definition, stored on `WorkflowDefinitionVersion` (immutable) and `WorkflowDefinitionDraft` (mutable).

- **§E2.9.1 In scope:** variables; one authored `RootActivity` of any kind, whose composition belongs to that activity's own contract; workflow-level `Inputs`/`Outputs`; `WorkflowActivityOptions` and `StrategyOptions`. `ActivityNode` MUST NOT expose a generic child collection, connection list, start-child member or executable mirror, and core MUST NOT define framework-wide slot names, metadata keys or edge records for activity-specific concepts.
- **§E2.9.2 Out of scope:** runtime state (§E2.2, §E2.6); executable and build metadata; publication and deployment state; listing and search projections; security and ownership; designer layout (the `…Layout` siblings); validation errors (`WorkflowDefinitionDraftValidation`). An ambiguous new member is escalated and settled by amending this section.
- **§E2.9.3 Triplet:** State (authoring), read models and projections (reading) and `WorkflowExecutable` (executing, compiled from an immutable version) MUST NOT be merged.
- **§E2.9.4 Enforcement:** the XML doc header on `WorkflowDefinitionState` and review; a scope analyser waits for a future code-analysers epic.
- **§E2.9.5** Workflow-definition reconciliation follows Model X (§E2.8); no workflow-design entity carries per-pass mutating fields.
- **§E2.9.7 Draft mutation:** one diff-based `IUpdateDraftCommand` takes the complete desired state with its layout beside it, assigns it last-writer-wins inside the per-draft lock, validates in the lock, persists atomically and publishes one event per detected difference. Lifecycle commands (`ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`) stay separate; a clone is a `DraftCreated` carrying `SourceVersionId`. Read-only commands use the named read ports. `DraftValidating` (Sequential, in the lock) and `DraftValidated` (Background) are unchanged.

## §E5 Elsa packaging and versioning

**framework §2.13 — Elsa specialization.** `Essentials/apps/Elsa.Workbench` is a development and demo host, not the product; it publishes as `elsaworkflows/elsa-workbench`. A clean product host is a product specification, and the `Elsa.Server` name and `elsaworkflows/elsa-server` image are reserved for it.

**One version line until GA** ([ADR 0080 D4](../../docs/adr/0080-elsa-4-simplification-decisions.md#d4--pre-ga-cleanup)): every package shares one version line until 4.0 GA; post-GA versioning is revisited once #2576 retires Line A. **Versions are computed, not authored:** no project hand-maintains `<Version>`, `VersionPrefix`, `VersionSuffix` or `PackageVersion`; `PackageVersioning.props` derives each package's version. **Dependency ranges on this repository's own packages carry an upper bound at the next major**. **Version magnitude** is to be enforced mechanically against the last released baseline: a bump smaller than the detected API delta fails (not yet built; ApiCompat is a separate issue). **Nuplane:** Strategy B per framework §3; a domain's `.Core` is not host-pinned, because a clean host carries no domains, and Nuplane promotes it to a shared assembly within that domain's subtree when the domain is installed, first- and third-party alike. Strategy A is not the default but not excluded. Small-project audit and reversibility notes: [elsa-worked-examples](../../docs/reference/elsa-worked-examples.md#elsa-packaging).

## §E6 Type-naming rules

**framework §2.2 — Elsa specialization.** R1–R8 govern Elsa-owned type names (D8) and are checked at review whenever a type is introduced or renamed. Names mirroring an external contract are exempt. Persisted and wire identifiers (type discriminators, document-kind strings, `nameof()`-derived names, checkpoint constants, serialized keys) are never renamed to satisfy them: keep the value and comment the divergence. Full text and origins: [docs/reference/elsa-naming.md](../../docs/reference/elsa-naming.md).

- **R1** At most **4** CamelCase components; **5** is a hard cap.
- **R2** Don't repeat the namespace; a leading `Workflow`/`Runtime`/`Activity` only to disambiguate from a sibling.
- **R3** Banned vague words: `Manager`, `Helper`, `Util(s)`, `Info`, `Data`, `Object`, `Service` (when a specific role fits), `Processor`. External mirrors (`UserManager`, `IApplicationManager`, `IRoleManager`, `ILiquidTemplateManager`) keep their names.
- **R4** One suffix, one meaning: `…Source` returns on request (reserved for that sense, *pending maintainer consensus*); `…Contributor` pushes into a context (→ `…Extension`, ADR 0080 D5); `…PreProcessor`/`…PostProcessor` phased; `…Validator` returns findings; `…Store` persists one aggregate; `…View` wire projection, `…Response` its envelope, both in one file; `…State` data structure, `…Status` lifecycle enum, sharing a file when a type has both; `…Provider` yields, `…Factory` constructs, `…Resolver` maps key→value, `…Registry` holds registrations; `…Executor`/`…Runner` do the work, `…Router`/`…Dispatcher` select and forward, `…Orchestrator`/`…Coordinator` sequence; `…Handler` only for mediator and sanctioned entity-lifecycle handlers.
- **R5** Concrete domain nouns over borrowed infra metaphors; kept jargon (`Quiesce`, `Passivation`, `ControlPlane`, `Nuplane`) MUST have a `docs/glossary/elsa.md` entry.
- **R6** One concept, one head noun; the head makes the distinction.
- **R7** Default-implementation prefixes `Default…`, `InMemory…`, `Noop…` and the provider prefix `Ef…`.
- **R8** `Agent` is reserved for the AI-assistant domain; workflow-execution roles use `Actor`/`Worker`/`Host`.

**Exceptions:** provenance `Source…` names, OpenTelemetry `ActivitySource` mirrors and the existing origin-value `…Source` types; the serialized single-payload `…Response` types in `Elsa.Agent.Api` and `Elsa.Foundation.Identity.Api`; older `…State` enums with serialized values; the scheduler `…WorkHandler` family. New and renamed types conform or record an explicit exception. **Protected names (NM-14):** `Bookmark`, `Trigger`, `Incident`, `Outbox`, `Checkpoint`, `WorkItem`, `Hold`, `PauseGate`, `Envelope` and `Slot` are exempt from shortening pressure.

## Governance

**Amendment process** (D7): the framework's ADR process. A numbered decision under `docs/adr/` is ratified by consensus of Joey Barten, Sipke Schoorstra and Frans van Ek, folded in with a version bump, and propagated to the Speckit templates in the same change. This file re-pins the framework version on every framework bump and versions itself the same way: PATCH for wording, MINOR for new or expanded Elsa guidance, MAJOR for a removed or redefined Elsa rule.

**Constitution Check loads both files.** Speckit's plan and analyze steps read only this file; they MUST also load `constitution-framework.md`, apply both Gate indexes and [docs/contributing/review-checklist.md](../../docs/contributing/review-checklist.md). CI enforces what admits mechanical checking, notably the §E2.2 asymmetry. A rule that cannot be applied cleanly is escalated to the architects.
