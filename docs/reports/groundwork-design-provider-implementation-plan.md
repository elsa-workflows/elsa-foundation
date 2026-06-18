# Groundwork design persistence provider — implementation plan

Status: **Ready to execute** (foundation proven in code). Owner program goal:
[Groundwork persistence readiness](../program-goals/groundwork-persistence-readiness.md).
Companion verdict: [Groundwork host-configurable persistence feasibility](groundwork-host-configurable-persistence-feasibility.md).
Companion handoff: [Groundwork closed-query capability spec](groundwork-closed-query-capability-spec.md).

## Why this plan exists

The feasibility investigation is **answered and proven in code**: Elsa's design lanes no longer speak
`IQueryable`/LINQ. They speak the closed, provider-neutral `Query<TEntity>` spec through named per-aggregate
read ports, and that spec demonstrably executes on **either** a relational database (EF Core, via
`EFCoreReadStore`) **or** a document database (Groundwork, via `GroundworkReadStore<TEntity>`) returning the
same result set. What remains is **productionization**: turning the proven generic read store into a
registrable, host-selectable Groundwork **design** provider that backs every design aggregate — reads and
writes — so a host can wire one provider for the whole product.

This is deliberately captured as a plan rather than improvised: the six design aggregates are **coupled**
through their write commands (e.g. `IAddWorkflowDefinitionCommand` writes a definition *and* its first
version together), and the rich entities carry a real **serialization-model decision**. Both warrant a
deliberate, reviewed build.

## Foundation already in place (committed on the universal-provider branch)

| Building block | Location | Proof |
|---|---|---|
| Closed query contract | `Elsa.Persistence.Core/Queries/Query.cs` | Eq/In/Contains + AND-of-OR + 1 order |
| EF Core translator | `Elsa.Persistence.EFCore/.../EFCoreQueryTranslator` + `EFCoreReadStore` | 7 proof tests |
| In-memory fallback evaluator | `Elsa.Persistence.Core/Queries/InMemoryQueryEvaluator` | 11 tests, EF-identical semantics |
| Document read store | `Elsa.Persistence.Groundwork.Querying/GroundworkReadStore<TEntity>` | 11 tests, same result set as EF |
| Document envelope | `Elsa.Persistence.Groundwork.Querying/GroundworkDocument<TEntity>` | by-collection partition technique |
| Named read ports (all 6) | `*.Design.Persistence.Core/Stores/I*Store.cs` | consumers migrated; `IQueries`/`IFilter` deleted |
| Query-uplift handoff for Groundwork | capability spec | 5 bounded capabilities |

## The serialization-model decision (RECORDED)

For documents, persist the **domain projection** of each entity, not the EF storage shape:

- **Include the logical state directly.** `WorkflowDefinitionVersion.State`, `WorkflowDefinitionDraft.State`,
  `ActivityDefinitionVersion.DescriptorPayload`/`Inputs`/`Outputs`/`DesignFacets`, and
  `WorkflowDefinitionVersionLayout.Records` are serialized as first-class JSON. (These are `[NotMapped]` for EF
  only because EF stores them in a shadow `*Source` string column via saving/loading handlers. A document store
  has no such constraint, so the logical object is the payload.)
- **Exclude the EF shadow `*Source` strings.** They are a relational-storage artifact (write-once columns); the
  logical state above is the source of truth in a document.
- **Exclude navigation properties.** `WorkflowDefinitionVersion.Definition`,
  `ActivityDefinitionVersion.Definition`, etc. are separate aggregates. The `GetWithDefinition*` port methods
  already model the relationship as an **explicit second read** (no join), so the nav need never be embedded —
  avoiding write-amplification and update anomalies across aggregate boundaries.
- **Keep the indexable scalars top-level** so the by-collection index field and any future native-pushdown index
  fields (`Id`, `DefinitionId`, `SemVerSortKey`) resolve directly from the JSON. `SemVerSortKey` is a precomputed
  plain string — the store needs zero SemVer knowledge.

**Mechanism (no entity pollution):** drive exclusion with a `System.Text.Json`
`DefaultJsonTypeInfoResolver` modifier (or per-type `JsonTypeInfo` tweaks) in a shared
`GroundworkDesignJson` options factory, rather than scattering `[JsonIgnore]` across core entities. Web
(camelCase) defaults so field paths match declared index names — same convention as the runtime bridge's
`GroundworkRuntimeJson`. Deserialization targets each entity's existing parameterized constructor (members
matched by name) plus its settable/`init` members.

## Per-aggregate work (read side — mechanical once serialization lands)

Each named read port gets a Groundwork adapter that wraps `GroundworkReadStore<TEntity>`, exactly mirroring the
EF adapter that wraps `EFCoreReadStore<TDbContext,TEntity>`:

