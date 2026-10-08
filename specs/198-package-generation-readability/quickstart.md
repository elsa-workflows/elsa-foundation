# Quickstart: Package Generation Readability

## Prerequisites and package provenance

- Use a .NET 10 SDK for the Foundation readability library and its current test project.
- Current committed package references are CShells `0.0.30-preview.159` and Nuplane `0.0.11-preview.94`; they predate the required build-lease and committed-catalog APIs.
- For API preflight, use an isolated private copy with the actual CShells `0.0.30-preview.166` and Nuplane `0.0.11-preview.99` packages and that copy's own package pins and lock files. Do not commit those preview pins/locks to Foundation.
- Before merge/Done, refresh D7 and the release plan, then validate the actual Foundation host against the published stable upstream package references. Upstream source-project substitution does not satisfy that final gate.

## Existing test subjects to retain

T001 refreshed issue #2164 comments and all matching pull requests before edits: the current claim is this branch's claim, and no competing active claim or open PR was found. The matching historical PRs are merged. Preserve the existing readability tests' subjects and assertions for positive replacement evidence, candidate/readability before the first initializer, exact feature and sibling load-context pins, default/non-Nuplane contexts, catalog-uninitialized/unreadable/custom fallback, catalog-refresh publication counts, unknown-shell lifecycle fallback, first `BindTo` ownership, failed drains before and after provider disposal, and actual-CShells drain/disposal timing. Update only fixture setup/wiring needed for the lease seam; keep exact count assertions and their transition objectives.


## Private public-preview preflight setup

The preflight source copy was created outside the checkout from baseline `c7a7241f9b354fef4f8e14c1a71827ac7a143bd8`, then its own `Directory.Packages.props` was changed from CShells `.159` to `.166` and Nuplane `.94` to `.99`. Only the copy owns generated lock files. The checkout pins and locks were not changed. The isolated copy and package cache are under `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/foundation-2164-preview-preflight`. To reproduce the prepared implementation, check out organization branch `2339-package-generation-readability` and archive its current source (the original copy began at the baseline above, then received the reviewed source files):

```bash
PREVIEW_COPY=/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/foundation-2164-preview-preflight
mkdir -p "$PREVIEW_COPY"
git archive --format=tar HEAD | tar -xf - -C "$PREVIEW_COPY"
cd "$PREVIEW_COPY"
```

Set those package versions in the copied central file only, then restore with the copy's isolated cache:

```bash
NUGET_PACKAGES="$PREVIEW_COPY/nuget-cache" dotnet restore \
  tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj \
  --force-evaluate --packages "$PREVIEW_COPY/nuget-cache"
```

The first focused restore succeeded and resolved `CShells.Abstractions` `0.0.30-preview.166` from `https://f.feedz.io/valence-works/cshells/nuget/index.json` with package SHA-512 `LR11RzzAAAqJ7BbHoCjoy0X589fh0mVqw7Gcr4ZEok3JKQ6K/LCLtxfHsk0h7IF4nM4EXQLLSLpWouXFvlUT+g==`; it resolved `Nuplane.Loading.Abstractions` `0.0.11-preview.99` from `https://f.feedz.io/valence-works/nuplane/nuget/index.json` with SHA-512 `ddG0PSFDvwfx1holS+lYbyezACFGSa0thBFojB5R0UOYf4QJopTOC5zHttYS4sJA8sv1mIAMRv3BxW0Upgxe8g==`. Root independently verified all six upstream archives in this isolated cache against the previously qualified direct Feedz archives, including NuGet SHA-512 metadata and nuspec source commits. CShells source is `2d81b21023387e11231d48720434ab8e88b38a93`; Nuplane source is `eb2cf6c2ee1f79dc2c45fb83cc415bbe4856d0d4`. The six test-output net10 DLLs also match their public archive assets byte for byte.

## Project checks

