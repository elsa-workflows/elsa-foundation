# Phase 0 Research — Activity Semantic Versioning

Resolves the five items the spec deferred to plan stage. Each is settled within the sanctioned-patterns catalog (framework §2.24.2); none triggers the §2.24.3 new-pattern gate.

---

## R1 — Semver ordering mechanism (FR-008)

**Decision.** Persist a **normalised, lexicographically-sortable semver key** as a real CLR property `SemVerSortKey` on `ActivityDefinitionVersion`, hidden from `IActivityDefinitionVersion`. `ActivityVersionOrderDefinition` orders **descending by `SemVerSortKey`** (DB-side). Precedence comparison in memory (and the normaliser that produces the key) is owned by a `SemVerComparer` (a Strategy/comparator).

**Why.** EF Core applies `OrderDefinition` to an `IQueryable` — it translates to SQL `ORDER BY`. A human-readable semver string sorts lexically in SQL (`"10.0.0" < "2.0.0"`), which is the exact silent-wrong-answer bug US3/SC-002 forbid. A normalised sortable key keeps ordering **DB-evaluable** (no full-catalog client materialisation for "latest"), correct for multi-digit segments, and correct for prerelease precedence.

**Key shape.** Zero-pad each numeric identifier to a fixed width and encode prerelease so it sorts **below** the release. Concretely:
- Normal version → `MMMMM.NNNNN.PPPPP` zero-padded (width chosen to bound realistic segment magnitude; documented constant).
- Release (no prerelease) ranks **above** any prerelease of the same normal version → append a high sentinel (e.g. `~` / a max marker) for releases and the normalised prerelease identifiers for prereleases (numeric identifiers padded; alphanumeric compared as ASCII per SemVer §11).
- **Build metadata is excluded** from the key (ignored for precedence and equality, FR-011/FR-013).

**Constitution fit.** This is exactly framework §2.9.1 / §2.24.2 row 12 — a persistence-only field that is a real CLR property and omitted from the read interface (NOT an EF shadow property). It is set at row creation and is **immutable** (`[Immutable]`, picked up by the central immutable scanner), satisfying §E2.8 Model X version-row immutability. The comparator is §2.24.2 row 9 (Strategy). **No new pattern.**

**Alternatives rejected.**
- *In-memory comparison after materialisation* — correct but forces "latest version" / ordered listings to pull all version rows for a definition into memory before sorting; breaks the DB-side `OrderDefinition` contract and scales poorly for large catalogs.
- *Store the parsed numeric components in separate columns and `ORDER BY` multiple columns* — viable but spreads the sort semantics across several columns + complicates prerelease ranking; a single normalised key keeps the invariant in one place and one comparator.
- *Computed/shadow column* — violates §2.9.1 (provider shadow mechanism); the immutable scanner and tooling would not see it.

**Equality note (FR-013).** `(DefinitionId, Version)` lookup equality follows SemVer precedence-equality (build metadata ignored). Implementation: compare on the normalised key (which already excludes build metadata) rather than byte-equality of the human string.

---

## R2 — `Elsa.Activities.Runtime.Core` extraction (FR-009 / FR-020)

**Decision (constitution-compliance verdict; NOT ratification).** The extraction is **constitutionally permitted** and is the working direction. It remains **contested by Frans/Sipke** and is carried to the architecture touchpoint — this plan validates compliance; it does not close the debate.

**What moves.** `IActivity`, `ActivityBase`, `IActivityExecutionContext`, and the new `[Version]` attribute move **from `Elsa.Workflows.Runtime.Core` into the existing `Elsa.Activities.Runtime.Core`** (which today holds only activity runtime-service contracts — factory, implementation-resolver). This is a **move into an existing package**, not a greenfield package.

**Dependency-direction validation (the load-bearing FR-020 question).**
- The new edge is `Elsa.Activities.Design.Reconciliation.Clr` (**Design** side) → `Elsa.Activities.Runtime.Core` (**Runtime-activities** abstractions).
- §E2.2's hard rule forbids only `Elsa.Workflows.Runtime.* → Elsa.Workflows.Design.*`. It says nothing against Design referencing a Runtime-activities `.Core`, and **Design→Runtime is the allowed direction** (the reverse is the prohibited one). → **G15 PASS.**
- Module-decomposition (§2.20): no premature umbrella is created (the package exists); the move *reduces* the `Workflows.Runtime.Core` catch-all surface, which §E2.3-style "don't make Core a bucket" reasoning favours. → **G12 PASS.**
- `.Core` purity (§2.3): the moved abstractions carry no heavy deps. → **G3 PASS.**

**Blast radius (FR-018 unification).** Re-typing `IActivity.Version` / `ActivityBase.Version` from `int` to the semver reaches every current `IActivity` implementation. The change is mechanical (type swap + default) and stays within the activity abstraction; it introduces **no** Runtime→Design edge. Existing tests on these abstractions are preserved (§2.21.1).

**Alternatives rejected.**
- *Leave abstractions in `Elsa.Workflows.Runtime.Core`* — preserves the catch-all Joey explicitly wants to dissolve and forces the Design scanner to reference the broader runtime core (more surface than needed).
- *Put `[Version]` alone in a tiny `.Core` and leave the base types* — splits the attribute from the type it annotates; authors would reference two packages to define one activity.

