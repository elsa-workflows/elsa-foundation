# Modular Hosting Publication Preparation

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md). Current checkpoint (2026-10-08): Nuplane #110 and CShells #150/#151 have merged and published interim packages; the combined CShells primitives passed an actual-package consumer. Foundation coordination PR #2512 merged at `90966bd`; all six resulting-main workflows passed, while issue #2293 remains open. The sole active implementation leaf is CShells #147; no final release or milestone completion is claimed. The maintainer organization-branch publication path was confirmed on 2026-10-08; no Git workflow selection remains pending.

## Reviewable submission material

The table below records the original first-wave submission heads and their local diff bases; its states are historical, not current. Local recovery artifacts at `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/publication-preparation/` contain seven prepared PR bodies with separate titles, isolated candidate diffs and a source/base/file/hash manifest. The [evidence register](../plans/modular-hosting-upstream/evidence.md) records current heads and gates:

| Submission order | Task / branch | Qualified tip | Local isolated diff base |
|---|---|---|---|
| 1 | Nuplane #109 / `029-removal-completion` | `d6eac6e` | `21e2c24` |
| 2 | CShells #144 / `016-share-host-singletons` | `9e66d41` | `49c9126` |
| 3 | CShells #146 / `017-runtime-catalog-commits` | `3318f5b` | `49c9126` |
| 4 | CShells #147 / `018-shell-generation-build-leases` | `8b22edc` | combined ownership/catalog `352a25e` |
| 5 | CShells #148 / `019-settled-activation` | `f42a487` | `8b22edc` |
| 6 | CShells #149 / `020-activation-runner` | `8e2a189` | `f42a487` |

The Foundation documentation PR accompanied this sequence and merged as #2512 without closing #2500. Its original branch incorporated main `249cd21329a13d6d67da5fd8e2b0974579cb044b`; incoming ReplaySafe runtime changes were outside this program’s delta. The resulting main commit is recorded below.

The catalog task is independent of ownership; local combined history contains cherry-picked catalog commits. Root computed a real three-way merge of `9e66d41` and `3318f5b`: it succeeds without conflicts and produces tree `8abe777ccc07c5264de6bfd5d0ba0934ac307143`, exactly the tree of combined base `352a25e`. This proves the existing combined source matches those two candidates; it does not predict future main integration.

Keep one integration/review lane. Before each dependent submission, fetch actual main, preserve the qualified ref, integrate merged prerequisites without rewriting published history, inspect the resulting delta and re-run required exact-head checks/reviews. Do not present the pre-integration tests as proof for a changed PR head. Merge only through normal gates; owner pipelines publish previews from main, and actual package/feed/source identity must be verified before downstream adoption. Keep all candidate refs/checkouts and audited backups.

## Backup correction and recovery proof

The original `cshells-through-149-8e2a189.bundle` passed `git bundle verify`, which printed “complete history,” but a fresh clone failed on missing parents of shallow base `49c9126`. The earlier complete-history claim is corrected. Original failed bundle and clone/fsck diagnostics are preserved; successful verification inside the source repository was insufficient proof of independent recovery.

Root fetched missing history from the existing upstream remotes for both repositories. Remote configuration, product candidate SHAs and source trees stayed unchanged. New full-history bundles are retained at the program artifact root:

| Bundle | Candidate source tree | SHA-256 |
|---|---|---|
| `cshells-through-149-8e2a189-full-history.bundle` | `df827c9d05355bf7f21efc1c27653b887fd54f63` | `09fc048fa0bd3dd3b2f77f06f7c173a3b7e6d227d6f49c223ac70ce43da5875f` |
| `nuplane-through-109-d6eac6e-full-history.bundle` | `c4d27661f317c11f49afd22e72046b49158870f8` | `1e9f619f2b604d49f041c35e9c575fc8fc437fae0aa6650510006b9c9867e09a` |

An independent reviewer fresh-cloned each as a mirror, verified non-shallow state and all task refs, compared final source trees, and ran `git fsck --full --strict --no-reflogs` with no errors. CShells `020-activation-runner` contains 548 commits; Nuplane `029-removal-completion` contains 393. Logs and recovered clone paths are under `publication-preparation/backup-audit/`. Root separately checks exact artifact hashes and refs. This proves recovery of the original candidates named in the table. These bundles do not contain the later review-fix commits; those are preserved by organization branches and retained checkouts. Runtime/package evidence remains attributed to its exact source.

## Current publication checkpoint — 2026-10-08

Nuplane #110 merged via reviewed follow-up `5b6bccc` to main `eb2cf6c2ee1f79dc2c45fb83cc415bbe4856d0d4`. Main validation [run 37777197684](https://github.com/valence-works/nuplane/actions/runs/37777197684) passed 1,324 tests and three-OS jobs. Owner publication [run 37777044387](https://github.com/valence-works/nuplane/actions/runs/37777044387) passed; root verified all eight Feedz `0.0.11-preview.99` packages byte-identical to their pipeline artifacts and audited source/dependency metadata. The actual-package consumer qualification is recorded in the [Nuplane report](modular-hosting-removal-completion-qualification.md).

