# Research: Consumer Contract Fragments as Build Output

**Feature**: 149-consumer-contract-fragments | **Date**: 2026-08-08

Facts below were verified against the tree at branch point (`2ba15c2ae`). File references are current as of that commit.

## R1 — How the "shared projection library" is realized

**Decision**: The RFC's "one shared projection library" is realized as the *existing product descriptor pipeline*, composed by the new emitter CLI — not as a new parallel library:

- **Activity contracts**: `ClrAssemblyScanner` (`src/Elsa/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`, `public sealed`) is the single place CLR activity facts are minted (typeKey, inputs, outputs, design facets/ports, version). The emitter drives the same scanner against built assemblies. The runtime catalog serves persisted rows reconciled from the same scanner — per constitution §E2.8 the endpoint must NOT re-project live types, so the shared seam is the scanner, not the handler.
- **Payload/option schemas**: `AuthoringSchemaExporter` (`src/Elsa/Workflows/Design/Api/Services/AuthoringSchemaExporter.cs`) is promoted `internal` → `public` so the emitter exports structure payload schemas with the identical wire-coupled serializer the endpoints use.
- **Structure kinds**: the emitter instantiates the assembly's `IActivityStructureHandler` implementations and reads `Kind`/`SchemaVersion`/`SupportsScopedVariables`/`AuthoredPayloadType` — the same instances `ListActivityStructuresHandler` enumerates at runtime.
- **Expression surface**: the emitter instantiates `IExpressionDescriptorProvider` implementations (same descriptors `ListExpressionDescriptorsRequestHandler` serves) and invokes `IJavaScriptDeclarationContributor` implementations against a fresh declarations context.
- **Feature metadata**: `[ShellFeature]` attribute data (id, DependsOn, display/description) read via MetadataLoadContext; options via the same manifest-hint projection `ManifestHintReader` performs (reference it if public; otherwise reproduce its documented mapping — decide at implementation, prefer reference).
- **Activity↔feature attribution**: the emitter uses the identical rule as `ActivityFeatureAttributionResolver` (assembly → feature whose startup type lives in it; ties broken by min-ordinal feature id).

**Rationale**: two code paths projecting the same types is the drift generator the RFC names as the key risk. Every fact in a fragment is minted by code the runtime also executes. G1/G2 are implemented inside that shared code so both surfaces change together.

**Alternatives considered**: (a) extracting a new `Elsa.Contracts.Core` src package now — rejected as a premature package (§2.20 Rule 1 spirit): no product code consumes fragment models in steps 1–2; the RFC step-5 endpoint flip is the first product consumer and the extraction is cheap then. (b) Re-implementing projection in the tool — rejected outright (parallel truth).

## R2 — G1 mechanism: capture CLR property-initializer defaults without executing code

**Problem**: `ClrAssemblyScanner` runs reflection-only (`MetadataLoadContext`) and never instantiates activity types, so `HttpEndpoint.ResponseMode { get; set; } = ResponseMode.Async;` is invisible; `InputDefinition.DefaultValue` is populated only from `ActivityInputAttribute.DefaultValue` (a string).

**Decision**: add an **IL initializer analyzer** (`InitializerDefaultReader`, System.Reflection.Metadata-based) to the scanner. It walks the activity type's parameterless-constructor chain and interprets the simple, compiler-generated initializer pattern (`ldarg.0; <constant load>; stfld <backing field>`), recognizing constant loads only: `ldc.i4.*`/`ldc.i8`/`ldc.r4`/`ldc.r8`/`ldstr`/`ldnull` (+ trivial `conv.*`). The effective static default per input is then:

1. `ActivityInputAttribute.DefaultValue` when declared (author-explicit wins — current behavior preserved);
2. else the IL-derived initializer constant when the pattern matches;
3. else `default(T)` for non-nullable value types (this is the truth: an unauthored input leaves the constructed property value in place — for `ResponseMode`, `Async` = 0 either way);
4. else (reference/nullable types without initializer) an explicit `null` default;
5. any initializer that is *not* a recognized constant pattern (computed values, `newobj`, method calls) → **no static default**, represented distinctly from `null` (spec edge case), never a guess.