**Touchpoint flag.** Because Frans/Sipke were not convinced of the "version = assembly version" premise that motivates the focused activity-abstractions core, the extraction is listed in the plan's Complexity Tracking and is a **candidate agenda item for the next architecture touchpoint.** Implementation may proceed on the working direction; ratification is separate.

---

## R3 — CLR `SourceId` derivation

**Decision.** `SourceId` is a **configured logical name** on `ClrReconciliationOptions`, defaulting to the **normalised absolute folder path** when not set. `SourceKind` is the constant `"CLR"`.

**Why.** Model X identity is `(SourceKind, SourceId, ActivityTypeKey)`. `SourceId` must be stable and must distinguish two CLR sources configured against different folders within one host (e.g. core activities vs a plugins folder). The folder path is a sensible stable default; an explicit logical name lets an operator keep identity stable if the folder moves. `SourceId`/`SourceKind` are properties the **source owns inline** (per the reconciliation pattern), not options threaded from the reconciler.

**Alternatives rejected.**
- *Hash of the scanned assemblies* — changes when assemblies change, which is the opposite of a stable source identity (it would fork identity on every rebuild).
- *Fixed constant* — collides if two CLR sources are configured.

---

## R4 — Reflection mechanics (CLR type metadata → catalog model)

**Decision.** For each discovered `IActivity` implementation, the scanner reads, by reflection:
- **`ActivityTypeKey`** = the CLR type's **full name** (namespace + type name, e.g. `Elsa.Http.SendRequest`) — FR-022. Not from the mutable `IActivity.Type` instance property; not assembly-qualified.
- **Display name / description / category** = from activity-metadata attributes if present, else conventional defaults derived from the type (documented).
- **Inputs / outputs** = from the activity's input/output property declarations (the existing convention used by the activity model).
- **DesignFacets** = from the activity's declared port metadata.
- **Version** = `ActivityVersionResolver`: `[Version]` attribute if present (validated SemVer 2.0.0), else the declaring assembly's version resolved per R5/FR-020.

Each discovered activity becomes one `ActivityVersionReconciliationModel` with `ImplementationKind = "CLR"` and a `ClrImplementationDescriptor`.

**Fault handling (FR-023).** Whole-DLL load/reflect failure → log-warning-and-skip (resilient scan). A discovered activity with an **invalid** `[Version]` value or an **unresolvable** assembly version → domain-scoped exception (framework §2.23.5; no raw `FormatException`/reflection exception escapes the source boundary). Identity collisions are the reconciler's Model X concern (hash-mismatch path).

**Why reflection-only.** The scanner needs *metadata* (types, attributes, property shapes), never execution. It must not run activity constructors or static initialisers, and must not require the activities' transitive runtime deps to be loadable/executable.

---

## R5 — Load-context choice

**Decision.** Use **`System.Reflection.MetadataLoadContext`** (reflection-only) to load and inspect the folder's assemblies, with a `PathAssemblyResolver` seeded from the folder + the runtime's trusted-platform assemblies. Multi-version assembly-load-context loading of the same type stays **out of scope** (per the Unit 3 follow-up).

**Why.**
- **No execution, no ALC pollution.** `MetadataLoadContext` never runs code and never loads assemblies into the default `AssemblyLoadContext`, so scanning cannot break or mutate the running host. This directly supports the resilient-scan requirement (FR-023) and avoids version-conflict hazards when a scanned DLL ships a different version of a dependency the host already loaded.
- **`AssemblyInformationalVersion` is readable as metadata.** R-assembly-version resolution (FR-020: prefer `AssemblyInformationalVersionAttribute`, else map the 4-part `AssemblyVersion`'s `Major.Minor.Build` → `MAJOR.MINOR.PATCH`) reads custom attributes — fully available in a metadata-only context.

**Cost / caveat.** `MetadataLoadContext` requires resolving the closure of referenced assemblies for the resolver (missing references surface as load failures). That is acceptable: a DLL whose references cannot be resolved is exactly the "log-and-skip" case in FR-023.

**Alternatives rejected.**
- *`Assembly.LoadFrom` into the default ALC* — simplest, but loads (and can execute static init of) arbitrary DLLs into the live host, risks dependency-version clashes, and cannot be unloaded; contradicts the reflection-only, resilient-scan intent.
- *A collectible custom `AssemblyLoadContext`* — unloadable and isolated, but actually *loads* (executes) and is heavier than needed for pure metadata reading; reserved for the out-of-scope multi-version execution scenario.

---

## Cross-cutting confirmations

- **Sanctioned-patterns check (§2.24.2).** All resolutions map to catalogued patterns: contribution source (row 3/3b), Strategy/comparator (row 9), domain-level shadow property (row 12), provider module decomposition (row 11). **No §2.24.3 gate.**
- **No FluentAssertions.** All new tests are xUnit-only (constitutionally pinned).
- **Constitution edits are in-unit (FR-016):** §E2.8 reworded for string semver + assembly-sourced version + `[Version]` override; module-decomposition wording updated if the extraction is adopted.