CShells ownership #150 at `911033d042c6ed76bdf9eb093c739a76c1c3cb40` and catalog #151 at `e172a3ae6a1e8454c813837f5a018e87e49acc7b` merged normally. Their combined main tree `9d2eb538463215e9cd94898a6d3a0e0d0a013032` matches the predicted merge. Packages run `37781957221` published all nine `0.0.30-preview.161` packages; each public Feedz archive was byte-identical to its owner workflow artifact. The outside-checkout consumer restored only `CShells` and `CShells.Abstractions` from public Feedz and passed on actual .NET 8.0.10/9.0.9/10.0.8. It verified ordered root identity and selected alias behavior, last-registration selection, shared-disposable retention through old/new shell overlap, provider-local singleton identity, final root disposal, exact catalog payload/snapshot identity, generation visibility and late-subscriber behavior. The complete proof is in the [evidence register](../plans/modular-hosting-upstream/evidence.md); retained external-consumer artifacts are under `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/cshells-published-primitives-consumer/`.

Foundation PR #2512 merged at `90966bd2a142bd6eb15cd5c3cc48ce4f8cdafda8`. All six resulting-main checks (CI, Code Quality, Docker Images, Packages, Maps and Solution filters) passed; retained run details are in `publication-preparation/foundation-2512-resulting-main-gates.json`. Issue #2293 remains open and is not represented as resolved. The Nuplane `.99` and CShells `.161` previews are interim only. Foundation pins remain at `.94`; no final releases or Foundation adoption are claimed.

CShells #147 is the sole active implementation leaf. Its locally integrated candidate is `fd6da4ac6f15ef55f8f0d2031373719b232a0e77`, based on the normal local prerequisite merge `5091ecb`; this is not an upstream merge. Release builds for net8/net9/net10 passed with zero warnings/errors, 76 focused tests passed, and the provider-teardown mutation proof passed. The complete affected-project suite passed 711 tests, and fresh independent source review found no concrete blocker. [PR #152](https://github.com/valence-works/cshells/pull/152) is open at this head; CI and Greptile are running. Upstream merge and publication remain pending. #148/#149 remain buffered. Copilot review was unavailable due to monthly quota; independent source QA is fallback evidence, not Copilot approval. No adapter integration, physical package pruning, final releases or Foundation runtime adoption is proved here. See the current [evidence register](../plans/modular-hosting-upstream/evidence.md) for the qualification details.

## Remaining delivery gates

The prior Git-selection hold is resolved: maintainer work uses organization branches without a preference-selection gate. Require current-head CI/review, normal merges, owner preview pipelines and feed/source identity, optional integration, then final GitHub releases and Foundation adoption/e2e under the [release plan](modular-hosting-release-plan.md). M3 still requires its owner admission/compatibility decision and complete deletion-safety proof. Foundation #2354 and #2362 remain separately owned, and #2293 remains open. No release, adoption, pruning safety or end-to-end completion is inferred from submission preparation or backup recovery.

## Historical first publication wave — 2026-10-08

| Candidate | Organization PR | Exact submitted head | Gate at initial submission |
|---|---|---|---|
| Nuplane removal completion #109 | [#110](https://github.com/valence-works/nuplane/pull/110) | `d6eac6e3faacb72b5acfb93c59d2862c340ca3a7` | [Validate attempt 2 passed](https://github.com/valence-works/nuplane/actions/runs/37773871404/attempts/2); Greptile 4/5, test follow-up in progress |
| CShells ownership #144 | [#150](https://github.com/valence-works/cshells/pull/150) | `9e66d41321083a456e132a4979c3ed997a367d9f` | [CI passed](https://github.com/valence-works/cshells/actions/runs/37773896187); Greptile follow-up required |
| CShells catalog #146 | [#151](https://github.com/valence-works/cshells/pull/151) | `3318f5b1564a033bc5650641231740541fad80a3` | [CI passed](https://github.com/valence-works/cshells/actions/runs/37773928444); Greptile follow-up required |

At initial submission, root refreshed upstream main/permissions/claims and confirmed no competing PR. The initial heads and source trees matched the locally qualified candidates; later corrections and current outcomes are recorded in the checkpoint above. Fresh read-only independent source review found no concrete blocker in all three; it ran no additional tests and does not replace hosted checks or bot review. Copilot review requests returned successfully but produced no review. GitHub’s reviewer picker explicitly reports “Monthly limit reached”; no Copilot approval is claimed. Root and fresh independent source review provide the documented unavailable-reviewer fallback, while hosted CI and Greptile convergence remain required. The Nuplane initial CI failure and successful second attempt are both retained. Foundation Greptile 4/5 identified missing outcome assertions in the standalone lock probe; CodeRabbit and independent QA also identified stale blocker statements. This follow-up corrects those reports and the probe without changing Foundation runtime behavior. Exact publication metadata is retained in `publication-preparation/first-wave-publication.json`. Dependent #147/#148/#149 remain prepared locally until prerequisites land; Foundation coordination documentation is published alongside.