Defaults are serialized to wire form (same options as the served catalog: camelCase, string enums) into `InputDefinition.DefaultValue` (`JsonElement?`), exactly the field the catalog view already exposes.

**Rationale**: no arbitrary code execution during reconciliation or emission (activity constructors can have side effects; MetadataLoadContext cannot execute anyway); deterministic; the fallback ladder makes G1 a total function with an honest "not static" escape hatch.

**Alternatives considered**: (a) instantiate activity types in a collectible ALC and read properties — rejected: executes arbitrary constructors in the reconcile path and fails for scan-only folder assemblies; (b) require authors to duplicate defaults on the attribute — rejected: authoring burden + drift, contradicts "the generator emits what the code already knows".

## R3 — G2 mechanism: output requiredness

**Decision**: `OutputDefinition.IsRequired` already exists and is correctly populated (from `[Output]`, whose `IsRequired` defaults `true` — `HttpEndpointResult.Request`/`RouteData` are required, `ParsedContent` opts out). The gap is purely the API view: `ActivityOutputDescriptorView` drops it. Fix: add `IsRequired` **and** `ReferenceKey` (also currently dropped; needed so a consumer can bind the output it is told is required) to `ActivityOutputDescriptorView`, mapped in `ListActivityAuthoringCatalogRequestHandler.ToView`. Additive per spec FR-014. Fragments carry both from `OutputDefinition` directly.

**Enforcement note**: required-output binding is enforced at publish compile (`RuntimeOutputCaptureCompiler.Compile` throws on a missing required target), not at draft validation — the fragment/catalog now states what the publish compiler will demand.

## R4 — Emitter integration: MSBuild target + opt-in + completeness guard

**Decision**:
- `tools/contracts/ContractFragments.targets`, imported conditionally from `src/Elsa/Directory.Build.targets`, gated on `<EmitContractFragment>true</EmitContractFragment>` set in each contributing project's csproj.
- Build ordering: contributing projects get a `ProjectReference` to the CLI project with `ReferenceOutputAssembly=false` (injected by the targets file) so the CLI is always built first; the target invokes the built CLI via `dotnet exec` with `Exec`, passing `@(IntermediateAssembly)`, `@(ReferencePath)`, and output paths.
- Diagnostics: the CLI writes canonical MSBuild-format lines (`file(line): warning ELSACT001: …` / `error ELSACT00N: …`); `Exec` surfaces them natively to IDEs, CI, and agents (RFC resolved position 2).
- **Completeness rule enforcement** (spec FR-001): opt-in alone would allow silent omission, so `CompletenessGuardTests` (in the new test project) scans all built src assemblies for contribution markers (non-abstract `IActivity` implementations, `IActivityStructureHandler`, `IExpressionDescriptorProvider`, `IJavaScriptDeclarationContributor`, `[ShellFeature]`) and fails when a contributing assembly lacks an embedded fragment resource. New surface kinds added later extend the guard, inheriting the obligation.

**Rationale**: running the emitter on all 153 projects taxes every build for nothing on ~130 non-contributors; opt-in + a mechanical guard gives the same completeness guarantee at a fraction of the cost. Precedent for the target shape: the external `Elsa.Platform.PackageManifest.Generator` package (already consumed repo-wide) wires `GenerateElsaPackageManifest` `AfterTargets="Build"` with a MetadataLoadContext task — ours differs by being repo-local and process-isolated (Exec, not in-proc task; per RFC resolved position 2).

## R5 — Embedding the fragment as an assembly resource

**Problem**: the fragment is projected *from* the compiled assembly, but manifest resources are attached *at* compilation — a chicken-and-egg.

