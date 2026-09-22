---
status: proposed
date: 2026-08-07
amended: 2026-09-22
decision_context: FR-1 discussion on issue #1144, agreed by Joey Barten, Sipke Schoorstra and Frans van Ek
amendment_context: patch derivation changed from commit height to last-published state after the rename hazard was measured; awaiting the same three-party agreement as the original
---

# Package versioning uses two version lines with a computed patch digit

## Context

Every push publishes all 151 packable projects at a single injected `4.0.0-preview.<run_number>`.
Because `dotnet pack` derives each nuspec dependency range from the referenced project's version, a
uniform version makes every feature declare that it requires the contract packages at that same
version, even when an older one would satisfy it.

That inflated floor is not cosmetic. Under Nuplane Strategy B the host pins a contract surface and
features arrive at runtime, so an inflated floor means **a feature can only be installed into a host
already pinned at the feature's own version**. Every feature update drags a host upgrade behind it,
which defeats runtime feature installation.

Two measurements taken over the repository's full history (2026-05-08 onward, 1030 commits touching
`src/`) shaped the decision:

- A commit touches a median of two projects; 46% touch exactly one; 53% touch no contract package at
  all. Per-package version maintenance is therefore small, not large.
- The `.Core` suffix is not a stability boundary. `Elsa.Workflows.Runtime.Core` has 281 commits, the
  most-churned project in the repository, against 4 for `Elsa.Pipelines.Core` and 7 for
  `Elsa.Events.Core`. A contract bundle defined by naming would move constantly and be worthless to
  pin. (Commits touching a project, not public API deltas, and the period is pre-release.)

## Decision

**Two version lines.**

**Line A, the host baseline.** The cross-cutting contract packages a host pins share one version that
moves only when the contract surface changes, under framework §4.2 SemVer. Membership begins with
`Elsa.Primitives`, `Elsa.Events.Core`, `Elsa.Tasks.Core`, `Elsa.Serialization.Core`,
`Elsa.Mediator.Core`, `Elsa.Attention.Core` and `Elsa.Expressions.Core`,
selected on cross-domain reach and low churn.

**Line B, everything else.** All features and all domain `.Core` packages share `major.minor` across
the repository, with the patch digit per package. A domain's `.Core` ships with its domain, because a
domain is a delivery unit.

**The patch digit is computed, not authored.** No `<Version>` element is hand-edited anywhere.

*Amended 2026-09-22.* The patch was originally the count of commits touching a package's own files
since the release tag. It is now derived from the package's **last published version, recorded per
package id in the dependency map**: a package whose own files changed since it was last published
increments its patch; an unchanged package keeps its number and is not published. Paths take no part
in the computation.

The change is confined to how the digit is derived. Everything else this decision records — the two
lines, selective publishing, derived floors with an upper bound, release-shaped previews, and the
absence of authored version elements — is unaffected, and the reasons for them are untouched.

**Publishing is selective.** Only packages whose own files changed are published. Unchanged packages
keep their last published version, so dependency floors are never restamped for a change the package
did not take part in. This is what keeps derived floors honest without stitching ranges at pack time.

**Previews are shaped like releases.** No run-number suffix on `main`; branch builds carry a branch
label; release promotes the artifact already built and tested rather than rebuilding from a tag.
While the 4.0 line is unreleased, packages carry a plain `-preview` label with no counter so nothing
on the feed reads as generally available.

**Dependency ranges carry an upper bound**, `[x.y.z, next-major)`, so the SemVer band is expressed
rather than assumed.

