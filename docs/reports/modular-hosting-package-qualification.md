# Local CShells Package Qualification

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md), [#2500](https://github.com/elsa-workflows/elsa-foundation/issues/2500). Evidence date: 2026-10-08. This qualifies an unpublished candidate; no milestone is complete.

## Candidate and owner gates

Root qualified the clean combined CShells tree at `8e2a1896d66fd046720d6bf641aa1d1aa5b642bb` in `/tmp/hosting-cshells-runner`. It includes locally reviewed #144, #146, #147, #148 and #149. The upstream `.github/workflows/ci.yml` and `publish.yml` require a Release solution restore, build and test. Root ran those commands locally through the shared-machine build-slot wrapper:

```sh
dotnet restore CShells.sln
dotnet build CShells.sln --configuration Release --no-restore
dotnet test CShells.sln --configuration Release --no-build --logger trx --results-directory <qualification>/results
```

The Release solution build passed with zero warnings/errors. Actual TRX counters show **728 CShells.Tests and 31 CShells.Tests.EndToEnd passed**, zero failures or skipped tests. The latter suite exercises CShells.Workbench through its test application factory; it is distinct from Elsa.Workbench and Foundation's process/e2e acceptance gates. This is local macOS evidence, not the upstream Ubuntu CI result.

## Package family and provenance

Root packed all nine projects listed in the owner publication workflow using its Release `--no-build` recipe and `/p:Version=0.0.30-preview.local.2500.8e2a189`. This private local version is not a pipeline-issued preview and was not uploaded. The nine IDs are CShells, CShells.Abstractions, CShells.AspNetCore, CShells.AspNetCore.Abstractions, CShells.AspNetCore.Testing, CShells.FastEndpoints, CShells.FastEndpoints.Abstractions, CShells.Management.Api and CShells.Providers.FluentStorage.

Archive inspection confirmed:

- All nine `.nupkg` files have the same candidate version and nuspec repository commit `8e2a1896d66fd046720d6bf641aa1d1aa5b642bb`.
- Every package contains its assembly and XML documentation for net8.0, net9.0 and net10.0, plus its README. All internal CShells dependency groups use the matching candidate version.
- The runner's test-only Microsoft.Extensions.TimeProvider.Testing dependency is absent from all production nuspec dependency groups.
- Nine generated `.snupkg` files contain 27 portable PDBs. Reading their Source Link custom debug information confirmed every document map targets the candidate SHA. Remote availability of those unpublished-source URLs was not tested.
- A SHA-256 manifest identifies all 18 archives. The owner workflow uploads/pushes `.nupkg` files; local symbol archives do not establish symbol publication.

The actual main-triggered workflow derives `0.0.30-preview.<run number>`, rebuilds/retests and publishes through Feedz after success. Its eventual run, checked-out SHA, artifacts and visible feed identities must be inspected independently. This local qualification did not include final release publication. The owner subsequently authorized final GitHub releases after the upstream gates; follow the [release plan](modular-hosting-release-plan.md).

## External package consumer

The qualification directory contains a console consumer outside the CShells checkout, with only a `CShells` PackageReference at the private candidate version. Its local feed mapping and separate package cache prevent accidental source-project substitution. Assets and `.nupkg.metadata` confirm `CShells` and transitive `CShells.Abstractions` are packages restored from that feed, with no ProjectReferences. An independent read-only reviewer found no concrete false positive or cleanup flaw in the harness or provenance linkage.

The consumer compiled for net8.0, net9.0 and net10.0 with zero warnings/errors and ran successfully on each major runtime. It holds an old shell scope across reload and verifies identical host-owned objects, distinct generation-local singletons, exact committed catalog/snapshot identity, two overlapping build leases, release after old drain, settled-current activation-runner state, final shell drain and exactly one root disposal. The dedicated solution tests provide the causal provisional-settlement, retry and failure-path evidence; this harness is a package integration check of the successful path.

This consumer exercises the two changed core packages. Packing the higher packages and inspecting their dependencies does not establish Foundation's complete restored graph, its startup/readiness policies or its host/Workbench/EF/reload journey.

## Retained evidence and remaining gates

Artifacts are retained outside Git at `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/release-qualification-8e2a189/`: package archives, `SHA256SUMS`, `pack.log`, `package-inspection.json`, `source-link-inspection.json`, TRX files and `test-counters.json`, consumer source/config/assets/cache provenance, `consumer-runtime.log`, and portable-PDB inspector source. Its README contains rerun commands. The original source bundle later failed independent cloning at a shallow-history boundary despite passing `git bundle verify`. Use the replacement `cshells-through-149-8e2a189-full-history.bundle`, independently recovered and strict-fsck checked as recorded in the [publication preparation report](modular-hosting-publication-preparation.md); original evidence is retained.

**Current status (2026-10-08):** This is still local qualification of the unpublished CShells candidate `8e2a189`; it is not evidence of an upstream CShells package publication. Ownership [PR #150](https://github.com/valence-works/cshells/pull/150) follow-up `4067b0f` passed CI `37780187981`; Greptile is running after its remaining field-name correction. Catalog [PR #151](https://github.com/valence-works/cshells/pull/151) follow-up `0b63743` corrects its remaining declaration-style feedback and passed focused 25/25 plus library builds for net8/net9/net10; new-head CI `37780579896` passed, while new-head Greptile remains required. Prior CI passed at `44aa162`/`4522596`. The old combined-package proof does not qualify these newer sources. Dependent #147/#148/#149 still await integration/publication. Nuplane preview `0.0.11-preview.99` is published and its separate actual-package removal consumer passed; this does not qualify a CShells.Nuplane adapter. Foundation's package pins remain on `.94` until final release-family adoption. Final releases, Foundation adoption and the M3 deletion-safety/shared-root gates remain outstanding. This package proof cannot unblock pruning or final program qualification.