1. `IWorkflowDefinitionStore` → `WorkflowDefinition` (simplest: Id/Name/Description, no nav, no NotMapped). **Start here.**
2. `IWorkflowDefinitionVersionStore` → `WorkflowDefinitionVersion` (rich: `State`, nav, `SemVerSortKey` order, `GetWithDefinition` = 2nd read).
3. `IWorkflowDefinitionDraftStore` → `WorkflowDefinitionDraft` (rich: `State`).
4. `IWorkflowDefinitionVersionLayoutStore` → `WorkflowDefinitionVersionLayout` (rich: `Records` value-converted collection).
5. `IActivityDefinitionStore` → `ActivityDefinition`.
6. `IActivityDefinitionVersionStore` → `ActivityDefinitionVersion` (rich: `DescriptorPayload`/`Inputs`/`Outputs`/`DesignFacets`, nav, sort-key order).

The `GetWithDefinition*` methods perform the parent read as a second `FindByIdAsync` — never a join.

## Write side (the larger, coupled piece)

The design write surface is a set of named command interfaces with EF implementations over `DbContext`:

- Workflows: `IAddWorkflowDefinitionCommand`, `ICreateDraftCommand`, `IUpdateDraftCommand`,
  `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, `ISubmitWorkflowDefinitionCommand`,
  `ICloneDraftFromVersionCommand`.
- Activities: `IAddActivityDefinitionCommand`.

Each needs a Groundwork implementation that writes `GroundworkDocument<TEntity>` envelopes via
`IDocumentStore.SaveAsync` / removes via `DeleteAsync`, stamping the constant collection partition. Because
commands span multiple aggregates (definition + version + draft) and Groundwork preview documents are
**autonomous per operation** (no cross-document transaction — see runtime Phase 2 finding), each command must be
**idempotent and ordered** so a mid-sequence failure is recoverable on retry (write the child, then the parent
pointer; or use deterministic ids so re-execution converges). This mirrors the runtime checkpoint writer's
per-id durable-marker approach.

## Design storage manifest

Mirror `ElsaRuntimeStorageManifest`: a `WorkflowsDesignStorageManifest` + `ActivitiesDesignStorageManifest`
(or one combined design manifest) declaring each document **kind** with a **by-collection keyword index**
(equality, the only thing every Groundwork provider supports today) and `PortableQueryDeclaration`s for the
enumerate-collection read. Intent `PortableDocument`, `IdentityPolicy.StringId`, `ConcurrencyPolicy.Optimistic`,
`SerializationPolicy.Json`, `PhysicalizationPolicy.Portable`. As Groundwork ships the capability-spec operators,
add native index declarations and push individual clauses down without changing the port contracts.

## Single-provider host composition

One registration entry point wires **every** lane to the chosen provider, e.g.
`AddGroundworkDesignStores(...)` alongside the existing `AddGroundworkRuntimeStores(...)`, both pointed at one
host-selected `IDocumentStore` (e.g. `SqliteGroundworkDocumentStore`). Use the runtime registration's
`RemoveAll<TPort>() + AddSingleton<TPort, TGroundworkAdapter>()` swap pattern so composing the Groundwork
provider replaces the EF (or in-memory) registrations. The acceptance test: a host that registers only the
Groundwork provider runs the full design + runtime surface against one document database.

## Suggested project layout (mirrors the EF Core split)

- `src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj`
- `src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj`

Each references its `*.Design.Persistence.Core` plus `Elsa.Persistence.Groundwork.Querying`. The
host/provider feature (Sqlite document store) already exists as `Elsa.Persistence.Groundwork.Sqlite` and is
reused unchanged. (An adapter project may reference the heavy `Groundwork.Documents` package because it is **not**
a `*.Core` project — same rule the runtime bridge follows.)

## Execution order

1. **WorkflowDefinition** read adapter + design manifest + `GroundworkDesignJson` + DI swap + tests (the
   replicable pattern; no serialization complexity).
2. **WorkflowDefinitionVersion** read adapter — first rich entity; lands the serialization-model decision and a
   round-trip test (proves `State` survives, nav excluded, sort-key order works, `GetWithDefinition` = 2nd read).
3. Remaining four read adapters (mechanical replication).
4. Write commands per aggregate (idempotent/ordered), with restart-recovery tests like the runtime writer.
5. Single-provider host composition + end-to-end "one document DB backs everything" test.
6. Refresh generated maps (`bash tools/maps/generate-extension-point-map.sh`) and close out the feasibility
   report.

## Out of scope (already settled)

- No relational Groundwork providers for the design lane — every lane runs on relational **or** document, host's
  choice (host decision, 2026).
- No full ORM in Groundwork — only the bounded capability-spec uplift.
- No `Include`/join — modeled as explicit second reads.
- No SemVer logic in the store — precomputed `SemVerSortKey`.
