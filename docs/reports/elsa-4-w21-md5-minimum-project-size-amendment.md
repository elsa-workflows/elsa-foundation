# W21 / MD-5 — Minimum-project-size amendment (PROPOSAL)

Status: **proposal** — draft amendment text + rationale for review. **Not** applied to
`constitution.md`; ratification is the architects' / user's call and routes through
[Constitution Readiness](../program-goals/constitution-readiness.md). Produced by W21 of the Elsa 4
remediation fleet.

**Branch point:** `1d5bb6bb` (W18 merge tip). **LoC method:** physical `.cs` line count (matches the
2026-07 review — its 32 / 41 smallest values reproduce exactly at this SHA).
**Snapshot caveat:** parallel wave units W16 (adding feature projects) and W17 (extracting
`Publishing.Core`) will change the population below; the numbers are a snapshot at this SHA and are not
chased across their branches.

## The finding this addresses (MD-5)

The 2026-07 review found 11 projects under 100 LoC (smallest 32) and observed that the
micro-fragmentation is **constitutionally intentional**: framework §2.16 ("when in doubt, prefer the
finer-grained split; merging later is easier than separating") combined with §2.1 ("one `.Core`/impl
split per feature") *produces* these tiny projects by design. The open question (review Open Question 1):
*is the "prefer finer-grained split" gate still calibrated correctly now that the tree has ~40
sub-150-LoC projects, and was a LoC/consumer threshold ever debated?*

## Fresh data at `1d5bb6bb`

**13 projects now sit under 100 physical LoC** (up from the review's 11 — the two new
`Persistence.Groundwork.*.Unified` provider projects landed since). Full population with per-project
disposition:

| LoC | Files | Project | Contents | Exception class | Disposition |
|---:|---:|---|---|---|---|
| 32 | 2 | `Elsa.Workflows.Design.Reconciliation.Core` | 1 interface + 1 delegate | Contracts-only `.Core` seam | **KEEP** |
| 41 | 2 | `Elsa.Serialization.Newtonsoft` | feature + 1 `IJsonIslandTypeHandler` impl | Layer-2 helper (isolates Newtonsoft) | **KEEP** |
| 45 | 1 | `Elsa.Persistence.Groundwork.Unified` | provider-neutral composite `StorageManifest` | Shared composition seam (2 consumers) | **KEEP** |
| 50 | 4 | `Elsa.Workflows.Primitives` | constants + 1 model | Primitives/constants | **KEEP** |
| 51 | 2 | `Elsa.Locking.Core` | 2 interfaces | Contracts-only `.Core` seam | **KEEP** |
| 56 | 3 | `Elsa.Expressions.JavaScript.Primitives` | constants only | Primitives/constants | **KEEP** |
| 56 | 3 | `Elsa3.Activities.Design.Import` | elsa3 import boundary | Migration boundary (§E2.7) | **KEEP** |
| 61 | 3 | `Elsa.Caching.Core` | 3 interfaces | Contracts-only `.Core` seam | **KEEP** |
| 70 | 3 | `Elsa.Expressions.JavaScript.Libraries` | feature + preprocessor + options | Independently-composable feature unit | **KEEP** |
| 76 | 2 | `Elsa.Persistence.Groundwork.PostgreSql.Unified` | provider unified shell feature | Provider leaf (§2.7 isolation) | **KEEP** |
| 77 | 2 | `Elsa.Persistence.Groundwork.Sqlite.Unified` | provider unified shell feature | Provider leaf (§2.7 isolation) | **KEEP** |
| 78 | 4 | `Elsa.Locking.FileSystem` | options + feature + 2 adaptors | Provider leaf (§2.7 isolation) | **KEEP** |
| 82 | 3 | `Elsa.Http.JavaScript` | constants + contributor + feature | Cross-domain contribution seam (HTTP × JS) | **KEEP** |

**Result: 0 of 13 are forced merge candidates.** Every sub-100-LoC project falls into a named
exception class where merging it away would either violate another gate (provider isolation §2.7/MD-8;
migration boundary §E2.7; the cross-`.Core` composition mechanism §2.1) or collapse a real capability
boundary (an independently-composable `[ShellFeature]`; a shared seam with ≥2 consumers). This is the
key, and somewhat counter-intuitive, finding: **the current tree is already compliant with the
exception taxonomy the amendment proposes** — so the amendment's job is to *codify why these are
legitimate*, not to trigger a merge campaign.

### Watch band (100–150 LoC) — for context, not action

The next nine projects (102–141 LoC) all likewise map to an exception class:
`Activities.Design.Reconciliation.Core` (102, contracts-only), `Activities.Composition.Runtime` (111,
feature), `Events.Strategies` (121, dispatch-strategy feature), `Activities.Scheduling` (126, feature),
`Tasks.Core` (130, contracts), `Primitives.Hosting` (130, primitives/hosting),
`Workflows.Design.Validations.Core` (138, contracts-only), `Persistence.Groundwork.PostgreSql` (139,
provider leaf), `Persistence.EFCore.Sqlite` (141, provider leaf). None is a forced merge candidate.

## Why a hard LoC gate would be wrong

A hard "merge anything under N LoC" rule would actively **fight** §2.16's fragmentation-by-design and
would force violations of higher-value gates — you cannot merge a provider leaf into its `.Core`
without breaking provider isolation, nor merge a contracts-only `.Core` into a consumer without
breaking the cross-`.Core` composition mechanism. The DX cost the review measured is real, but it is a
cost of the *ceremony per feature* (review "DX Cost Analysis"), not of the project count per se.
Therefore the amendment is **soft guidance + an exemption test**, not a threshold gate.

---

## Draft amendment text (PROPOSAL — for ratification review)

**Recommended placement:** a new corollary under framework §2.16 (Refactor-cost test), because the
economic question (does NuGet-identity preservation pay for itself below N LoC?) is framework-level and
distributes with the framework constitution. It cascades to the Elsa constitution as an interpretive
note. An alternative placement as an Elsa-only §E section is viable if the architects prefer to keep
the framework document unchanged; the text below is written to work as framework §2.16.1.

**Version impact if ratified:** framework MINOR (new guidance section); Elsa MINOR cascade
(interpretive note + provenance row in the amendment index).

> ### §2.16.1 Minimum-viable-project guidance
>
> The finer-grained-split preference (§2.16) is deliberate and is **not** overridden by small project
> size. There is **no minimum line count** below which a project must be merged.
>
> **Guidance for *new* projects.** When creating a *new* implementation project (Layer 3) that would
> ship below roughly **100 physical lines of code**, briefly record why it earns a separate NuGet
> identity rather than folding into an existing sibling. The bar is *a reason*, not a size — a single
> sentence in the PR or the project's README suffices.
>
> **Exemption test.** A project below the guidance size is **automatically legitimate** — no
> justification required — when merging it away would either (a) violate another gate, or (b) collapse
> a real capability boundary. The following exception classes are the enumerated worked catalog of (a)
> and (b); a project fitting any of them is exempt:
>
> 1. **Contracts-only `.Core` seam** — a `.Core` project consisting of interfaces, delegates, abstract
>    types, and models only. It is the cross-`.Core` composition mechanism (§2.1); merging it into a
>    consumer would break that mechanism. *(Examples: `Elsa.Locking.Core`, `Elsa.Caching.Core`.)*
> 2. **Primitives / constants project** — a zero- or near-zero-dependency project of shared constants
>    and primitive models. *(Examples: `Elsa.Workflows.Primitives`, `Elsa.Expressions.JavaScript.Primitives`.)*
> 3. **Provider leaf** — a provider-specific implementation (`*.Sqlite`, `*.PostgreSql`,
>    `*.FileSystem`, …) whose small size reflects that the provider genuinely needs little code.
>    Provider isolation (§2.7 / the `.Core`-heavy-package rule) *mandates* the separate project.
>    *(Examples: `Elsa.Locking.FileSystem`, `Elsa.Persistence.EFCore.Sqlite`.)*
> 4. **Migration / compatibility boundary** — a one-way import or compatibility surface whose
>    separateness is required by an explicit boundary rule (in Elsa, §E2.7). *(Example:
>    `Elsa3.Activities.Design.Import`.)*
> 5. **Layer-2 helper / adapter library** — an opt-in default implementation that isolates a single
>    focused external dependency, kept out of Layer 1 by §2.1. *(Example: `Elsa.Serialization.Newtonsoft`.)*
> 6. **Independently-composable feature unit or cross-domain contribution seam** — a project that
>    exists as a separately-toggleable `[ShellFeature]` composition unit, or a project that bridges two
>    domains by contributing one domain's types to another (and so cannot fold into either without
>    creating a forbidden dependency). *(Examples: `Elsa.Expressions.JavaScript.Libraries`,
>    `Elsa.Http.JavaScript`.)*
>
> A project that fits **none** of these classes and is below the guidance size is a candidate for
> merging into its nearest sibling — but the merge is still discretionary under §2.16 (which favors
> keeping NuGet identity stable), not mandatory.