**Decision**: post-compile resource injection. The CLI's `emit` command, after producing `<AssemblyName>.contract.json`, injects it into the intermediate assembly (`obj/**/<Assembly>.dll`) as manifest resource `elsa.contract.json` using **Mono.Cecil** (tool-only dependency; symbols rewritten in sync), running `AfterTargets="CoreCompile"` / before output copy. Fragment content depends only on the compiled *types*, which the injection does not alter, so the operation is idempotent-by-content and deterministic.

**Rationale**: single compile pass; battle-tested technique (Fody ecosystem); keeps the resource byte-identical to the emitted file (the later RFC step-5 "serve the same bytes" flip depends on this identity).

**Alternatives considered**: (a) WPF-style double compile — rejected: doubles build cost of contributing projects and re-implementing the `Csc` invocation is fragile; (b) sidecar file next to the assembly instead of embedding — rejected: RFC explicitly requires embedded resources from day one.

## R6 — Fragment identity and granularity

**Decision**: **one fragment per contributing assembly**, named by assembly simple name (`fragments/Elsa.Activities.Http.json`). Inside, every contribution entry carries the owning `featureId`, attributed by the same deterministic rule as `ActivityFeatureAttributionResolver` (assemblies hosting multiple features: min-ordinal feature id wins ties). Consumers filter the merged set by intersecting `featureId` with their shells.json.

**Rationale**: the emitter's natural unit is the assembly (that is what a build produces and what gets embedded); the RFC's consumer-filtering contract needs feature ids on entries, which attribution provides; per-feature *files* would force the emitter to split one assembly's output and complicate embedding.

## R7 — Deterministic serialization + fingerprints

**Decision** (mirrors `Elsa.Maps.Generator` conventions + spec-148 fingerprint format):
- JSON written with ordinal-sorted object keys, 2-space indent, LF line endings, UTF-8 without BOM, invariant culture; values in wire form (camelCase property names, string enums — same `JsonSerializerOptions` family as `AuthoringSchemaExporter`).
- Per-fragment fingerprint `sha256:<lowercase hex>` over the fragment file bytes, recorded in `docs/contracts/manifest.json`.
- Per-activity content hash: reuse `DefaultActivityDefinitionHasher`'s canonical form so the fragment's activity hash equals the persisted `ActivityDefinitionVersion.Hash` for the same content (uppercase hex, no prefix — kept as-is for row-identity; the `sha256:` form stays at file level). One projection extends to hashing.

## R8 — `docs/contracts/` merge + CI check

**Decision**:
- `merge` command: collects fragments from built src assemblies (embedded resources of opted-in projects), writes `docs/contracts/fragments/*.json`, `submit-schema.json` (via `AuthoringSchemaExporter.ExportSchemaNode(typeof(SubmitDefinition))` — joins as-is per RFC), and `manifest.json` (schema_version, generator id, per-fragment fingerprints, counts). Merge fails loudly if any expected fragment is unreadable (spec edge case: no silently partial contract set).
- `check` command: regenerates to a temp dir and **byte-compares** each file against the committed copy, `MapFreshness`-style (exit 1 + regenerate-and-commit remediation text). `manifest.json` is *included* in the comparison (unlike maps): fragment fingerprints are contract, not bookkeeping.
- CI: a step appended to the existing `build-and-test` job in `ci.yml` after `dotnet build` (`dotnet run --project tools/contracts/Elsa.Contracts.Generator -- check`), reusing the build output. A separate job would need its own full solution build.
- Unlike maps (human-initiated refresh), contract regeneration is expected in the same PR that changes the surface — the check failing on a contract-affecting PR *is* the reviewer signal (spec US3).

## R9 — Equivalence test (one-projection rule, phase 1)

