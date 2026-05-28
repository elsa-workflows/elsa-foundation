# Phase 0 Research — Unit C plan-stage decisions

Resolves the *Open questions → Plan-stage detail decisions* listed in the Unit C follow-up. Each item carries **Decision / Rationale / Alternatives considered** per speckit Phase 0 conventions. None of these are NEEDS CLARIFICATION items blocking the plan — they are concrete plan-stage choices that need pinning before implementation begins.

---

## R1. `ActivityPortConnection` NodeId-named join key — final name

**Decision:** `ActivityNodeId`.

**Rationale.** The join key on `ActivityPortConnection` carries the *source/target activity-node identifier* — it is a foreign-key reference to an `ActivityNode.NodeId`. Naming it `NodeId` would conflict with the natural reading of `ActivityNode.NodeId` (the node's own identity); `ActivityNodeId` reads unambiguously as "the id of the connected activity-node." Connections are edges between activity nodes, not nodes themselves — the `Activity` prefix is meaningful, not redundant. The two-part name also matches typical FK naming conventions (`<ReferencedTable>Id`).

**Alternatives considered.**
- `NodeId` — rejected: ambiguous with `ActivityNode.NodeId`; reader has to disambiguate by context.
- `ConnectedNodeId` — rejected: introduces a new noun (`ConnectedNode`) that doesn't appear elsewhere in the model.
- `TargetNodeId` / `SourceNodeId` — would imply two separate fields per edge half; but `ActivityPortConnection` already has direction semantics through its source/target port references, and the activity-node identifier is symmetric (both ends reference an activity-node by id). One field name suffices.

---

## R2. `ValidationError.Path` format convention

**Decision:** `Path` is a slash-delimited path with optional input/output reference suffix. Format:

```
{NodeId}                                — workflow-level concern bound to a node
{NodeId}/inputs/{InputReferenceKey}     — activity input
{NodeId}/outputs/{OutputReferenceKey}   — activity output
$workflow/inputs/{InputReferenceKey}    — workflow-level input declaration
$workflow/outputs/{OutputReferenceKey}  — workflow-level output declaration
$workflow/variables/{VariableName}      — workflow variable
$workflow                               — workflow-scope concern (e.g. missing start activity)
```

The `$workflow` sentinel disambiguates workflow-level paths from any conceivable NodeId. The `$` prefix is a common DSL convention for "the document root."

**Rationale.** A single string suffices for the UI grouping key while still being machine-parseable for tooling. Slash-delimited paths are familiar (JSONPath, XPath idiom), and the suffix forms cover all five validator scopes (FR-033's five baseline validators each emit paths in one of these forms). Plan-stage commits to the format so all validator implementations + the UI grouping logic share one parsing convention.

**Alternatives considered.**
- Structured object (e.g. `{ NodeId, InputRef, OutputRef }`) — rejected: changes `ValidationError` from a value object to a tagged-union shape; more complex than necessary; UI grouping key would need to flatten anyway.
- Free-form `string` — rejected: every validator would invent its own convention; UI grouping would be brittle.
- JSON-encoded path — rejected: too heavy for what's effectively a short identifier.

---

## R3. `ValidationError.Type` extensibility convention

**Decision:** `Type` is a slash-delimited category string. Format:

```
{Category}                — single-level category
{Category}/{Subcategory}  — two-level category
```

Reserved baseline categories (Unit C ships these):
- `Graph/OrphanActivity`
- `Graph/StartActivity`
- `Variables/Uniqueness`
- `InputOutput/MissingRequired`
- `Expressions/UnresolvedVariable`

External validators (activity features, future Validations.* sub-modules) extend by prefixing with their domain — e.g. `Http/AuthPolicyUnknown`, `Http/InvalidUrl`, `JavaScript/SyntaxError`. No central registry; validators MUST choose categories that are clearly scoped to their domain.

**Rationale.** The grouping key `(Path, Type)` benefits from `Type` being parseable into category levels — the UI can group at the top-level category (`Graph`, `Variables`, `Http`, …) and surface subcategories on expand. Slash-delimited is the same idiom as `Path`, keeping the parsing consistent. No enum because the legal set is genuinely extensible (FR-022 says so explicitly); the slash convention gives structure without enumeration.

**Alternatives considered.**
- C# `enum` — rejected: legal set must be extensible by validators; an enum would force every contributing feature to extend the enum (impossible cross-package).
- Free-form string — rejected: validators inventing parallel category names (`MissingInput`, `RequiredInputMissing`) would muddle UI grouping.
- Numeric error code — rejected: harder for humans to read in logs/diagnostics.

---

## R4. Catalog-parity test mechanism

**Decision:** Reflection-scan strategy + markdown heading convention `### <EventClassName>`.

Mechanism:
1. Reflection-scan the target assembly (`Elsa.Workflows.Design.Core` or `Elsa.Workflows.Design.Validations.Core`) for all types implementing `IDomainEvent` (public, non-abstract, concrete `class` per framework §2.6.1's sealed-class requirement).
2. Parse the corresponding `DOMAIN_EVENTS.md` from the project root. Extract all level-3 markdown headings (`### `). Filter to those that look like type names (alphanumeric, no spaces, starts with `On`).
3. Assert bidirectional set equality. Failures report missing entries (event without heading) or stale entries (heading without event).

**Naming convention:** the markdown heading MUST match the event class name verbatim, prefixed with `### `. Example: `### OnActivityAddedToDraft`. No prose decoration in the heading (the prose lives in the heading's body).

**Test location:** `tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs`. The same test class parametrises over both Core assemblies (Workflows.Design.Core + Workflows.Design.Validations.Core), running the same parity assertion against each pair.

**Rationale.** Reflection-scan is the simplest mechanism that gives bidirectional guarantees. The heading convention is structurally machine-parseable without imposing on the prose authoring of the catalog. Parametrising the test over both `.Core`s means new domains that adopt the rule (per framework §2.22.1) only have to add a new parameter row, not duplicate the test.

**Alternatives considered.**
- Code-generation (a `[GeneratedCatalog]` attribute that emits the markdown) — rejected: tightly couples writing prose to source-level codegen; reverses the "prose-first, code-validates" intent of the catalog.
- Annotation-based (each event has a `[CatalogEntry("...")]` attribute) — rejected: duplicates the catalog content into source; defeats the purpose of a single authoritative markdown file.
- Doc-comment scan (extract `<summary>` tags from event types) — rejected: doc-comments and the catalog have different audiences (consumer docs vs domain reference); coupling them harms both.

---

## R5. EF cascade rules for `*Layout` and validation siblings

**Decision:**
- `WorkflowDefinitionVersionLayout`: `OnDelete: Restrict` (parent Version delete is forbidden — versions are never deleted per Joey's 2026-05-19 Q3 stance; sibling cascade is moot but documented).
- `WorkflowDefinitionDraftLayout`: `OnDelete: Cascade` (Draft deletion via `IDiscardDraftCommand` MUST atomically delete the layout sibling per FR-029).
- `WorkflowDefinitionDraftValidation`: `OnDelete: Cascade` (same — FR-029 atomicity).

EF Core configuration sets `OnDelete(DeleteBehavior.Cascade)` on the Draft-side relationships; the Version-side relationship uses `OnDelete(DeleteBehavior.Restrict)` as a belt-and-braces guard against accidental Version deletes.

**Rationale.** FR-029 (`IDiscardDraftCommand`) explicitly requires atomic deletion of the Draft + both siblings. EF Core's `Cascade` behaviour is the standard mechanism. The Version-side `Restrict` is documentary — the application contract already forbids Version deletion, but the database-level guard catches any out-of-band delete that bypasses the command.

**Alternatives considered.**
- `OnDelete: SetNull` on Draft-side siblings — rejected: leaves orphan rows; defeats FR-029's atomicity.
- Manual deletion in the command body (no cascade configured) — rejected: brittle; a future code path that deletes a Draft outside the command would leak orphan rows.
- `OnDelete: NoAction` (no FK constraint enforcement at all) — rejected: removes the database-level safety net.

---

## R6. ~~Forbidden-types mechanism for the scope-policy test~~ — RETIRED

**Decision:** **Item retired** per clarify session 3 (2026-05-28). The scope-policy test (formerly FR-004 / SC-003 / US1 Acceptance Scenario 2) is removed from Unit C scope. The constitutional rule (Elsa §E2.X) + the documentation header on `WorkflowDefinitionState` (FR-003) + PR review carry the in-State / out-of-State boundary. Automated compile-/build-time enforcement is deferred to a future *Code Analysers* epic that approaches the whole platform's static analysis as a unified bundle.

**Rationale.** Per Joey 2026-05-28: the constitution is being made deliberately sound precisely so that ad-hoc per-rule guard tests aren't needed. Creating one micro-validator per constitutional rule now would produce a fragmented enforcement layer that becomes mess; consolidating compile-/build-time enforcement into a dedicated epic once all the patterns settle is architecturally cleaner. The exception is the catalog-parity test (FR-031), which is kept because documentation drift on the events catalog is uniquely structural — the catalog IS the discovery surface for cross-domain consumers, and a stale catalog is an immediate consumer-impact problem from day one (different category from the State scope policy, which evolves slowly and is reviewer-tractable).

**Future-epic candidate registered at:** [`../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_future_epic_code_analysers.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_future_epic_code_analysers.md).

**Alternatives considered (recorded for the future epic).** When the Code Analysers epic opens, the original options re-surface as design candidates: reflection blacklist (cheapest, positive failure-on-violation), marker interface (requires opt-in from forbidden types, brittle cross-codebase), Roslyn analyzer (heaviest, most diagnostic-rich, compile-time enforcement). The choice is deferred until the epic opens; bundling multiple constitutional rules' enforcement under one analyzer is the architectural payoff.

---

## R7. Test-project allocation

**Decision:** Single new `tests/Elsa.Workflows.Design.Tests` project (NOT a sibling-split).

The project hosts:
- Catalog parity tests for BOTH Core assemblies (parametrised — see R4)
- Event-naming tests (no bare `Input`/`Output`)
- Method-pattern tests (no raw collections on events per §2.6.1)
- All five baseline validator tests
- Validations feature registration test
- Cross-feature validator subscription test
- All FR-019 mutation command tests
- Lock-semantics tests
- Clone/Discard tests
- Promotion gate test

**Rationale.** A single project is simpler to wire (one `xunit` reference, one DI container scaffold, one test-runner config). The test surface is cohesive — all tests target `Elsa.Workflows.Design.*` and `Elsa.Workflows.Design.Validations.*`. Splitting (e.g. a separate `Elsa.Workflows.Design.Validations.Tests`) would require duplicating shared test fixtures (Draft-with-State factory helpers, etc.).

The existing `tests/Elsa.Activities.Design.Tests/` retains responsibility for Activities-domain assertions; Unit C adds one test class there for the `IsRequired` field assertions (SC-024 — same domain, same project).

**Alternatives considered.**
- **One test project per source project (e.g. `Validations.Tests` + `Validations.Core.Tests`)** — rejected: over-fragmented; shared fixtures would duplicate.
- **Reuse `Elsa.Activities.Design.Tests`** — rejected: different domain ownership (Activities ↔ Workflows.Design); mixing dilutes responsibility.
- **No new test project; add tests to `Elsa.Server`'s test surface** — rejected: `Elsa.Server` has no test project today and shouldn't accumulate domain-test responsibility.

---

## R8. Provisional name for `IPromoteDraftToVersionCommand`

**Decision:** Use the provisional name `IPromoteDraftToVersionCommand` in Unit C's plan + tasks. Final allocation is Unit D's call; Unit C does not introduce the command's implementation (only its contract reference in FR-024 + FR-027b).

The provisional name is documented in two places:
- Spec FR-024 + FR-027b explicitly carry "provisional name" wording.
- This research entry is the canonical record of why Unit C does not finalise the name.

Unit D will rename if needed; the rename costs are bounded because the command type doesn't exist in Unit C's deliverables — only references to its name appear in spec prose.

**Rationale.** Cardinality semantics for the Draft → Version promotion command (specifically: what happens when a Draft is promoted while another Draft of the same Definition exists) are Unit D's territory per FR-024. Naming the command without resolving those semantics risks committing to a name that misaligns with the eventual contract.

**Alternatives considered.**
- Finalise the name now (e.g. `IPromoteToVersionCommand`, `ICreateVersionFromDraftCommand`) — rejected: pre-empts Unit D's cardinality decision; if Unit D resolves to a multi-step promotion (validate, freeze, promote, archive-prior), the command name would shift to match the verb structure.
- Use `ICompleteDraftCommand` — rejected: "Complete" is ambiguous (does it mean "mark this Draft as final" or "the Draft's last edit"?).

---

## R9. `"Variable"` expression-type kind string

**Decision:** `"Variable"` (capitalized, exact case).

This convention already exists in the codebase per `src/Elsa.Workflows.Runtime.Core/Models/InputArgument.cs:72`:
```csharp
public InputArgument(IVariable variable) : base(new("Variable", variable), variable, typeof(T))
```

The `Variable-expression resolver` validator (FR-033) MUST detect `expression.Type == "Variable"` using ordinal string comparison (`StringComparison.Ordinal`). No casing normalisation; the convention is exact-case.

**Rationale.** Convention already exists; pinning it formally as part of Unit C documents what's de-facto. The contract surface for expression-type kinds (`IExpression.Type : string` per `Elsa.Expressions.Core/Contracts/IExpression.cs`) is intentionally open-ended; no enum exists by design. Each contributing expression engine documents its own kinds; `Variable` is the in-domain one.

**Alternatives considered.**
- `"variable"` (lowercase) — rejected: contradicts the existing usage.
- Promote to a typed constant in `Elsa.Expressions.Core` (e.g. `public static class ExpressionKinds { public const string Variable = "Variable"; }`) — accepted as an optional refinement; if Unit C implements it, the validator uses the constant. If Unit D or a later unit hasn't added the constant yet, Unit C uses the literal `"Variable"` string. Either way the value is `"Variable"`.

---

## R10. Migration strategy for the `WorkflowMetadata` deletion + `IsRequired` column addition

**Decision:** **Fresh init migration** — regenerate the SQLite migration baseline for both `ActivitiesDesignDbContext` (carries `InputDefinition` / `OutputDefinition` mappings) and `WorkflowsDesignDbContext` (carries `WorkflowDefinition` mappings).

Per Unit B's established convention ("no preserved production data" — see Unit B follow-up §39), incremental migrations are not needed; the SQLite migration baseline is regenerated whenever schema-shaping changes happen.

Concrete steps (executed in Phase /speckit.tasks):
1. Delete existing `Migrations/` folders for both contexts.
2. Apply Unit C model changes (add `IsRequired` to InputDefinition/OutputDefinition; add new entities; remove `WorkflowMetadata`).
3. Generate fresh `Initial` migration per context: `dotnet ef migrations add Initial -c ActivitiesDesignDbContext --project src/Elsa.Activities.Design.Persistence.EFCore.Sqlite`; same for `WorkflowsDesignDbContext`.
4. Verify the generated schema is clean (no orphan tables, correct FKs, `IsRequired` column on the activity-side input/output tables, layout/validation entity tables present).

**Rationale.** Unit B established the precedent and the operational backstop ("no production data preservation in the Unit B–G refactor period"). Continuing it for Unit C keeps the migration story uniform across the refactor. Incremental migrations across this many schema changes would produce a noisy, hard-to-review migration file that's worse than the regen.

**Alternatives considered.**
- **Incremental migrations** — rejected per the established Unit B convention. Operators with deployed production data are not in scope for the refactor period; the convention will revisit post-Unit-G when the refactor stabilises.
- **Hybrid (incremental for `IsRequired`, fresh for new entities)** — rejected: complicates the migration story; the `IsRequired` column addition would land in one migration, the new entities in another; reviewers and operators benefit from a single fresh baseline per context.

---

## Summary

All 10 plan-stage items resolved. No remaining NEEDS CLARIFICATION items block /speckit.tasks. The five provisional constitutional sub-rules continue to ride through the implementation per Constitution Check's complexity-tracking entry; ratification on 2026-06-01 either confirms them (no rework) or revises (small in-branch cascade).
