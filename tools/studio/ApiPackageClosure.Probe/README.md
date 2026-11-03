# Studio.Api package dependency closure

This regression for [#3367](https://github.com/elsa-workflows/elsa-foundation/issues/3367) proves a normal
`PackageReference` to `Elsa.Studio.Api` supplies its public/runtime dependency closure. Run from the repository:

```bash
python3 tools/studio/ApiPackageClosure.Probe/qualify.py
```

The script packs Core and Api at an owned local version, creates a Web SDK host outside the repository with
one Api package reference, then restores with a fresh package/HTTP cache and repeats restore in locked mode.
Consumer restore, publish and runtime deny node/npm/pnpm/npx and retain a zero-invocation audit.
The host uses public API service/options
registration and enumerates API types/interfaces, including Core metadata. It publishes and checks candidate
archive and DLL bytes across the candidates, private NuGet cache and published payload. The entire graph must
contain package libraries and use only the owned cache. Finally it hides its feed, consumer sources
and package cache, starts only the published host, and verifies the real HTTP response and loaded assembly paths.
The producer repository is not moved; package-only assets and byte/path checks establish the input boundary.

For existing CI artifacts (the current Studio job uses this mode after pack):

```bash
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages /path/to/studio-nuget
```

Exactly one Core and Api package at a common version is selected. Other Studio packages in the artifact are
not injected as consumer references. Logs and `report.json` remain in the printed owned temporary root.
Only the script's own host process is terminated. This performs no publishing or deployment.

## Required-dependency control

Run the positive, a private dependency omission, and a restored positive against the same genuine candidates:

```bash
studio_packages=/absolute/path/to/studio-nuget
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages"
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages" --omit-api-core-dependency
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages"
```

Expected exits are0,1,0. The middle run retains both originals in its owned `original-packages` directory,
then removes only Api's Core dependency requirements from a private feed copy's nuspec. All non-nuspec entries
must remain byte-identical. Signed inputs and an already missing edge are rejected before restoring; the
supplied archives are never modified. A retained original directory can itself supply the restored positive.

The intended negative remains a **failed product proof**, with exit1 and `status: failed`. Its report must have
`stage: publish`, `negativeControl.expectedFailureObserved: true`, no `restoredCoreLibraries`, an Api package
library, the missing-Core `CS0234` diagnostic and zero `frontendInvocations`. Original/mutated archive hashes,
removed requirements, unchanged payload count, actual restored archive hashes and full logs remain available.
The separate observation confirms the expected failure; it does not turn that failed consumer into a pass.

Do not accept arbitrary exit1 as a valid control. Restore/network/SDK failures, signed or already broken inputs,
and other diagnostics leave the expected-failure observation false. Both fresh positive runs and their artifact
hashes must also match. Reports record the tool hash and identify supplied archives as external producer
provenance rather than inventing their source revision.

The full shell's missing-resource/restored proof is the existing
[production Hosting browser qualification](../HostingAssets.Probe/README.md). Its negative must fail specifically
with `HTTP asset mismatch: /studio/vendor/react.js (status 404)`. Program #3363 combines these two controls;
#3364 retains custom-module/addon/auth/live Console Stream journeys and #3365 owns their CI wiring.

Before the fix, the actual Api nuspec had no dependency group: the fresh consumer restored Api but not Core,
and `dotnet publish` failed with `CS0234` for `Elsa.Studio.Core`. Both candidate packages were in the local feed,
so this is a dependency-declaration failure rather than a missing artifact. Removing Api's blanket suppression
restores normal metadata; the source-only generator remains private, and CShells abstractions resolve through
the declared ASP.NET Core abstractions dependency.

This profile checks the Api library boundary, not a fully composed Studio shell/UI. The consumer does not
resolve the module-manifest provider, which needs the host's CShells catalog. The production shared-loader
lifecycle test remains the dynamic-package/shared-contract regression. No blanket change to dynamic module
package policies is made here; the program's custom-module and optional-capability consumer matrix retains
those separate qualifications.
