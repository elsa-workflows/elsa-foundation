# Simplification Review — Decisions Requiring a Ruling

**Date:** 2026-08-01
**Companion to:** [`simplification-review-2026-07.md`](simplification-review-2026-07.md) §9
**Status:** proposals only. Nothing here has been changed in the codebase.

This records the §9 items that turned out to be **decisions rather than refactors**, plus two items
that were attempted, measured, and withdrawn. It was produced in the first environment able to
restore the private feeds, so unlike the original review every claim below is compile- or
test-verified. Where a number here contradicts the original review, the original was taken from a
static census and this one from the build.

**Verification baseline.** 250 projects restore; `dotnet build Elsa.Server.slnx -c Release` is clean;
`tests/Elsa/Architecture` passes 351/351; the container-free unit suite passes 8,382 tests (one
contention flake in `Groundwork.DesignConformance`, which passes in isolation in 5s versus 1m19s
under parallel load).

---

## 1. §9.7 — narrowing implementations to `internal sealed` is a constitutional amendment

**This is the item the review ranked highest, and it cannot be done as written.**

Framework constitution **§2.23.3** (`constitution-framework.md`) says:

> - **Feature classes** are `public` and NOT sealed. […]
> - **Logic-bearing implementations** are `public sealed`. They are not part of the §2.5 inheritance
>   pattern; tests construct them directly. Sealing prevents accidental specialization.
>
> This replaces the historical `internal sealed` convention, which forced tests to use reflection or
> `[InternalsVisibleTo]` — both code smells.

The review's finding A1 proposes reverting exactly this rule, and does not cite it. **673 of the 681**
public classes under `Services`/`Handlers`/`Stores` in `src/Elsa` are already `public sealed` — 99%
compliance. The "89% of types are public" statistic is the constitution working as designed, not
accretion.

### What it would cost if amended

| Obstacle | Size |
|---|---|
| Classes registered by a *sibling* assembly (`.Core` holds the impl, `.Api` registers it) — cannot go internal without also moving code | **156** |
| Test files constructing these types **by name**, as §2.23.2 mandates | **~586** |
| `InternalsVisibleTo` escape hatch | Guard-forbidden (`ArchitectureGuardTests:541`), only 2 documented exceptions |
| Reflection assertions that would fail immediately | 6, incl. `DiagnosticsPersistenceFeatureTests` (×2), `AspNetCoreIdentityGroundworkRegistrationTests`, `CatalogParityTests` |

§2.23.4 makes each resulting test failure a collaborative architect decision, not a mechanical edit.

### The real question for Sipke

Not "should we tidy the surface" but: **is §2.23.3 the right trade?** It buys
testability-by-direct-construction and costs a semver-committed public surface across 151 NuGet
packages. That is a legitimate question — the review's underlying instinct is not wrong — but it is a
constitutional amendment with a fleet-wide major-version consequence, not a cleanup.

**Recommendation: leave §2.23.3 in force unless you want to reopen the testing strategy with it.**
The two are one decision, not two.

### What *was* done, because it stands alone

`HostShellFeatureVisibilityTests` scanned only `src/Apps`, leaving ~78 public `IShellFeature`
declarations in `src/Elsa` unguarded. CShells discovery uses `Assembly.GetExportedTypes()`, so an
internal feature class is **silently dropped at runtime** — it builds, tests pass, and the feature
quietly does nothing (the regression that once kept the OTel tracing bridge inert). The guard now
covers all of `src`, with a floor assertion so a regex that stops matching cannot make it pass
vacuously. Verified by injecting a real violation.

---

## 2. §9.6 — the `*.Unified` provider base was built, measured, and withdrawn

Implemented in full: an abstract base in `Elsa.Persistence.Groundwork.Unified`, all four providers
derived from it, all four generated `elsa-package.json` manifests **byte-identical** to baseline, 351
guards and 70 unified-host tests green.

Then measured:

| | Lines |
|---|---:|
| Base class added | **+80** |
| Saved across the four providers (95→94, 83→77, 73→70, 70→69) | **−11** |
| **Net** | **+74** |

The review estimated ~58 of PostgreSql's 70 lines were shareable. That assumed hoisting the
executable-cache settings — which is **forbidden**:
`GroundworkStorageCompositionTests.Unified_contains_no_hard_coded_family_union_or_domain_project_references`
asserts that project carries exactly one project reference, to `Composition`, keeping it free of
workflow-domain edges. `WorkflowExecutableCacheOptions` is a domain type, so those settings must stay
in the provider leaves. What remains genuinely shared is ~11 lines.

**Reverted**, under §2.17 ("a few duplicated lines … preferred when the duplication is small"). The
only thing kept is a comment on the `.csproj` recording the one-reference rule, which was previously
discoverable only by tripping the guard.

**No ruling needed** unless you disagree with the revert.

---

