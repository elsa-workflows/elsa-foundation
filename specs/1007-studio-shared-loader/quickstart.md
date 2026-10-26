# Verify Studio's shared loader profile

Run from the repository root with its pinned .NET SDK:

```bash
dotnet test Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj
dotnet test Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj --no-build --no-restore --list-tests
dotnet restore Elsa.Server.slnx --locked-mode
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Studio maps `Elsa.Specifications.*` to the Elsa 4 preview feed, matching the root configuration.
A populated global package cache can hide an incorrect source mapping. To check this boundary, restore
`Elsa.Studio.Api.csproj` and `Elsa.Studio.Web.csproj` with `--packages` pointing at a new owned empty cache
and `--no-http-cache --force-evaluate`. Verify both Specifications preview.8 package metadata files name
`https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json` as their source. This is a producer restore check;
the separate NuGet-only consumer qualification still belongs to #3363/#3364.

`StudioPackageLifecycleTests` binds the actual Web `appsettings.json` and `nuplane-management.json`, overriding
only owned state/install/feed paths and background scheduling. It uses `WithStudioFeatureAssemblies`, which
selects the released adapter with `ChangedOrPending`, automatic reload and Studio result diagnostics.

The test creates real `.nupkg` files from the existing First/Second fixture assemblies. Each archive also includes
a CShells contract DLL copy: discovery and service resolution must still use the host's shared contract identity.
It drives the real Nuplane admin reconciler, loader, CShells runtime catalog and shell registry. It checks:

- Cold reconciliation defers activation; first activation discovers the installed feature.
- Update refreshes the catalog before a deliberately failed reload; the old shell still serves requests and
  the original failure is reported with its shell name.
- An unchanged reconciliation retries pending reload work and serves the updated assembly.
- The public observer propagates an already-requested cancellation. This focused callback check does not
  claim to exercise cancellation midway through Nuplane disk IO.
- Removal-to-empty drops discovery and new-generation services; a retained old-generation scope still works.
- A fresh unchanged empty completion leaves catalog/generation/reporting untouched.
- Retained scopes are released before the host stops, and shutdown completes.

`StudioGeneratedManifestTests` reads the API project's actual generated `elsa-package.json` and round-trips it
through `Elsa.Specifications.PackageManifests`. It checks Studio runtime-kind, feature identity and category hints,
so changing generator namespaces cannot silently reduce the manifest to server-default semantics.

For a bite-proof, temporarily change only `options.AutoReload = true` to `false` in Web's
`StudioFeatureAssemblies`, then run the lifecycle test with `--filter FullyQualifiedName~StudioPackageLifecycleTests`.
It must fail at serving-generation replacement. Restore the production profile and rerun the whole project.
Record the exact head/results on the PR; do not commit the negative control.

The two removed catalog-wrapper mirror tests are replaced by the real lifecycle and generated-contract tests.
Console Stream remains root-hosted in Program; its existing full-project regression tests still apply.
No test here proves collectible garbage collection, physical package pruning or an isolated NuGet-only Studio
consumer. Those remain the separate program qualifications in #3359 and #3363/#3364.
