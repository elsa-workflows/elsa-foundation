# Studio.Api package dependency closure

This regression for [#3367](https://github.com/elsa-workflows/elsa-foundation/issues/3367) proves a normal
`PackageReference` to `Elsa.Studio.Api` supplies its public/runtime dependency closure. Run from the repository:

```bash
python3 tools/studio/ApiPackageClosure.Probe/qualify.py
```

The script packs Core and Api at an owned local version, creates a Web SDK host outside the repository with
one Api package reference, then restores with a fresh package/HTTP cache. The host uses public API service/options
registration and enumerates API types/interfaces, including Core metadata. It publishes and checks candidate
DLL bytes across the archives, NuGet cache and published payload. Finally it hides its feed, consumer sources
and package cache, starts only the published host, and verifies the real HTTP response and loaded assembly paths.
The producer repository is not moved; package-only assets and byte/path checks establish the input boundary.

For existing CI artifacts (the current Studio job uses this mode after pack):

```bash
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages /path/to/studio-nuget
```

Exactly one Core and Api package at a common version is selected. Other Studio packages in the artifact are
not injected as consumer references. Logs and `report.json` remain in the printed owned temporary root.
Only the script's own host process is terminated. This performs no publishing or deployment.

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
