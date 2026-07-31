# Simplification Review — YAGNI, DRY, and Accretion Pressure

**Date:** 2026-07-31
**Working tree:** `claude/elsa-foundation-review-yn8y3v` at `acc00611`
**Question asked:** *is the code overly complicated? Apply YAGNI. Find superfluous code. Is it DRY?
Can it be simplified? Modernized? Less is more.*

**Method:** static census of the working tree (file/line/type counts, cross-reference and
reachability analysis over `.csproj` graphs, grep-verified pattern counts) plus targeted reading.
Every count below is reproducible — see [Appendix A](#appendix-a--how-the-numbers-were-taken).

> **Verification caveat.** This review is measurement and reading only. The environment it was
> produced in has no .NET SDK, and the four private feeds in `NuGet.config` (CShells, Nuplane,
> Elsa 3, Groundwork) are unreachable from it — 94 projects reference `CShells.*`, 92 reference
> `Elsa.Platform.*`, 27 reference `Groundwork.*`. **Nothing here was compile- or test-verified.**
> Findings that depend on compilation (single-implementation interfaces, unused public members,
> the exact `internal` blast radius) are marked as needing a build before action.

---

## 1. Executive summary

**The C# is in better shape than the concern implies.** The prior review's DRY inventory
(navigators, scheduler handlers, store bridges, mediator stacks) really was closed by W10/W12/W13.
There is **zero** `TODO`/`HACK`/`FIXME` and **zero** `NotImplementedException` in `src/`. One clock
abstraction (`TimeProvider`, 299 uses) with no `DateTime.UtcNow` anywhere. File-scoped namespaces
throughout; primary constructors in 1,039 files; `is null` 2,074 vs `== null` 44; five `[Obsolete]`
members, all carrying migration messages. This is a disciplined codebase.

**The complexity is real but it is structural, not textual.** In roughly four weeks the tree went
from 109 to **148** projects, ~84.3k to **241.7k** lines, and 1,809 to **4,274** public types. The
mechanism is that every per-work-unit obligation — a new project, an extension-point catalog, a
spec directory, evidence records, guard tests — is *additive*, applied at ~5 PRs/day of fleet work,
with no countervailing consolidation ritual anywhere in the operating model. Nothing decided was
wrong. Everything ratchets one way.

**The three highest-leverage findings:**

| # | Finding | Size |
|---|---|---:|
| 1 | **89% of types are `public`** — 4,274 public vs 517 internal top-level; 744 public classes in `Services`/`Handlers`/`Stores` folders shipped as semver-committed API across ~148 packages | 744 classes |
| 2 | **Zero style or analysis enforcement** — no `.editorconfig` anywhere, no `TreatWarningsAsErrors`, no `EnableNETAnalyzers`. All the discipline above is habit, held up by code review alone | repo-wide |
| 3 | **245 of 246 `.csproj` hand-repeat the same three properties** while `Directory.Build.props` sits deliberately metadata-only | ~735 lines |

---

## 2. Census

| Surface | Files | Projects | Lines |
|---|---:|---:|---:|
| `src/` C# | 2,692 | 148 | 241,716 |
| `tests/` C# | 1,201 | 93 | 286,312 |
| Markdown (repo-wide) | 1,362 | — | 126,718 |
| `specs/` | 1,285 | 175 dirs | — |
| `tools/` scripts | 12 | — | 3,720 |

`src/` file-size distribution: mean 90 lines, median 38, p90 216, max 2,270. 692 files under 20
lines; 171 under 10.

Per-domain (`src/Elsa/*`), largest first: Workflows 95,609 LoC / 29 projects · Activities 52,246 /
23 · Persistence 28,161 / 16 · Diagnostics 14,005 / 13 · Foundation 12,896 / 8 · Agent 6,012 / 5 ·
Modularity 5,839 / 5 · Expressions 4,641 / 10 · Secrets 3,321 / 5. The tail (Attention 623 / 2,
Mediator 595 / 2, Caching 258 / 2, Pipelines 202 / 1, Git 178 / 1, Locking 138 / 2) is where the
project-per-concept granularity is most visible.

---

## 3. What is good — protect these

Listed explicitly because a simplification pass is exactly when good properties get traded away by
accident.

- **Zero rot markers.** No `TODO`, `HACK`, `FIXME`, or `NotImplementedException` in `src/`.
- **One clock.** `TimeProvider` used 299×; zero direct `DateTime.UtcNow`.
- **Modern C# is already the norm** — see §6; the remaining gap is a tail, not a backlog.
- **Suppressions are disciplined.** 16 `#pragma warning disable`, each with an explanatory comment
  and mostly the project's own `GW0001`–`GW0004` Groundwork analyzer codes; 11 `#nullable disable`,
  all in generated EF migration files.
- **Deliberate deprecation.** 5 `[Obsolete]` members, all with a named migration path.
- **The endpoint visibility pattern is already right.** FastEndpoints endpoints are `internal
  sealed` (`src/Elsa/Modularity/Api/Endpoints/Apply.cs`,
  `src/Elsa/Attention/Api/Endpoints/GetAttentionItems.cs`). Finding A1 is asking for that existing
  pattern to be applied to services, not for a new convention.

---

## 4. YAGNI and superfluous surface

### A1 — 89% of types are public *(highest leverage)*

4,274 top-level `public` type declarations against 517 `internal`. Within `Services/`, `Handlers/`
and `Stores/` folders specifically: **744 public classes vs 66 internal**.

These are implementation details published as semver-committed API across ~148 NuGet packages.
Nothing is deleted by narrowing them — but the public contract shrinks by an order of magnitude,
and the surface grew 2.4× in a month, so the cost compounds.

The right method is compile-driven: apply `internal sealed` domain by domain and let the `tests/`
build reveal what genuinely must stay public. Anything a test reaches that shouldn't be public is
either a type that belongs in the contract or a test reaching past a seam — both worth knowing.
Note the constitution bans `InternalsVisibleTo` (MD-3 guard, 5 pre-existing uses), so the rule is
*prefer leaving a type public over adding a friend assembly*.

**Needs a build.** Do not attempt blind.

### A2 — 22 of 148 `src` projects are unreachable from the app host

`src/Apps/Elsa.Server/Elsa.Server.csproj` has 87 direct project references and transitively reaches
126 of 148 projects. The other 22 are alive only via test-project references:

- **6 EF Core projects** — owned by `specs/144-zero-ef-final-removal`, in flight. Leave alone.
- **4 `Elsa3.*`** and **4 `Workflows.Design.Reconciliation.*`** — migration/compatibility
  boundaries, legitimately separate under §E2.7.
- **MongoDb / SqlServer Groundwork providers** (4) — alternative providers, legitimately unwired.
- **`Agent.Anthropic`** (447 LoC) and **`Expressions.Liquid`** — no host composes them and no
  boundary rule requires them to exist. These two need an explicit keep-or-drop verdict rather
  than continued drift.

### A3 — Micro-projects, and the §2.16.1 tension

46 of 148 projects (31%) have ≤5 source files; 15 have ≤2; 3 have exactly 1. The smallest:

| LoC | Files | Project | Contents |
|---:|---:|---|---|
| 32 | 2 | `Workflows.Design.Reconciliation.Core` | 1 interface + 1 delegate |
| 46 | 1 | `Serialization.Newtonsoft` | one feature class |
| 47 | 1 | `Workflows.Runtime.Tracing` | one feature class |
| 47 | 2 | `Workflows.Design.JavaScript` | one feature class + README |
| 51 | 2 | `Locking.Core` | 2 interfaces |
| 63 | 3 | `Expressions.JavaScript.Core` | — |
| 68 | 4 | `Workflows.Primitives` | constants + 1 model |
| 73 | 3 | `Caching.Core` | 3 interfaces |
| 83 | 2 | `Activities.Scripting` | one feature class |
| 85 | 3 | `Http.JavaScript` | constants + contributor + feature |

**This is a governance question, not a code defect.** Framework constitution §2.16.1 was ratified
2026-07-04 — after the review that raised it — precisely to legitimise this shape, with a six-class
exemption test. Every project above passes that test. The finding is therefore recorded as a
*challenge to the amendment*, not a merge proposal:

> §2.16.1 was ratified against a 13-project sub-100-LoC population and concluded "0 of 13 are
> forced merge candidates." The population has since grown and the project count went 109 → 148 in
> four weeks. The amendment's own reasoning — that a hard LoC gate would fight §2.16 and force
> violations of provider-isolation and cross-`.Core` composition gates — still holds for
> *individual* projects. What it does not address is the **aggregate rate**: nothing in the gate
> asks whether 148 assemblies for 242k LoC is the right total, only whether each one is defensible
> in isolation. A per-item exemption test cannot answer an aggregate question.

Recommended framing for a future amendment: keep the exemption classes, add a *review trigger* on
the aggregate (e.g. when project count grows faster than LoC over a window, the growth itself gets
a look), rather than a per-project size gate.

**No restructuring performed.** This is Sipke's call.

### A4 — Guard-test machinery

`tests/Elsa/Architecture` is 14,490 lines for 300 test methods (~48 lines per test) because most of
it is scanner and validator infrastructure rather than tests:

| File | Lines |
|---|---:|
| `EfCoreSurfaceScanner.cs` | 1,607 |
| `GroundworkCoverageLedgerValidator.cs` | 1,381 |
| `GroundworkCoverageLedgerTests.cs` | 1,335 |
| `EfCoreSurfaceRatchetTests.cs` | 1,131 |
| `ArchitectureGuardTests.cs` | 1,123 |

**This is not on the cut list, and that is deliberate** — see §8. Guard tests are what keeps a
fleet-driven repo honest; the ~2,900 lines of EF-ratchet machinery is already owned by an in-flight
spec, and the remainder enforces live gates.

### A5 — Defensive null guards

1,852 `ArgumentNullException.ThrowIfNull` call sites across 631 files, in a codebase where every
project sets `<Nullable>enable</Nullable>`. Densest: `WorkflowDispatchRecord.cs` (25),
`GroundworkOpenTelemetryStore.cs` (19), `GraphActivityScope.cs` (18).

Legitimate on genuinely public API entry points; noise on DI-injected constructor parameters the
container can never pass null to. Needs a policy decision — *guard at the package boundary, trust
the compiler inside* is the usual line — before a mechanical sweep. **Not actioned.**

Related: 808 null-forgiving `!` operators, clustered densely in the Publishing API
(`SourceOwnedActivityVersionPublisher.cs`, `PublishWorkflowRequestHandler.cs`). A cluster that
dense is either guaranteed-non-null by a design that should be modelled, or under-modelled
nullability. Worth one focused pass; needs a build.

---

## 5. DRY

### B1 — Build configuration repeated 245 times

`Directory.Build.props` exists but is metadata-only by explicit comment ("must not change build
behavior"). Consequently every project independently declares:

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
```

245 of 246 `.csproj` repeat it (~735 lines). One legitimate outlier —
`src/Elsa/Workflows/Design/CodeGeneration/` targets `netstandard2.0` as a Roslyn source generator.
Also `IsPackable=false` × 97 and the `Elsa.Platform.PackageManifest.Generator` `PackageReference`
× 93.

Formatting has drifted with it: 18 files tab-indented, 5 collapse the whole `PropertyGroup` onto one
line, and `Elsa.Server.slnx` mixes `/` and `\` path separators across its 245 entries.

### B2 — `JsonSerializerOptions` repeated 91 times — but **do not merge the wrappers**

*Revised after inspection. The first pass of this review called the ~8 frozen-options wrapper classes
"near-identical" and recommended collapsing them. That was wrong, and the correction is the more
useful finding.*

`new JsonSerializerOptions(JsonSerializerDefaults.Web)` does appear 91× across 84 files, and the
wrappers do cite each other in their own doc comments (`IdentityGroundworkJson`: *"Mirrors the
Secrets Groundwork options"*; `GroundworkDesignJson` and `GroundworkActivitiesDesignJson` both cite
*"the same convention the runtime bridge's `GroundworkRuntimeJson` follows"*). Reading them, though,
they hold **at least three genuinely different converter configurations**:

| Wrapper | Configuration |
|---|---|
| `GroundworkDesignJson`, `GroundworkActivitiesDesignJson` | Web defaults only |
| `SecretsGroundworkJson` | Web + `JsonStringEnumConverter` |
| `IdentityGroundworkJson` | Web + `JsonStringEnumConverter` + `ReadOnlyStringSetJsonConverter` |

Each is a **frozen persistence contract**, not a style choice — `IdentityGroundworkJson`'s own
comment says freezing the options "keeps the on-disk shape deterministic, which the golden-fixture
drift test relies on." Merging them onto one options instance would silently change how enums
serialize for the modules that currently omit the converter: a breaking change to already-persisted
documents that the type system cannot catch.

The envelope-versioning code around them differs materially too, and is not copy-paste:
`DistributedGroundworkDocuments` enforces strict schema-version equality and refuses anything else,
while `PublishingGroundworkDocumentSerialization` carries a per-document-kind version map plus an
upcaster registry.

**What is actually shared is two lines of construction idiom.** The remaining honest options are a
factory with explicit opt-in layers (`GroundworkJson.Web()`, `.WithStringEnums()`) that keeps each
module's contract declared and separate — a small win — or leaving it alone. Either way this is
**not** a consolidation, and it must not be attempted without running the golden-fixture drift tests.

This is the same shape of trap as C3 below: "these look alike, therefore merge" is the obvious YAGNI
read, and here the near-identical text encodes deliberately independent contracts.

### B3 — `AdaptiveIntervalSchedule` exists five times

Four standalone copies, 15–17 lines each, all different hashes, under
`src/Elsa/Workflows/Runtime/`: `ReferenceGarbageCollection/`, `Scheduling/`, `Distributed/Services/`,
`Resumption/`. A **fifth** is nested privately as `WorkflowAlterationAdaptiveIntervalSchedule` inside
`Services/Alterations/WorkflowAlterationOrchestrationPumpTask.cs` — identical logic, invisible to a
filename search, which is why the first count missed it.

All five class bodies are byte-identical; only the namespace and the doc-comment wording differ. The
type belongs in `Elsa.Tasks.Schedules` beside its fixed-interval sibling `IntervalSchedule` and the
`ScheduledTaskExecution` it delegates to — not in the runtime domain at all. All five consuming
projects already referenced `Elsa.Tasks.Schedules`, so the move needed no new dependency edge.

### B4 — Error codes have no home, and the obvious home is blocked by the Design↔Runtime seam

No `ErrorCodes` constants class exists anywhere. ~20 dotted literals repeat 3–25× each:
`"activity.request.invalid"` ×25 across 17 files, `"activity.action.forbidden"` ×15,
`"activity.operation.failed"` ×10, `"activity.publication.invalid"` ×8, plus per-activity
`"elsa.*.structure"` families. These are a **wire contract** — a typo in any one site silently mints
an unmatched code that no client can match on, and nothing detects it.

The placement is the interesting part, and it rules out the obvious answer. The `activity.*` codes
span **three** domains:

| Code | Domains |
|---|---|
| `activity.request.invalid`, `activity.operation.failed` | Activities/Design, Workflows/Publishing, Workflows/Runtime |
| `activity.provider.failure` | Activities/Design, Workflows/Publishing |
| `activity.cursor.expired` | Activities/Design, Workflows/Runtime |

So the constants cannot live in `Activities.Design.Core`: `Workflows.Runtime` would have to reference
it, and that breaks the **Design↔Runtime seam**, which is not merely convention — it is enforced by
`ArchitectureGuardTests` (`DeferredRuntimeDesignReferences` carries exactly one tracked exception,
plus a spec-006 test asserting no project in the activity-construction runtime path references any
Design project).

**The only correct home is `Elsa.Primitives`** — zero-dependency by §2.3, and exactly the case §2.17
describes ("a shared helper in `<App>.Primitives` … only when ≥3 consumers"). Two of the three
consumer `.Core` projects already reference it; `Elsa.Workflows.Publishing.Core` does not, so the
change adds one project-reference edge to the app's own primitives library (not an external
dependency, so §2.17 is satisfied).

**Not done in this pass.** It is a cross-domain contract placement touching ~100 call sites in
projects that need the private feeds to compile — an architectural decision that deserves its own
work unit rather than a drive-by in a review PR. The constraint above is the part worth carrying
forward: anyone attempting this who reaches for `Activities.Design.Core` will fail the guard suite.

### B5 — Every tool is written twice

`tools/maps/` ships 5 PowerShell generators (1,413 lines) **and** 5 bash generators (1,562 lines)
implementing the same logic, plus `tools/architecture/restore-zero-ef-certification.{ps1,sh}` (430 +
315). ~1,700 lines kept in lockstep by hand, with no test proving the two implementations agree.

A single .NET tool removes the class of bug entirely. Precedent exists on both sides:
`tools/groundwork/Elsa.Groundwork.ProviderEvidenceImporter` is already a .NET console project, and
`.config/dotnet-tools.json` already carries `groundwork.tool`. The 10 script paths should survive as
thin shims — AGENTS.md, `docs/maps/README.md` and ~17 spec `tasks.md` files document those exact
invocations. CI is unaffected: its only `tools/` use is `tools/performance/`.

Correctness bar for the port: regenerate all 11 files listed in `docs/maps/manifest.json`'s
`generated_files` and diff byte-for-byte; `input_fingerprint` must not change.

### B6 — The four `*.Unified` provider features

Sqlite (95 LoC), MongoDb (83), SqlServer (73), PostgreSql (70) are structurally identical — same
feature shape, same options class, same `[ManifestSetting]` blocks — differing in connection string
and a handful of provider-specific toggles. A shared base with provider overrides removes the
copy-paste. **The four assemblies stay**: provider isolation (§2.7) mandates them.

### B7 — SSE header literal

`"X-Accel-Buffering": "no"` verbatim in three streaming endpoints:
`Diagnostics/StructuredLogs/Endpoints/StreamEndpoint.cs`,
`Diagnostics/OpenTelemetry/Endpoints/StreamEndpoint.cs`, `Agent/Api/Endpoints/StreamSession.cs`.

---

## 6. Modernization

### C1 — No enforcement at all *(the real finding)*

- **No `.editorconfig` anywhere in the repository.**
- No `TreatWarningsAsErrors` / `WarningsAsErrors` beyond a single `GW0004` append in
  `src/Elsa/Directory.Build.targets`.
- No `EnableNETAnalyzers`, `AnalysisLevel`, or `AnalysisMode`.
- No `GenerateDocumentationFile`, despite pervasive `<summary>` XML docs that consumers never get.

Everything in §3 is convention-by-habit. Nothing stops it regressing, and at ~5 PRs/day of fleet
work the odds are not favourable. A root `.editorconfig` codifying what the code already does costs
nothing to adopt and locks in gains already paid for.

### C2 — The remaining tail

| Pattern | Count | Note |
|---|---:|---|
| classic ctor + `private readonly` assignment | 221 files | ~83 mix both styles in one file |
| `new List<T>()` / `new Dictionary<>()` / `Array.Empty<T>()` | 265 / 113 / 68 | collection expressions apply |
| `.GetAwaiter().GetResult()` | 37 | zero `.Wait()`; the 10 `.Result` hits are domain `Result` records, not tasks |
| stray `ConfigureAwait(false)` | 6 | against 4,354 `await` sites and an otherwise-universal no-`ConfigureAwait` convention |
| `Console.WriteLine` in `src/` | 3 | should be `ILogger` |
| `async void` | 3 | all reviewed; at least one is a legitimate `TimerCallback` |

### C3 — Package pins are undocumented, not unused

Two pins in `Directory.Packages.props` are referenced by no `<PackageReference>` anywhere:
`Microsoft.OpenApi` (2.7.5) and `SQLitePCLRaw.bundle_e_sqlite3` (3.0.3).

**They are not dead, and must not be removed.** `CentralPackageTransitivePinningEnabled` is `true`
(line 4), so a `PackageVersion` with no direct reference is how this repo pins a *transitive*
version — `Microsoft.OpenApi` under `Microsoft.AspNetCore.OpenApi` (10.0.9), and
`SQLitePCLRaw.bundle_e_sqlite3` under `Microsoft.Data.Sqlite` (10.0.8). Deleting either would
silently change what actually restores.

This is worth recording as a near-miss: "referenced by nothing, therefore delete" is the obvious
YAGNI read, and it is wrong here. A one-line comment on each transitive pin makes the intent legible
and prevents the next reviewer from making the cut.

The same applies to the several `Microsoft.AspNetCore.*` pins sitting at `2.3.x` while siblings are
`10.0.x` — almost certainly correct, since those package IDs stopped shipping standalone once
ASP.NET Core folded them into the shared framework, but undocumented and an open invitation to a
breaking drive-by "bump everything" PR.

**Net finding: no pin should be deleted; three groups should be commented.**

### C5 — The `SQLite defaults` perf gate has no headroom over its own variance

*Found by tripping it twice while landing this review's own commits.*

`http-workflow-performance.yml` enforces `--enforce-p95-ms 250` against a live HTTP workflow
benchmark. Warm p95 across the 10 recorded runs that produced a measurement — four branches, all
passing except where noted — spans **137.9 ms to 246.2 ms**: a **108 ms spread against a 250 ms
budget**, with the threshold sitting ~4 ms above the observed maximum of *passing* runs.

The cold/warm relationship also inverts freely between consecutive runs (cold 263.8 / warm 246.2 in
one; cold 187.6 / warm 251.4 in the next), which is not how a real regression behaves.

The gate is therefore comparing a single noisy sample against a constant that sits at roughly the
95th percentile of that sample's own distribution, so it fails a meaningful fraction of the time on
changes that cannot affect runtime — this branch tripped it on a commit whose diff was one Markdown
file, and on another that changed zero dependency-graph edges.

**Do not fix this by raising the budget.** The gate guards something real. Recommended, in order of
value: take several samples per run and enforce on their median; or measure a per-run baseline in the
same job so runner speed cancels out, instead of comparing to a fixed constant; or, if a constant is
preferred, recalibrate it from the observed distribution and record how the number was derived.

---

## 7. The apparatus

### D1 — Documentation outweighs a third of the code

126,718 lines of markdown against 241,716 lines of `src/` — roughly one line of prose per two lines
of code. 63 `EXTENSION_POINTS.md` catalogs (4,555 lines) maintained by hand under §2.22.1, which
specifies ~10 required fields *per event*.

**Recommendation: do not generate the catalogs.** The "when and why to override" prose is the actual
value, and a generator would replace judgment with a schema dump. The better fix is to extend the
mechanical completeness check beyond today's heading-drift guard so drift is caught by tooling while
the prose stays human.

### D2 — Specs accumulate with no exit

175 spec directories, **every one still status `Draft`**, many tagged "superseded / retained / out
of scope" in `docs/maps/spec-status-map.md`, none ever retired. There are **27 duplicate spec
numbers** (`015-*` ×3, `090-*` ×3, `092-*` ×3, `095-*` ×3, and 23 more) from two numbering lanes —
runtime and groundwork — that ran concurrently.

The root cause is that **no archival or numbering policy exists anywhere in the repo**. That is the
gap worth closing.

**Recommendation: do not renumber.** The collisions are cosmetic, and fixing them means rewriting
884 spec-path links across 61 markdown files plus 14 references from C# — churn with no payoff.
Write the lifecycle policy, apply real statuses in place so every cross-link keeps resolving, and
let the spec-status generator surface terminal vs live.

### D3 — Root cause: the ratchet

Each obligation is individually cheap and individually justified. A new work unit adds a project
(§2.16 prefers the finer split), an extension-point catalog (§2.22.1, mandatory), a spec directory
(the Speckit flow), evidence records, and guard tests (§2.23). None of them has a counterpart that
ever removes anything. At five merged PRs a day, the aggregate is what the "this feels too
complicated" instinct is detecting — correctly.

The structural fix is not a cleanup. It is adding a *subtractive* obligation somewhere in the
operating model: a periodic consolidation review with the standing to merge projects, retire specs,
and delete guards whose gate has been superseded.

---

## 8. Two deliberate non-actions

**The EF ratchet machinery stays.** `EfCoreSurfaceScanner.cs` (1,607) +
`EfCoreSurfaceRatchetTests.cs` (1,131) + `FrozenAspNetCoreIdentityEfOracleRatchetTests.cs` (158)
look like ~2,900 obsolete lines, and they will be. But
`specs/144-zero-ef-final-removal/tasks.md` T059–T071 *already owns* replacing them with the
fail-closed absolute-zero guard, and that spec's intake froze 2026-07-30. Removing them here is a
merge collision with frozen in-flight work, not a cleanup.

**No spec renumbering.** Reasoning in D2 — 898 references rewritten for a cosmetic result.

Both are recorded so the next reviewer does not re-derive them.

---

## 9. Recommended work

Ordered by leverage per unit of risk. Items marked **build-gated** cannot be done without a
restorable .NET 10 environment with private-feed access.

Status as of PR [#1112](https://github.com/elsa-workflows/elsa-foundation/pull/1112) (merged or
awaiting merge): items 1, 2, 10 and the first two thirds of 4 are **done**.

| # | Work | Finding | Status |
|---|---|---|---|
| 1 | Hoist TFM/`Nullable`/`ImplicitUsings` to `Directory.Build.props`; add `tests/Directory.Build.props`; strip the duplicated blocks | B1 | ✅ **done** — net −1,173 lines; all 246 projects verified to resolve identical properties |
| 2 | Root `.editorconfig` + `EnableNETAnalyzers`/`AnalysisLevel`, warnings-first | C1 | ✅ **done** — three rules tuned against measured output; see the file's header |
| 3 | Comment the two transitive pins and the `2.3.x` ASP.NET Core pins — **delete none** | C3 | ✅ **done** — first comments in the file. Correction: the seven `2.3.x` pins are all *directly* referenced, not transitive |
| 4 | Collapse `AdaptiveIntervalSchedule` 5→1; shared SSE helper; central `ErrorCodes` | B3, B7, B4 | ✅ **done** — `ActivityErrorCodes` in `Elsa.Primitives`, 85 sites in 25 files. Needed **no** new project reference; every consumer already reaches it transitively |
| 5 | ~~Consolidate the JSON options wrappers~~ | B2 | ❌ **withdrawn** — distinct frozen persistence contracts; do not attempt |
| 6 | Shared base for the four `*.Unified` provider features | B6 | ❌ **withdrawn after building it** — measured net **+74 lines** (+80 base vs −11 saved). Hoisting the cache settings is barred by `GroundworkStorageCompositionTests`. See [decisions §2](simplification-review-decisions-2026-08.md) |
| 7 | Narrow implementation classes to `internal sealed`, domain by domain | A1 | ⛔ **blocked — constitutional**. §2.23.3 mandates `public sealed` for logic-bearing implementations and names `internal sealed` as the convention it replaced; 673/681 already comply. See [decisions §1](simplification-review-decisions-2026-08.md) |
| 8 | Modernization tail: primary constructors, collection expressions, sync-over-async | C2 | 🟡 safe subset ✅ — 6 no-op `ConfigureAwait(false)` removed, 9 collection expressions. Correction: the counts were repo-wide; 266 of 270 `new List<T>()` are `var`-declared and **cannot** take `[]` |
| 9 | One .NET tool replacing 10 duplicated scripts; keep script shims | B5 | ⬜ open, medium, build-gated |
| 10 | `docs/reference/spec-lifecycle.md` + `specs/README.md` | D2 | ✅ **done** — statuses applied in place is the open remainder |
| 11 | Keep/drop verdicts for `Agent.Anthropic` and `Expressions.Liquid` | A2 | 📋 proposal ready — **keep both**; the real drop candidate is the superseded `ClaudeAgentProvider` stub. [decisions §3](simplification-review-decisions-2026-08.md) |
| 12 | §2.16.1 aggregate-growth trigger amendment | A3 | 📋 proposal ready — [decisions §4](simplification-review-decisions-2026-08.md) |
| 13 | A subtractive obligation in the operating model | D3 | 📋 proposal ready — [decisions §4](simplification-review-decisions-2026-08.md). Ratchet confirmed still running: 148→151 projects, 4,274→4,302 public types in 62 commits |
| 14 | Recalibrate the `SQLite defaults` perf gate | C5 | 📋 proposal ready — median-of-N. Blocker: the 10 cited runs are **not in git**. [decisions §5](simplification-review-decisions-2026-08.md) |

---

## Appendix A — How the numbers were taken

All counts exclude `obj/` and `bin/`. Representative commands:

```bash
# census
find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l
find src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec cat {} + | wc -l

# visibility (top-level declarations only — the actual package surface)
grep -hE '^public +(sealed +|abstract +|partial +|static +|readonly +|record +)*(class|record|interface|struct|enum) '
grep -hE '^internal +(sealed +|abstract +|partial +|static +|readonly +|record +)*(class|record|interface|struct|enum) '

# csproj boilerplate
grep -rl '<TargetFramework>' --include=*.csproj src tests benchmarks tools | wc -l
```

Host reachability (A2) was computed by transitively walking `<ProjectReference>` from
`src/Apps/Elsa.Server/Elsa.Server.csproj` and differencing against the 148 `src` projects.

Growth figures compare against the census in
[`elsa-4-architecture-review-2026-07.md`](elsa-4-architecture-review-2026-07.md) §1 (2026-07-02:
109 projects, ~84,300 LoC, 1,809 public types).

**Not verified by compilation.** Single-implementation-interface counts, unused-public-member
analysis, and the true `internal` blast radius require a build and are deliberately absent rather
than guessed.