## 3. §9.11 — keep/drop verdicts: **keep both**

### `Elsa.Expressions.Liquid` (425 LoC) — keep, decisively

- Two test projects reference it. `ExpressionToolingProviderContractTests:117-141` runs it through
  the **shared** tooling-provider contract suite alongside JavaScript.
- **ADR 0036's stated exit criterion is a "JS+Liquid" read-parity test.** Dropping it invalidates an
  accepted ADR.
- It is the only non-JavaScript implementation of `IPortableExpressionHandler` and
  `IExpressionToolingProvider`; dropping it makes both single-implementation.
- It received feature work three days before this was written (#1085).

### `Elsa.Agent.Anthropic` (447 LoC) — lean keep

- Never wired into any shell in its four-week life (`git log -S"AnthropicAgent" -- shells.json` is
  empty), which is the honest argument against it.
- But it has real tests including an **API-key redaction security regression test**, and it is the
  second implementation of a deliberately pluggable `IAgentProvider` seam whose entire purpose is
  provider substitution. Dropping it collapses that seam to one implementation and removes the only
  evidence it is real.

### The actual drop candidate is neither

`src/Elsa/Agent/Core/EXTENSION_POINTS.md:106` documents a `ClaudeAgentProvider` stub as "wire an
Anthropic `IChatClient` … to enable" — which `Elsa.Agent.Anthropic` already does. **That stub is
superseded dead weight.** Per §2.22.1 its removal must update the catalog in the same unit of work.

### Cheap fix that resolves the ambiguity

Neither project appears in any shells file, so "unwired" is indistinguishable from "abandoned". Add
an explicit `"Enabled": false` entry, or record why each is intentionally absent.

**Ruling needed:** confirm keep/keep, and approve deleting the `ClaudeAgentProvider` stub.

---

## 4. §9.12 and §9.13 — the ratchet is still running

The review argued the growth is structural. It is also **measurably ongoing**. Comparing its own
census against the tree 62 commits later:

| Metric | Review census | 62 commits later |
|---|---:|---:|
| `src` projects | 148 | **151** |
| Public types in `src` | 4,274 | **4,302** |
| Architecture guard tests | 300 | **351** |

Supporting counts today: 64 `EXTENSION_POINTS.md` catalogs, 175 spec directories.

**One correction to the review:** its claim that *every* spec is still `Draft` is already outdated —
current statuses are 112 draft, 10 approved, 7 implemented, 2 superseded, 1 ratified, 1 complete.
The lifecycle policy landed; applying statuses in place is the live remainder.

### §9.12 — proposed §2.16.1 amendment

Keep the six exemption classes exactly as they are; they are sound and every current micro-project
passes them. Add an **aggregate review trigger**: when project count grows faster than LoC over a
window, that growth itself gets a look. A per-item exemption test structurally cannot answer an
aggregate question, which is the gap.

### §9.13 — a subtractive obligation *(the review's highest-leverage item, and I agree)*

Every per-work-unit obligation is additive — a project (§2.16 prefers the finer split), an
extension-point catalog (§2.22.1, mandatory), a spec directory, evidence records, guard tests
(§2.23). None has a counterpart that removes anything. At ~5 merged PRs/day the aggregate is what the
"this feels too complicated" instinct is correctly detecting.

**Proposal:** a periodic consolidation review with standing to merge projects, retire specs, and
delete guards whose gate has been superseded — with the explicit authority to *subtract*, since no
existing role has it.

**A caution this session earned.** Three of the review's headline findings were wrong on inspection
(§9.7 constitutional, §9.6 net-negative, §9.5 already self-withdrawn), joining the two it withdrew
itself. On this codebase, apparent duplication and apparent over-exposure keep turning out to be
load-bearing. **Any subtractive body must be build- and test-verified, never census-driven** — that
is precisely the difference between the original review and this document.

---

## 5. §9.14 — recalibrating the `SQLite defaults` gate

Mechanics, now fully traced (`tools/performance/measure-http-workflow.sh`):

- 1 cold + 20 warm-up + 200 measured requests, concurrency 1.
- p95 by **nearest rank** (line 200) = rank 190 of 200 — the 11th-worst request, which is exactly why
  shared-runner scheduling jitter dominates.
- Enforced at lines 265-268; cold is measured and reported but never gated.
- Raw latencies already persisted via `--output-samples`, so no new instrumentation is needed.

**Blocker to record:** the 10 runs the review cites (137.9–246.2 ms warm) are **not in git**. They
exist only as GitHub Actions artifacts, and `f6b15d928` — the commit that set 250 — committed no run
data. Any recalibration must re-derive the distribution and record its derivation.

**Options, best first:**

1. **Median of N passes.** Run the 221-request pass 3–5 times in-job and gate the median. Cheap
   against the 20-minute budget, and collapses most jitter.
2. **Self-baseline.** Measure a baseline in the same job so runner speed cancels, instead of
   comparing to a fixed constant. Strongest, but needs a second build + `groundwork apply` + port.
3. **Recalibrate the constant** from an observed distribution, and commit the derivation.

**Do not raise the budget without doing one of these** — the gate guards something real.

---

## 6. §9.9 — the generated maps, the shell twins, and the code graph

The maps tooling is now one .NET tool. Two things surfaced that matter more than the port itself.

### The two "identical" generators were not identical, and porting found four bugs

| # | Where | Defect |
|---|---|---|
| 1 | `generate-extension-point-map.sh` | Tab is an IFS *whitespace* character, so `IFS=$'\t' read` collapses consecutive tabs. Catalogs with no `###` headings or no `Kind:` lines lose those fields and every later column shifts left. **37 rows affected; 25 corrupted rows are committed on `main`**, reporting `0` as their "Extension-point headings". |
| 2 | `generate-extension-point-map.ps1:131` | Double-quoted string, so PowerShell reads the backticks around `EXTENSION_POINTS.md` as escapes and strips them. **So the twins disagreed with each other, and both were wrong.** |
| 3 | `generate-maps.sh` feature-map | `sort -k3,3` with no `-t`, so fields split on spaces as well as tabs. The Kind column contains spaces ("EF Core feature base"), so the sort key landed on a token *inside* Kind, not the project. |
| 4 | `generate-maps.sh` findings | The multi-version count uses gawk's `arr[i][j]`, which BWK awk rejects, and `2>/dev/null \|\| echo 0` swallowed the error. Silently **0 on macOS, 3 on Linux**. The true answer is 3. |

Plus: `manifest.json`'s `generated_files` was a hardcoded list of all 11 files, so a v1-only refresh
claimed the four v2 maps were fresh without writing them. And the shell sorted with the ambient
locale, so output depended on `LC_COLLATE`; the tool pins ordinal ordering.

This is exactly the B5 risk — *"no test proving the two implementations agree"* — except it was not
hypothetical. It had already shipped into committed documentation.

### Are the maps still needed, given the code knowledge graph?

`codebase-memory-mcp` indexes this repository (**101k nodes / 374k edges**) and answers symbol-level
questions — find a class, trace calls, semantic search, Leiden clustering — far better than a static
table. The honest split:

| Map | Subsumed by the code graph? |
|---|---|
| project-reference, package, feature, test | **Largely** — but the graph models *code* imports, not `.csproj` `ProjectReference` or NuGet version resolution |
| domain, architecture-reference | Partly — clusters are richer, but the design/runtime seam is a build-graph fact |
| **spec-status** | **No** — spec lifecycle from `specs/*.md` |
| **extension-point** | **No** — `EXTENSION_POINTS.md` catalog contents |
| **feature-dependency** | **No** — `[ShellFeature(name:…)]` arguments and config-property classification |

Complementary, but **over-built for what is uniquely theirs**: non-code artifacts and build-graph facts.

### The evidence that they are not currently load-bearing

- Nothing in CI reads or validates them.
- The committed maps were **stale** (148 source projects recorded; 151 actual) and **partly corrupted**
  (25 rows), and neither was noticed.
- A full session of deep repository investigation completed without opening `docs/maps/` once — and in
  the two places a map covered the question, it would have answered *wrong*.

**Recommendation.** Keep generation manually initiated — that is the right default, and the port
removes the cost argument. Measured on an otherwise-idle machine: the five shell generators total
roughly **4 minutes** (v1 layer 1m30s, domain 1m36s, feature-dependency 26s); the tool produces **all
twelve outputs in 4.8s**. Then add a **CI freshness check** that recomputes `input_fingerprint` and
fails when the committed maps do not match the tree — a sub-second job that regenerates nothing and
makes silent rot impossible. The runtime was never the real defect.

**A related observation for §9.13.** The code graph has **340 indexed projects, 310 of them throwaway
worktrees**. The same one-way ratchet the review found in the repository has reproduced itself in the
tooling around it. Nothing in that system subtracts either.

---

## 7. Summary of what needs a ruling

| # | Question | Recommendation |
|---|---|---|
| 1 | Reopen §2.23.3 (`public sealed`) together with the testing strategy? | **No**, unless reopening both |
| 2 | Accept the §9.6 revert? | **Yes** — net +74 lines |
| 3 | Keep `Liquid` and `Agent.Anthropic`; delete the `ClaudeAgentProvider` stub? | **Yes** |
| 4 | Amend §2.16.1 with an aggregate growth trigger? | **Yes**, keep exemption classes |
| 5 | Create a subtractive consolidation review? | **Yes** — highest leverage |
| 6 | Fix the perf gate by median-of-N? | **Yes**; do not raise the budget |
| 7 | Add a CI freshness check for the generated maps? | **Yes** — sub-second; keeps generation manual |