**Decision**: `EquivalenceTests` composes a representative host in-process (pattern: `WorkflowsDesignTestHost` + feature `ConfigureServices` composition) with the feature set: Activities Design API + Reconciliation.Clr + Sequence + Flowchart + ControlFlow + Http + JavaScript/Expressions + Groundwork SQLite persistence. It runs CLR reconciliation, dispatches `ListActivityAuthoringCatalog(Availability: All)` and `ListActivityStructures` at the mediator level, then asserts:

- catalog items for CLR-provided activities == fragment activity entries of the composed features, after stripping the server-state overlay (`ActivityVersionId`, `Available`, `AvailabilityReason`, `Provenance`) and normalizing the server-generated template boilerplate;
- intrinsic catalog items == the intrinsics fragment;
- structure registry == fragment structure entries (including payload schemas);
- dynamic/store-fed activities are additive (assert union semantics with a test-registered reusable activity).

Fragments come from the embedded resources of the referenced assemblies (the same bytes the merge committed — verified byte-equal by `check`).

**Version wrinkle (accepted)**: activity versions derive from assembly versions; `ci.yml` builds without `/p:Version` injection, so committed fragments and the equivalence run see the same stable assembly version. Version-injected pipelines (`packages.yml`, docker) do not run the check.

## R10 — Expression-language surface capture

**Decision** per sub-surface:
- **Expression descriptors**: instantiate `IExpressionDescriptorProvider` implementations (parameterless), record `Type`/`DisplayName`/editing mode — identical data to `expressions/descriptors`.
- **JS design-time declarations**: invoke `IJavaScriptDeclarationContributor` implementations against a fresh declarations context; record the contributed declarations per contributor (e.g. `HttpTypeDeclarationContributor`, `BindingPureArgsDeclarationContributor`).
- **Jint sandbox globals** (`getVariable`, frozen `args`/`variables`, per-variable `get<Name>()` accessors): these are installed imperatively in `IsolatedJintEngine` and are not discoverable. Introduce a small **declarative sandbox-surface catalog** (public static readonly data in the Jint feature package) that the fragment projects, **pinned by a guard unit test** that composes the engine and asserts the declared globals exist and no undeclared ones do. Dynamic per-variable accessors are described structurally as a pattern entry (`kind: "perVariableAccessor"`), not per-name.

**Rationale**: the catalog+guard-test pair keeps the declared surface from drifting without refactoring the engine; contributor instantiation reuses runtime types (one projection). A provider/contributor whose constructor requires dependencies fails emission with a canonical diagnostic — surface contributors must stay dependency-free to be contract-projectable (documented in `docs/contracts/README.md`).

## R11 — Model X hash impact (deployment risk, must be surfaced)

Enriching `InputDefinition.DefaultValue` (G1) changes `DefaultActivityDefinitionVersionHasher` output for the same logical content. Under Model X, reconciling against a database holding pre-G1 rows for the same `(DefinitionId, Version)` throws `ActivityVersionHashMismatchException` — by design ("same identity, different content").

**Mitigations** (no code change):
- local/e2e convention is already fresh-DB-on-rebuild (`e2e-tests/README.md`);
- CI/docker images carry new assembly versions per build → new activity versions → new rows;
- the consumer-workspace validation (delivery protocol) uses a freshly built image and fresh DB.
- Call this out explicitly in the RFC comment and hand-off notes: an in-place upgrade of a long-lived database to this branch requires either a version bump of activity assemblies or a reset of the activity catalog store.

## R12 — What stays out (scope fences)

- No `[ConsumerNote]`, no F1–F3 analyze findings, no `docs/consumer-guide/`, no NuGet content-file shipping, no resource-backed endpoint serving (RFC steps 3–5). The only forward accommodations: fragments self-declare `schemaVersion` (semver string, additive evolution documented in the schema contract) and are embedded as resources.
- No assigned version ids or availability in fragments (server state).
- No changes to existing routes, permissions, fingerprint contracts, or persisted shapes beyond the additive output-view fields and the scanner's richer `DefaultValue` (which flows into *new* rows only).
