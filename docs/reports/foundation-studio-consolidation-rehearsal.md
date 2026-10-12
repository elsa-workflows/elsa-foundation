# Foundation and Studio consolidation rehearsal

Program [#2501](https://github.com/elsa-workflows/elsa-foundation/issues/2501), rehearsal
[#2510](https://github.com/elsa-workflows/elsa-foundation/issues/2510). This report records the isolated
candidate; it does not authorize a main merge, source switch, repository retirement or publisher activation.

## Sources and disposition

Foundation baseline: `0e0c1ff6fa101282cef65e1eabf030b72de32acf`.
Studio baseline: `3eef8fa788bfe2ebd4686df0e9a116532c9b0dc9`.
Both were captured in complete-history bundles during the acknowledged 11 October window. Original
Foundation lockfile changes, Studio untracked files and retained owner work remain outside the candidate.
The separate GitBook source merge `d4d7b6bbcc814d3b8229c933931115ccefe116ec` was subsequently incorporated.

| Source category | Candidate destination and reason |
|---|---|
| Foundation `src/essentials/` | `Essentials/src/`; existing backend classification retained |
| Foundation `src/apps/` | `Essentials/apps/`; both Workbench and Foundation Host retained |
| Foundation `tests/essentials/`, `tests/Shared/`, test props | `Essentials/tests/`; shared fixtures and nearest-props import retained |
| Foundation `src/extensions/<capability>/` | `Extensions/<capability>/`; existing `src/` and five test projects remain together |
| Studio `src/essentials/`, `src/apps/` | `Studio/src/essentials/`, `Studio/src/apps/`; native shared UI and shell organization retained |
| Studio `src/extensions/Elsa.Studio.<capability>/` | `Extensions/<capability>/studio/Elsa.Studio.<capability>/`; Weaver Core and Workflows share the Weaver bucket |
| Studio tests, examples, scripts, assets, package fixtures | Under `Studio/` with relative references adapted; examples remain separately consumable |
| Studio build and central-package props | Under `Studio/`; capability-level wrappers import only these Studio props, preserving product-specific pins |
| Foundation docs, tools, workflows, specs, samples | Existing shared root locations retained; active paths adapted |
| Studio docs, specs, provider instructions and `.github/` | Namespaced under `Studio/`; no colliding spec numbers or governance files flattened; nested workflows are inert provenance |
| Public documentation | Foundation `website/docs/`; dedicated GitBook owner retains implementation scope |
| Licenses and icons | Both identical MIT license files retained; product icons and package metadata remain explicit |
| Original dirty, untracked, ignored and private work | Retained in original owner locations; not imported, deleted, or represented as independently restored |

Classification follows the existing backend boundary. A matching Studio component does not by itself
reclassify a backend essential. No packages, assemblies, activities, schemas, APIs or licenses are renamed.
`Extensions/README.md` records the final colocated tests disposition for #2579; PR #2620 was closed as
superseded without merging or deleting its retained history.

## History and content proof

Foundation's pure relocation commit `87d14dd2b` preserves every one of its 8,911 tracked files' blob and
mode; 6,157 paths move. The essentials, apps and extension subtree object IDs are unchanged at that stage.
Configuration and path adaptations are subsequent commits, so a reviewer can separate content-preserving
moves from implementation changes.

Studio history is mapped under `Studio/` in an isolated copied repository. All 698 mapped commits were
checked against their originals: their subtree, author, committer and message bytes match. The original
Studio head is also retained as an ancestry parent through an additional merge. Mapping changes commit
IDs; it is not a claim that the mapped commits keep their original IDs. The mapping is committed at
[`tools/consolidation/studio-commit-map.txt`](../../tools/consolidation/studio-commit-map.txt).
Both file-disposition ledgers and the reproducible checker live beside it. From a full-history
checkout, run `python3 tools/consolidation/verify-history.py`; missing original or mapped objects
fail the check. The pure-move checkpoints are pinned in the checker, before subsequent adaptations.

Ordinary `git log --follow -- Studio/README.md` traverses the mapped history. Original commit/path queries
remain available through the retained ancestry. Capability relocation is a later pure-move commit and can
be followed independently. Public source history is never rewritten.

**Cutover must preserve the merge graph. Use a merge commit. Squash or rebase merging this candidate
would discard imported ancestry and invalidate the acceptance evidence.**

## Qualification gates

The independent pinned project manifest at
[`tools/consolidation/layout-manifest.json`](../../tools/consolidation/layout-manifest.json) accounts for
all 310 original projects, including tools and samples. Its verifier resolves 1,538 project references,
compares declared package/assembly identities, refuses old roots and unaccounted projects, and includes
failing controls for omitted and invented manifest entries. It complements evaluated build and package
checks; literal XML inspection alone does not prove effective MSBuild values.

Backend maps and architecture guards explicitly cover Essentials source, apps and tests plus each
extension's backend source/tests. They exclude Studio, which has separate solution, frontend and package
qualification. The backend map inventory independently discovers 281 projects and confirms 13 relocated
Studio projects are excluded. Studio's layout guard checks its solution, workspace, CSS and asset selectors.

Moving paths changes version-calculator input fingerprints. The candidate retains historical `src/tests`
snapshot discovery for already-published commits and uses new discovery for consolidated commits. The
synthetic migration gate checks stable package IDs, advancing patches, omitted old-root refusal and an
empty affected set after recording the migrated publication. Actual publish-state and feed floors must
be read again before cutover; no synthetic test proves a live feed is ready.

The implementation checkpoint `850b7d973220588b5d10c17b2bdeb3e0f7011cdc` passes backend Build & test
(14,935 passed, 32 skipped), Core-only build & test, Architecture guards, generated maps, Linux and
Windows backend source checks, all Studio CI jobs, and Workbench/Foundation Host image builds on
amd64 and arm64. Studio CI includes 457 .NET tests, exact 17-package verification and a standalone
image build without pushing. The real combined Studio/backend browser journey passed all nine
steps through a Completed workflow and incident inspection; its trace retains three aborted GET
requests, with no HTTP error response or page exception. Owned processes exited and the checkout
remained clean. This runtime evidence is separate from the mocked Studio browser CI.

Local qualification includes the calculator's 151 tests, publisher's 90 tests, 22 frontend asset
checks, all four packed npm libraries in a fresh consumer and browser, and a clean-cache consumer
of all 17 Studio NuGet packages. The independent history checker verified 8,911 Foundation files,
1,081 Studio files and 698 mapped commits. Independent recovery repositories passed complete-history
bundle read-back and fsck; that proves committed-source recovery only. The final computed backend
138-package pack and clean-consumer check are still in progress at this receipt. Final results and
any later-head checks must be recorded on the PR before it is declared ready. EF jobs were skipped;
CodeRabbit skipped the file-count cap, and no Copilot review or GitHub approval is recorded. None
of those absent checks is a pass. See the [root verification receipt](https://github.com/elsa-workflows/elsa-foundation/pull/2644#issuecomment-6114244229)
and [reviewed publisher control proposal](https://github.com/elsa-workflows/elsa-foundation/pull/2644#issuecomment-6114344002).

## Live cutover boundary

The acknowledged owner no-write window ended at 21:15 UTC on 11 October 2026. Its scope was immutable capture
and isolated rehearsal, not a global GitHub freeze. This candidate can continue from its captured copies;
fresh live capture or promotion requires delta reconciliation and renewed coordination. Idle sessions,
retained commit objects and clean checkouts are not independent recovery or working-file backup proof.

Before promotion: reconcile both repositories' new commits, open PRs, claims and publisher runs; obtain
owner dispositions for retained local-only/dirty/ignored work; verify recovery and exact final refs; agree
the bounded source switch and writer admission; then perform the reviewed source/publisher sequence in
the [source cutover runbook](consolidation-source-cutover.md), together with the
[publisher cutover runbook](consolidation-publisher-cutover.md). The source runbook records the
captured bundle proof and its limits, requires fresh owner-attributed delta reconciliation and a
pre-merge Foundation publisher hold, and defines abort and immutable-partial-publication recovery.
None of those future steps has been authorized or applied here. No branch deletion, worktree cleanup,
repository rename or archive follows implicitly from consolidation.