**Version magnitude is enforced, not asserted.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` makes an
unacknowledged public addition a build error, and `Microsoft.DotNet.ApiCompat.Tool` detects removals
and signature changes against the last released package. A gate compares the resulting delta class
against the bump and fails when the bump is smaller than the delta requires. The gate runs against
the release baseline, not per pull request.

**A compatible dependency update does not oblige dependents to republish.** Only a major change
propagates through the reverse closure.

**Line A stabilizes by policy after 4.0**, with breaking contract changes batched into planned majors.

## Considered options

- **Whole-version lockstep across the repository**, the current behaviour, was rejected because
  derived floors inflate and runtime feature installation stops being independent of the host. It is
  sanctioned by framework §2.13, so this is a release-engineering choice rather than a compliance
  correction.
- **Per-package versions everywhere**, as originally proposed in FR-1, was rejected as more
  granularity than the evidence supports once the contract line is separated. Its cost was
  concentrated in the reverse-closure bump obligation, which this decision removes.
- **A shared patch digit per domain subset** was rejected on measurement: 59% of (commit, domain)
  pairs touch exactly one package in that domain, rising to 72% for `Elsa.Persistence`. Banding would
  restamp unchanged siblings in the majority of cases, and in domains containing mutually exclusive
  provider leaves it would tell a consumer their provider changed when it did not. Sets that genuinely
  co-change already receive matching numbers from the computed patch, derived rather than declared.
- **A contracts meta-package** aggregating the contract surface was deferred. Third-party domains ship
  their own contract packages that cannot live inside a package published here, so such a package
  would describe the first-party subset of an open surface. Its membership is also a judgment call
  that a published identity would freeze, and the host whose pin list would define it does not exist
  yet. Elsa §E already holds back `Elsa.Foundation.Core` on the same reasoning.
- **Git-height versioning tooling** such as Nerdbank.GitVersioning was declined in FR-1 on
  directory-scoping and tool-dependency grounds. The commit-height patch this decision originally
  adopted used `git rev-list` with ownership resolved through the dependency map, which answered both
  objections without adding a tool to the publishing pipeline.
- **Commit height over a package's own files**, this decision's original mechanism, was replaced on
  2026-09-22 because **`git rev-list` does not follow renames**. Ownership resolution through the
  dependency map addressed directory *nesting*, which is what the original wording anticipated; it
  does not address a directory *moving*. Measured on this repository against the Elsa 3 move: a
  baseline at the move's parent commit gives an identical count (8 against a true 8), a baseline 25
  commits earlier gives 8 against a true 11, and at 80 commits the digit runs **backwards** — computed
  8 where 9 was already published. A published version that decreases is not recoverable by the
  monotonicity gate alone, because the gate can only reject the build, not supply the right number.
  Renames are not a tail risk here: `git log --follow` shows `Elsa.Agent.Core` has already moved
  through `src/Elsa/Foundation/Agent/Abstractions`, `src/Elsa/Agent/Abstractions`,
  `src/Elsa/Agent/Core` and `extensions/Agent/src/Core`.
- **Declared lineage**, keeping commit height but having each packable project record its prior
  directories so the count spans them, was considered and held in reserve. It preserves the property
  that a version is a pure function of (commit, package) with no external state, at the cost of a
  declaration on every move and a guard to enforce it. It remains the fallback if the dependency map's
  role in publishing is later judged too load-bearing.
- **`git log --follow` auto-discovery** of a package's historical directories was rejected. It works,
  but it is similarity-heuristic — its output for `Elsa.Agent.Core` includes an almost certainly
  spurious `src/Elsa/Foundation/Identity/Abstractions` — git's documentation cautions against it for
  complex histories, and a shipped version number must not depend on a heuristic that can vary between
  git versions.

## Consequences

- A feature can be installed into a host pinned at an older contract version whenever its declared
  floor allows, which is the behaviour FR-2's resolution-time gate depends on.
- Version numbers are never hand-maintained, so version lines cannot conflict between concurrent pull
  requests and a changed package cannot be published at a stale version.
- FR-2's publish-time gate no longer needs to stitch nuspec ranges from the dependency map. Ranges
  derive from project versions, and unchanged packages keep theirs.
- The dependency map becomes load-bearing for publishing, not only documentation: it resolves file
  ownership for the patch computation and generates the `SharedAssemblies` list and the host
  compatibility manifest.
- A revert increments the patch rather than decrementing it, which is correct because the content
  changed again.
- The patch is **monotonic by construction**: it is always one past the last published value, rather
  than a count that happens to increase. Change detection is a boolean, so a rename marks a package
  changed and bumps it — eager, never wrong — where a count would have silently undercounted.
- Version numbers remain stable only against a stable history. Rewriting `main` renumbers, because the
  last-published record and the change detection both read history. No scheme considered here survives
  a rewrite.
- Publishing now depends on the dependency map being **correct**, not merely present: a stale or wrong
  last-published record yields a wrong version. This deepens an existing dependency rather than adding
  one, since the map already resolves floors and change detection and already carries a freshness
  gate.
- Consumers hold a release number rather than a package version. A generated release manifest records
  which package versions constitute a given release.
- Line A membership is not fully settled. Six packages sit in a band the selection rule does not
  decide (`Elsa.Expressions.JavaScript.Core`, `Elsa.Locking.Core`, `Elsa.Http.Core`,
  `Elsa.Pipelines.Core`, `Elsa.Caching.Core`, `Elsa.Modularity.Core`). Which of these a host must pin
  is determined by the clean host specification, not by this decision.
- Nuplane must promote a domain's `.Core` to a shared assembly within that domain's subtree, for
  first-party and third-party domains alike; otherwise two features in one domain load separate copies
  and their types do not match. That work belongs to the clean host effort.
- Contract tests at each seam are required for an independently versioned package to be trustworthy
  on its own, since a green build of the whole tree only proves the combination that was built.

## Linked decisions

- [Elsa packaging snapshot](../../.specify/memory/constitution.md): §E5, which this decision amends
- [Framework §2.13 packaging and versioning, §4.1 per-package versioning, §4.2 SemVer for `.Core`](../../.specify/memory/constitution-framework.md)
- FR-1 discussion: https://github.com/elsa-workflows/elsa-foundation/issues/1144
