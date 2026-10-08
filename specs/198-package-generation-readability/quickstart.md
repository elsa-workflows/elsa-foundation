# Quickstart: Package Generation Readability

## Prerequisites and package provenance

- Use a .NET 10 SDK for the Foundation readability library and its current test project.
- Current committed package references are CShells `0.0.30-preview.159` and Nuplane `0.0.11-preview.94`; they predate the required build-lease and committed-catalog APIs.
- For API preflight, use an isolated private copy with the actual CShells `0.0.30-preview.166` and Nuplane `0.0.11-preview.99` packages and that copy's own package pins and lock files. Do not commit those preview pins/locks to Foundation.
- Before merge/Done, refresh D7 and the release plan, then validate the actual Foundation host against the published stable upstream package references. Upstream source-project substitution does not satisfy that final gate.

## Project checks

In the isolated private preview copy, after setting only that copy's pins and lock files to the preview packages, use the build-slot wrapper already on `PATH`; do not bypass it. Restore the focused test graph once, then run the test project in Release without restoring again:

```bash
dotnet restore tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj --force-evaluate

dotnet test tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj --configuration Release --no-restore
```

The test command builds the library and test project once. A successful build using the current `.159`/`.94` pins cannot prove the new participant or commit-notification integration.

## Behavioral scenarios

The readability test project must retain and adapt the existing test subjects, including `SupersededPackageGenerationTests` and `SupersededPackageGenerationShellTests`.

- Exercise a candidate before catalog read, select an older exact feature snapshot, advance the committed catalog, drain the previous shell, and prove the candidate's lease still pins the selected contexts until provider teardown.
- Exercise failure before provider creation, initializer/promotion failure after provider creation, uncertain provider disposal, and overlapping candidates with different snapshots. Only confirmed no-provider unwind or confirmed complete provider teardown releases a lease.
- Drive a real CShells build with `.166` to prove Begin precedes catalog reading, the participant receives the build's exact feature list before feature construction, and lease release follows provider disposal.
- Preserve the lifecycle-fallback regression for a shell whose host-local initializer is absent: pin from first notification through confirmed drain, without applying that release path to lease-managed generations.
- Preserve lease-owned descriptor identity through its terminal lifecycle notification; a late callback after lease completion must not create a fallback pin.
- The root `IShellLifecycleSubscriber` factory resolves the canonical participant singleton before returning the existing `BindTo(root)` facade. Verify `Bound(host)` resolves the same root-owned singleton that CShells participant discovery uses, and root disposal stops its subscription/watch exactly once.
- Synthetic provider tests use a test-owned provider wrapper that disposes the provider before signaling confirmed lease completion; fake drains control the signal. Do not cast the real service provider, use reflection, or treat an initializer's disposal as provider teardown.
- Commit snapshots that grow the retired set, shrink it by reintroducing the exact assembly, and leave it unchanged. Publish after teardown for retirement, restore constraints after reintroduction without waiting for a live provider, and publish nothing for an unchanged set.
- Cover custom catalog fallback, unreadable/uninitialized catalogs, subscription reconciliation, and stopping/unsubscribing at root lifetime end.
- Keep exact publication-count assertions tied to a deterministic initial queue baseline/quiescence and the transition under test. Do not weaken assertions or use sleeps to hide expected initial queued work.

## Causal mutation checks

Use reversible mutations to show the new tests detect the contract they claim:

1. Bypass Begin: the preselection race must fail because no conservative candidate pin exists.
2. Substitute the current catalog snapshot for a candidate's selected snapshot: the old-snapshot race must fail.
3. Release on initializer completion or drain start rather than complete provider disposal: the teardown test must fail.
4. Compare only newly retired assemblies: the reintroduction/shrink test must fail.

Restore each mutation and rerun the affected test.

## Final stable adoption

Final qualification remains pending until D7's current published stable versions are available. Build and run the actual Foundation host using upstream NuGet PackageReferences, exercise readability before and after retirement/reintroduction, and record exact package versions and clean-source evidence. Foundation-owned project references are permitted; replacing upstream package dependencies with upstream source-project references is not. Complete the architecture guard and generated-map check against the qualified head before merge/Done; they remain pending in this plan.
