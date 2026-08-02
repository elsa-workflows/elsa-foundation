# Subtractive obligation — framework constitution amendment (PROPOSAL)

Status: **proposal for ratification review** by Joey Barten, Sipke Schoorstra, Frans van Ek.
Produced from §9.13 of the [simplification review](simplification-review-2026-07.md), ruled *yes —
a periodic consolidation review* on 2026-08-01. Provenance to be recorded in the
[amendment index](constitution-amendment-index.md) on ratification.

**Branch point:** `42d6436b4`. **Method:** every figure below is a direct count at that SHA,
reproducible with the commands in [Appendix A](#appendix-a--how-the-numbers-were-taken).

---

## 1. What this amendment is, and what it deliberately is not

The finding is **not** "the codebase is too big" or "there are too many projects". Section 3 shows
those claims do not survive measurement. The finding is narrower and better supported:

> Every per-work-unit obligation in the operating model **adds** an artifact. None of them has a
> counterpart that ever removes one. There is no role, cadence, or rule under which a spec is
> retired, a superseded guard is deleted, or a stale catalog entry is pruned.

The consequence is not bloat. It is **decay that nothing detects** — artifacts that describe a state
of the world which stopped being true, with no mechanism that notices.

---

## 2. The evidence, all of it about retirement rather than size

### 2.1 Specs are never retired

| Signal | Count |
|---|---:|
| Spec directories | **175** |
| Still `Draft` — including specs whose work merged weeks ago | **112** |
| Carrying no status line at all | **42** |
| Reaching a terminal status (`superseded`, `complete`) | **3** |
| Duplicate spec numbers (two numbering lanes run concurrently) | **27** |

`docs/reference/spec-lifecycle.md` landed in July and defines the statuses. Applying them in place
is still open. Nothing obliges anyone to close the loop, so 112 specs claim to be drafts.

### 2.2 Guards accumulate; none is ever deleted

`tests/Elsa/Architecture` went **300 → 351 tests in 62 commits** — roughly one new guard per commit.
That is healthy while each gate is live. The gap is that no obligation asks whether a gate has been
*superseded*. The clearest live example is already known and documented: ~2,900 lines of EF-ratchet
scanner and ratchet tests whose replacement is owned by `specs/144-zero-ef-final-removal` T059–T071.
They will become dead the moment that spec lands, and nothing will notice.

### 2.3 Catalogs drift from the code they describe

64 `EXTENSION_POINTS.md` catalogs are maintained by hand under §2.22.1. Two concrete drifts were
found this month by accident, not by any check:

- `src/Elsa/Agent/Core/EXTENSION_POINTS.md` listed a `ClaudeAgentProvider` stub that **does not exist
  anywhere in the tree**, and omitted `AnthropicAgentProvider`, which does. Corrected in `42d6436b4`.
- `docs/maps/` was three projects stale and carried **25 corrupted rows** from a shell bug, committed
  and unnoticed. A CI freshness check now exists (`05f8181ea`) — added only because the port
  happened to surface it.

### 2.4 The pattern reproduces outside the repository

The code knowledge graph indexing this workspace holds **340 indexed projects, 310 of them throwaway
worktrees**. The same absence of a retirement step, in a different system.

---

## 3. What the evidence does *not* support, recorded so it is not re-argued

The simplification review's §9.12 proposed an **aggregate growth trigger** keyed on project count
outpacing LoC. **Measurement refutes the premise, and the trigger would never have fired.**

Like-for-like on the baseline's own scope (`src` + `tests`, 2026-07-02 → 2026-08-01):

| Metric | 2026-07-02 | `42d6436b4` | Growth |
|---|---:|---:|---:|
| Projects | 109 | **245** | 2.25× |
| C# files | 1,546 | **3,924** | 2.54× |
| LoC | 84,300 | **532,387** | 6.32× |
| Public types | 1,809 | **5,622** | 3.11× |

**Project count grew at roughly a third the rate of code.** Mean project size went 773 → 2,173 LoC:
granularity became *coarser*, not finer. Public types per line of code roughly **halved**. The
sub-100-LoC population §2.16.1 governs went 13 → **12** while the tree grew six-fold, so that gate
is not the binding constraint on anything.

The review's headline figures compared a `src`+`tests` baseline against `src`-only current counts,
which is what made project growth look disproportionate. Even on those mixed-scope numbers, projects
grew slower than code (1.36× vs 2.87×).

**Recommendation: do not amend §2.16.1.** Its six exemption classes are sound, every current project
passes, and the aggregate concern it was to address is not visible in the data.

---

## 4. Draft amendment text (PROPOSAL — for ratification review)

To be added to the framework constitution as **§2.25**, following §2.24.

> ### §2.25 Consolidation review — the subtractive obligation
>
> Every obligation in this constitution is additive: a work unit may add a project (§2.16), an
> extension-point catalog (§2.22.1), guard tests (§2.23), a spec, and evidence records. None of them
> requires anything to be removed. This section supplies the counterpart.
>
> **§2.25.1 The obligation.** A **consolidation review** runs periodically, on a cadence the
> architects set (a quarter is the suggested starting point). It is a work unit like any other: it
> produces a report, its changes go through the normal gates, and it may conclude that nothing needs
> to change. It is the only unit with standing to *retire* artifacts, and it is expected to use it.
>
> **§2.25.2 Standing.** The review may, within one unit of work:
>
> - move a spec to a terminal status, or archive it, per the spec lifecycle policy;
> - delete a guard test whose gate has been superseded, provided the report names the gate that
>   replaced it;
> - prune or correct an extension-point catalog entry that no longer matches the code;
> - merge two projects, subject to §2.16 (NuGet identity is preserved wherever possible) and the
>   §2.16.1 exemption classes;
> - delete generated artifacts and their generators when no consumer remains.
>
> **§2.25.3 Evidence bar — subtraction is verified, never inferred.** Every removal must be
> justified by evidence the build or the test suite can produce: a compile, a test run, a reachability
> check, a guard that still passes without the deleted code. **A static census, a text search, or a
> similarity judgement is not sufficient evidence to remove anything.**
>
> This bar is not theoretical caution. Of the headline findings in the 2026-07 simplification review —
> produced by exactly such a census — five did not survive compilation: two were withdrawn by the
> review itself, one was barred by §2.23.3, one measured as a net *increase* of 74 lines when built,
> and one rested on a scope-mismatched growth comparison. A subtractive body working from a census
> would have removed load-bearing code in at least two of those cases.
>
> **§2.25.4 What the review must report.** Each run records what it retired and why, and — equally —
> what it examined and deliberately kept. The second list is what stops the next review from
> re-deriving the same conclusions, and it is where the "looks duplicated, is actually two contracts"
> cases get written down.

---

## 5. Applying the draft to the current tree

A first consolidation review would find, on today's evidence:

| Candidate | Evidence available now | Likely verdict |
|---|---|---|
| 112 specs marked `Draft` with merged work; 42 with no status | Spec lifecycle policy exists and defines terminal statuses | Apply statuses in place; no renumbering (898 cross-links) |
| ~2,900 lines of EF-ratchet machinery | `specs/144-zero-ef-final-removal` T059–T071 owns the replacement | **Wait** — retiring it before 144 lands is a merge collision, not a cleanup |
| `EXTENSION_POINTS.md` drift | Two instances found by accident this month | Extend the mechanical completeness check; keep the prose human (§D1) |
| `Elsa.Agent.Anthropic`, `Elsa.Expressions.Liquid` | Both ruled **keep** 2026-08-01: ADR 0036 exit criterion; security regression test | Keep; record why, so it is not re-asked |
| 151 `src` projects | Growth is 2.25× against 6.32× LoC; 12 projects under 100 LoC, all exempt | **No action** — the data does not support consolidation |

Note the shape of that table: the honest first review retires **specs and catalog drift**, and leaves
projects and code alone. That is the opposite of where the original review pointed, and it is the
point of §2.25.3.

---

## 6. Recommendation to ratifiers

1. **Adopt §2.25** as drafted. The retirement gap is real, evidenced, and currently unowned.
2. **Do not adopt the §2.16.1 aggregate trigger.** Its premise does not survive measurement (§3).
3. Treat **§2.25.3** as the load-bearing clause. Without it, a body with standing to delete, working
   from static analysis, is a liability rather than a control.

---

## Appendix A — how the numbers were taken

At `42d6436b4`, excluding `obj/` and `bin/`:

```bash
# like-for-like with the 2026-07-02 baseline scope (src + tests)
find src tests -name '*.csproj' -not -path '*/obj/*' | wc -l
find src tests -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec cat {} + | wc -l
grep -rhE '^public +(sealed +|abstract +|partial +|static +|readonly +|record +)*(class|record|interface|struct|enum) ' \
  --include='*.cs' src tests | wc -l

# spec statuses
grep -rhoiE '^\*\*Status\*\*:.*' specs/*/spec.md | sort | uniq -c

# per-project LoC attributes each .cs file to its nearest ancestor .csproj, so nested
# projects are not double-counted into their parents.
```

Baseline figures are from [`elsa-4-architecture-review-2026-07.md`](elsa-4-architecture-review-2026-07.md)
§Scope, which states them for `src/` **and** `tests/` combined — the scope mismatch corrected in §3.