In the isolated private preview copy, after setting only that copy's pins and lock files to the preview packages, use the build-slot wrapper already on `PATH`; do not bypass it. Restore the focused test graph once, then run the test project in Release without restoring again:

```bash
cd "$PREVIEW_COPY"
NUGET_PACKAGES="$PREVIEW_COPY/nuget-cache" dotnet restore tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj --force-evaluate

NUGET_PACKAGES="$PREVIEW_COPY/nuget-cache" dotnet test tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj --configuration Release --no-restore --logger trx
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

## Prepared-source qualification — 2026-10-08

Root integrated and reviewed the complete source and fixture delta. Independent pinned-snapshot review found no product correctness blocker and identified a failure-path test cleanup gap; nested cleanup now disposes the acquired lease even if the evaluation join fails. No existing test subject or assertion was deleted. The 25 manual cases and 6 original real-CShells cases are retained; new public-API coverage protects pre-read candidates, exact old selection, disabled features, repeated pre-provider failures, failed unpublished cleanup, failed terminal notification, late callbacks, awaited catalog-read races, DI identity, shutdown and stock/custom rollback publication.

The complete affected readability project passed **110/110**, zero failed/skipped, Release/net10, against the actual public `.166`/`.99` packages in the private copy. The test command builds the affected library/project graph; no default-pin or whole-solution success is inferred. The final source manifest and test-output DLL audit establish what was executed.

All four reversible product mutations compiled and failed the intended regression, then passed after exact source restoration: bypass conservative Begin protection; ignore a candidate's selected snapshot in favor of current-catalog evidence; release from an early lifecycle callback; suppress retired-set shrinkage. The shrink mutation failed awaiting the missing reintroduction report; restored source passed. An earlier current-catalog mutation attempt failed compilation and is explicitly excluded from causal evidence. Product edits after the accepted mutations are comments/XML only.

Detailed source manifests, TRX/logs, cache/DLL audits, mutation negative/restored records and the independent review are **local-only evidence** under `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/publication-preparation/`: `2164-final-preflight-source-v2.json`, `2164-final-full-results-v2/`, `2164-root-final-qualification-v2.json`, `foundation-2164-preview-cache-provenance.json`, and `2164-mutation-*/result.json`. The organization branch and #2164 progress comments are the public preparation record. Copilot was not requested; Greptile was not awaited, following D15.

This qualifies the prepared readability implementation, not an actual Foundation.Host/Workbench process using final stable packages. Committed central pins/locks remain `.159`/`.94`, which lack the required APIs. Keep #2164, Feature #145 and final qualification #2509 open; architecture/maps and actual stable-host E2E acceptance remain T016 work.

## Support boundaries

Stock CShells owns the combined teardown condition, including successful terminal notification for a published generation; Foundation does not add a competing release gate. A failed normal drain is diagnostic only and never releases a lease. Custom lifecycle-only registries cannot supply pre-catalog protection and retain a legacy pin indefinitely after a failed drain. A custom catalog without commit notifications is polled while replacements exist; if its detailed read alone fails while its readable generation stays unchanged, later recovery requires a generation change or another evaluation trigger. These paths remain conservative. Readability retirement is not proof of assembly collection or permission to delete package files.

## Final stable adoption

Upstream release readiness uses delivered upstream source tasks, their checks, and the Foundation consumer proof against the exact public previews. Coordinating Feature #145 stays open for downstream stable acceptance; its closure and M1's final end-to-end acceptance are not prerequisites for tags that must supply those stable packages. This avoids waiting for #2164 Done before publishing the packages #2164 must finally adopt. The safe-pruning release gate remains required independently.

Final qualification remains pending until D7's current published stable versions are available. Build and run the actual Foundation host using upstream NuGet PackageReferences, exercise readability before and after retirement/reintroduction, and record exact package versions and clean-source evidence. Foundation-owned project references are permitted; replacing upstream package dependencies with upstream source-project references is not. Complete the architecture guard and generated-map check against the qualified head before merge/Done; they remain pending in this plan.