---

## Applying the draft to the current tree

Running the exemption test against the 13-project population above yields **13 exempt, 0 merge
candidates**. The amendment would therefore ratify the current shape rather than change it — its value
is (1) settling review Open Question 1 with a durable rule, (2) giving future feature authors a crisp
"do I need a new project?" test, and (3) preventing a future over-correction that merges provider
leaves or contracts-only seams and thereby breaks isolation/composition gates.

## Recommendation to ratifiers

1. **Ratify as soft guidance (§2.16.1), not a hard gate.** The data shows a hard gate would create more
   violations than it resolves.
2. **Adopt the six exception classes** as the worked catalog; they are exhaustive over the current
   sub-150-LoC population.
3. On ratification, fold the text into the chosen constitution section with the next version bump, add
   a provenance row to [`constitution-amendment-index.md`](constitution-amendment-index.md), and leave
   this report linked from the originating bucket.

## Links

- Finding source: [`review-modularity.md` §MD-5 + Open Question 1](elsa-4-architecture-review-2026-07/review-modularity.md)
- Gates referenced: framework §2.1, §2.16, §2.18.4, §2.7; Elsa §E2.7
- Routing: [Constitution Readiness](../program-goals/constitution-readiness.md) ·
  [Amendment index](constitution-amendment-index.md)
- Bucket: [Elsa 4 review remediation](../program-goals/elsa-4-review-remediation.md)
