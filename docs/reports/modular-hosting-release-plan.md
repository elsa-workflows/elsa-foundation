# Modular Hosting Release and Foundation Adoption Plan

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md), [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500). On 2026-10-08, Sipke authorized new Git releases after the upstream work lands, following the existing version pattern, and then updating downstream Elsa Foundation. This supersedes the earlier preview-only limit; it does not waive implementation, review, safety or validation gates.

## Verified version baseline — 2026-10-08

| Repository | Latest published GitHub/NuGet release | Current development base | Intended next release |
|---|---|---|---|
| CShells | [0.0.29](https://github.com/valence-works/cshells/releases/tag/0.0.29) | `src/Directory.Build.props`: `0.0.30` | `0.0.30` |
| Nuplane | [0.0.10](https://github.com/valence-works/nuplane/releases/tag/0.0.10) | `.github/workflows/publish-packages.yml`: `BASE_VERSION: 0.0.11` | `0.0.11` |

The recent release history increments the last version component (`0.0.N`), rather than the middle component. The next bases are already incremented; no additional bump is currently needed. Re-query tags, GitHub releases, NuGet and the final merged version files immediately before release. If another release consumes the intended number, select the next unused version in the same series and update all dependent plans and pins. Never overwrite a published tag or package.

GitHub REST release/tag/main results and NuGet flat-container versions are retained at `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/release-planning/release-audit.json`. This is a release inventory, not proof that the candidates have landed or been published.

## Release ordering and evidence

1. Land and validate all upstream work required by the program, including optional integration and the still-gated safe-pruning outcome. Keep interim previews available for integration qualification; they do not satisfy final release acceptance. Review the exact final source and resulting-main checks before tagging.
2. Prepare version metadata and release notes in reviewed organization branches where changes are needed. Tag exact validated commits, using the existing unprefixed `0.0.N` convention. Publishing a GitHub release triggers package publication; pushing a tag alone is insufficient for these workflows.
3. Release Nuplane first through its existing `Publish packages` workflow. Its release event takes the package version from the tag and publishes the package family to NuGet.org and Feedz. Verify terminal jobs, the complete expected package set, package/source commit identity, dependencies and availability from a clean cache.
4. Update the optional `CShells.Nuplane` package to the released Nuplane version, and validate that dependency before the final CShells release. CShells uses one central family version. Ensure the new integration package is included in its explicit pack-project list; the current workflow enumerates nine packages and will not discover it automatically. Publish the CShells GitHub release through `Packages`, then verify NuGet.org/Feedz availability and exact package/source identity for the complete family. Do not claim symbol-feed publication from local `.snupkg` output alone.
5. Update Foundation after both final package families are available. Change every CShells and Nuplane central package pin together, add the optional integration package where adopted, and remove the superseded local glue while preserving host defaults and Elsa policy. Current pins are seven CShells IDs at `0.0.30-preview.159` and five Nuplane IDs at `0.0.11-preview.94`; intended final versions are `0.0.30` and `0.0.11`, subject to the release-time refresh above.
6. Follow [NuGet lock files](../reference/nuget-lock-files.md): restore the affected project graph to update every reached lock file, review dependency/content-hash changes, then deliberately regenerate and review the affected maps. Verify a clean-cache locked restore of the actual published packages and inspect package-source mapping: Foundation currently maps CShells to Feedz and Nuplane to NuGet.org/Feedz. Both release workflows publish to Feedz, so a feed migration is not implied.
7. Run the Foundation adoption gate: affected modularity, host/Workbench, cluster/EF, architecture, maps, package and relevant real-process E2E suites, including package change, overlapping generation drain, failure/recovery and preserved reload defaults. Review and merge the Foundation update and verify resulting-main checks. Record exact release tags, source SHAs, workflow runs, feed/package identities, lock-file hashes and consumer evidence on #2500/#2509 before declaring delivery complete.

No release is created by this plan. Production deployment and customer package-store mutation remain outside authorization. The M3 shared-root admission/deletion-safety gate remains required; the release request does not authorize bypassing it.
