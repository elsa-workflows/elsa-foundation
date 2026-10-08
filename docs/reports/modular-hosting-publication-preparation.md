# Modular Hosting Publication Preparation

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md). Status: prepared locally; no upstream PR, push, release or milestone completion is claimed. The repository-required Git/session preference remains pending.

## Reviewable submission material

Artifacts at `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/publication-preparation/` contain seven PR bodies with separate titles, isolated candidate diffs and a source/base/file/hash manifest. Product candidates remain exactly those qualified in the [evidence register](../plans/modular-hosting-upstream/evidence.md):

| Submission order | Task / branch | Qualified tip | Local isolated diff base |
|---|---|---|---|
| 1 | Nuplane #109 / `029-removal-completion` | `d6eac6e` | `21e2c24` |
| 2 | CShells #144 / `016-share-host-singletons` | `9e66d41` | `49c9126` |
| 3 | CShells #146 / `017-runtime-catalog-commits` | `3318f5b` | `49c9126` |
| 4 | CShells #147 / `018-shell-generation-build-leases` | `8b22edc` | combined ownership/catalog `352a25e` |
| 5 | CShells #148 / `019-settled-activation` | `f42a487` | `8b22edc` |
| 6 | CShells #149 / `020-activation-runner` | `8e2a189` | `f42a487` |

The Foundation documentation PR can accompany this sequence; it references #2500 without closing the program. Its branch incorporates current main `249cd21329a13d6d67da5fd8e2b0974579cb044b` and has only program documents/reports in its delta. Incoming main's ReplaySafe runtime changes are not part of this program's implementation.

The catalog task is independent of ownership; local combined history contains cherry-picked catalog commits. Root computed a real three-way merge of `9e66d41` and `3318f5b`: it succeeds without conflicts and produces tree `8abe777ccc07c5264de6bfd5d0ba0934ac307143`, exactly the tree of combined base `352a25e`. This proves the existing combined source matches those two candidates; it does not predict future main integration.

Keep one integration/review lane. Before each dependent submission, fetch actual main, preserve the qualified ref, integrate merged prerequisites without rewriting published history, inspect the resulting delta and re-run required exact-head checks/reviews. Do not present the pre-integration tests as proof for a changed PR head. Merge only through normal gates; owner pipelines publish previews from main, and actual package/feed/source identity must be verified before downstream adoption. Keep all candidate refs/checkouts and audited backups.

## Backup correction and recovery proof

The original `cshells-through-149-8e2a189.bundle` passed `git bundle verify`, which printed “complete history,” but a fresh clone failed on missing parents of shallow base `49c9126`. The earlier complete-history claim is corrected. Original failed bundle and clone/fsck diagnostics are preserved; successful verification inside the source repository was insufficient proof of independent recovery.

Root fetched missing history from the existing upstream remotes for both repositories. Remote configuration, product candidate SHAs and source trees stayed unchanged. New full-history bundles are retained at the program artifact root:

| Bundle | Candidate source tree | SHA-256 |
|---|---|---|
| `cshells-through-149-8e2a189-full-history.bundle` | `df827c9d05355bf7f21efc1c27653b887fd54f63` | `09fc048fa0bd3dd3b2f77f06f7c173a3b7e6d227d6f49c223ac70ce43da5875f` |
| `nuplane-through-109-d6eac6e-full-history.bundle` | `c4d27661f317c11f49afd22e72046b49158870f8` | `1e9f619f2b604d49f041c35e9c575fc8fc437fae0aa6650510006b9c9867e09a` |

An independent reviewer fresh-cloned each as a mirror, verified non-shallow state and all task refs, compared final source trees, and ran `git fsck --full --strict --no-reflogs` with no errors. CShells `020-activation-runner` contains 548 commits; Nuplane `029-removal-completion` contains 393. Logs and recovered clone paths are under `publication-preparation/backup-audit/`. Root separately checks exact artifact hashes and refs. This proves source recoverability; runtime/package evidence remains the previously qualified unchanged candidates.

## Remaining delivery gates

Git/session selection still gates push/PR; no remote operation is waiting in the background. After publication is allowed, require current-head CI/review, normal merges, owner preview pipelines and feed/source identity, then optional integration and Foundation adoption/e2e. M3 still requires its owner admission/compatibility decision and complete deletion-safety proof. Foundation #2354 and #2362 remain separately owned, and #2293 remains open. No release, adoption, pruning safety or end-to-end completion is inferred from submission preparation or backup recovery.
