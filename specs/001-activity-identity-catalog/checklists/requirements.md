# Specification Quality Checklist: Activity Identity & Catalog as Source-of-Truth

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-27
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Specification is architect-internal: the audience is Joey + Sipke + Frans, not non-technical stakeholders. References to specific types (`ActivityDefinition`, `ActivityDefinitionVersion`, `TypeInformation`, etc.) are intentional — they are the *names of domain concepts*, not implementation details. The Content Quality items are interpreted accordingly: no language/framework names; references to type names are domain language per the constitution's "domain language only" naming convention.
- The constitutional-flag block in the spec's *Constitutional Compliance* section identifies three originating concerns: (a) a new Elsa §E2.x rule for catalog source-of-truth (certain), (b) a possible framework §2.x rule for the implementation-kind / descriptor pattern (deferred), and (c) the `TenantEntity` base-class refactor folded into Unit B. All three surface at the plan stage's Constitution Check.
- Items 8 + 9 (stable read contracts framing) are folded into User Story 6 and FR-023 / SC-009 rather than a separate doc-only pass.
- The activity-catalog portion of Unit A's entity-handler migration is folded into User Story 5 and FR-017 / SC-007. The wider Unit A item (handlers elsewhere in the codebase) remains open in the Unit A follow-up file.
- **Provenance / reconciliation split.** `ActivityDefinitionReconciliationState` is a new sibling entity (1:0..1 with `ActivityDefinition`) that holds operational reconciliation fields. The catalog row holds only immutable creation provenance (`SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`). This protects the `IActivityDefinition` read contract from churning every reconciliation pass (Sipke item 9 / framework §2.9 alignment).
- **`TenantEntity` base class.** A new `TenantEntity : Entity` base class is introduced in `Elsa.Primitives.Entities`. `TenantId` is removed from `Entity`. Both activity-catalog and workflow-side entities are migrated to inherit from `TenantEntity` in this same change, keeping the codebase consistent.
- **Open discriminators (pinned at clarify).** Both `ImplementationKind` and `SourceKind` are **smart-enum value-records** (`public sealed record X(string Value)` with `static readonly` instances). Persisted as the wrapped string; consumed as the typed wrapper. Unit B ships the minimum set (`Clr`, `Workflow` for kinds; the existing provisioner's source kind for sources); registration is open.
- **Descriptor storage (pinned at clarify, refined session 3+4).** `ActivityDefinitionVersion` exposes one CLR property: `[NotMapped] IImplementationDescriptor ImplementationDescriptor`. The persisted form is an EF Core shadow column named `ImplementationDescriptor` (same name; CLR `[NotMapped]` keeps it from colliding). Deserialisation: loading handler queries `IImplementationDescriptorRegistry.Resolve(ImplementationKind)` for the CLR type, then `IPayloadSerializer.Deserialize(json, type)` via reflection-driven generic method invocation. No custom `JsonConverter`.
- **Identity rename (pinned at clarify).** `UniqueName` → `ActivityTypeKey`. One immutable column carrying the stable logical key.
- **Uniqueness constraint (pinned at clarify).** `(SourceKind, SourceId, ActivityTypeKey)` is the unique composite. `(SourceKind, SourceId)` is a non-unique lookup index for the reconciler's "what did this source produce?" query.
- **Resolver split (pinned at clarify, revised mid-session).** Two interfaces, not one. `IActivityFactory.Create(IImplementationDescriptor, IEnumerable<ArgumentState>, IEnumerable<ArgumentState>, CancellationToken)` is the single dispatch + construction entry point. `IActivityImplementationResolver<TDescriptor>` (`string Kind`, `Type Resolve(TDescriptor)`) is the kind-specific type lookup. Resolvers are contributed via `IDomainEventSender` per framework §2.6.1.
- **Descriptor base is an interface (pinned at clarify).** `IImplementationDescriptor` is an interface in `Elsa.Activities.Design.Core`. `ClrImplementationDescriptor` implements it and wraps `TypeInformation` (which stays a pure primitive value object). Non-CLR descriptors (`WorkflowImplementationDescriptor`, etc.) implement the same interface per kind.
- **`IsBrowsable` removed (pinned at clarify).** No `IsBrowsable` column on `ActivityDefinition`. Picker visibility is catalog presence + `ActivityDefinitionReconciliationState.RemovedAt`. The context-aware policy layer (deferred) handles "hide without removing" semantics.
- **`TenantId` index centralized (pinned at clarify).** Declared in `ElsaDbContextBase.OnModelCreating` — model-build-time scan of `TenantEntity` descendants. Same centralization as immutability enforcement.
- **`IsStale` index (pinned at clarify).** Declared on `ActivityDefinitionReconciliationState` for the reconciler's stale-removal sweep. No index on `ImplementationKind`.
- **Leaf models are sealed records (pinned at clarify).** `InputDefinition`, `OutputDefinition`, `ActivityPortDefinition`, `ArgumentDefinition` → `public sealed record`s. No separate read-only sibling interface required.
- **`ArgumentState` is design-time filled-in canvas (pinned at clarify, second pass).** `ArgumentState` base record + derived `InputState` / `OutputState` (same shape, distinct types for signature clarity). Carries `ReferenceKey` + `ArgumentValue` (`{ object? Value, ExpressionType ExpressionType }`). Lives in `Elsa.Activities.Design.Core.Models`. Factory transforms it into a runtime `IExpression` at construction time.
- **Provisioning → Reconciliation rename (pinned at clarify, second pass).** Modules `*.Provisioning.Core` / `*.Provisioning` → `*.Reconciliation.Core` / `*.Reconciliation`. `IActivityVersionProvisioner` → `IActivityVersionReconciler`. `OnActivityVersionsProvisioning` → `OnActivityVersionsReconciling`. Field-level naming (`ProvisionedAt`, `ProvisioningHash`, etc.) preserved. New `IActivityDefinitionHasher` contract in `Reconciliation.Core` with default impl in the feature. Seed source: JSON-file reconciler (`SourceKind.Json`, `SourceId = assembly name`, `ProvisionedBy = Environment.MachineName`).
- **Source contract for provenance (pinned at clarify, second pass).** Reconciliation sources supply `SourceKind` + `SourceId` + `ProvisionedAt` + `ProvisionedBy` on the contributed `IActivityDefinitionVersion.Definition`. The reconciler is responsible for hashing, reconciliation-state writes, and find-or-create logic on the parent row.
- **`OnEntitySaving` ownership (pinned at clarify, refined session 3).** Unit B defines `OnEntitySaving` in `Elsa.Persistence.EFCore` (where the EF Core dispatch lives; the event carries `DbContext` + `EntityEntry`). Other features' migrations from the legacy saving provider interfaces happen later but inherit this event type. **`IEntityModelCreatingHandler` stays as-is** — model-creating is a sync side-effect chain, not a contribution flow in §2.6.1's sense; no `OnEntityModelCreating` event introduced.
- **`IArgumentDefinition` retired (pinned at clarify, second pass).** Removed entirely. The sealed `ArgumentDefinition` record is the read contract; no interface-record duality.
- **`IImplementationDescriptorRegistry` (pinned at clarify, fourth pass).** Explicit registry follows the canonical §2.6.1 Registry + StartUp Task sub-pattern. `IImplementationDescriptorRegistry` + `ImplementationDescriptorRegistration` record + `OnImplementationDescriptorsInitializing` event live in `Elsa.Activities.Design.Core`; the startup task lives in the activities runtime feature. EF loading handler resolves kind → CLR descriptor type via the registry, then `IPayloadSerializer.Deserialize`. Symmetric with the resolver registry.
