# Studio hosting reuse probe

Bounded compatibility proof for [#3351](https://github.com/elsa-workflows/elsa-foundation/issues/3351).
Consumes the released `CShells.Nuplane` adapter through NuGet and exercises the real feature catalog,
shell registry, feature configuration, replacement generations and retained scopes. Two small fixture
assemblies contribute the same feature identity with different services.

From the repository root:

```sh
dotnet restore tools/studio/HostingReuse.Probe/Runner/HostingReuse.Probe.csproj --locked-mode
dotnet run --project tools/studio/HostingReuse.Probe/Runner/HostingReuse.Probe.csproj -c Release --no-restore
dotnet run --project tools/studio/HostingReuse.Probe/Runner/HostingReuse.Probe.csproj -c Release --no-build -- --disable-auto-reload
dotnet run --project tools/studio/HostingReuse.Probe/Runner/HostingReuse.Probe.csproj -c Release --no-build
```

The normal run exits zero after four behavior groups: deferred cold discovery, update, removal-to-empty,
and unchanged empty completion. Update/removal verify discovery before reload completion, the replacement
generation's actual services and a retained old-generation scope. The control must exit **one**, specifically
because an update did not replace the serving generation. The restored normal run must exit zero again.

The public package baseline is CShells `0.0.31` and Nuplane abstractions/loading abstractions `0.0.12`, pinned by
the repository's central versions and these projects' lock files. JSON also reports assembly informational
versions/source commits; those values are not NuGet package versions (the Nuplane assembly reports `1.0.0`).

## Limits

The package catalog and reconciliation completions are scripted inputs. This proves the shared adapter's
composition with real CShells services, not `.nupkg` resolution, actual Nuplane loading, collectible assembly
unload, protected-store enrollment, physical pruning or HTTP/browser serving. Fixtures are ordinary referenced
assemblies, not packages installed from a feed. Those broader consumer and store proofs remain separate work
under #3353 and #3363/#3364. No application host or production package is changed by this probe.

## Real package-store and HTTP qualification

The sibling `StorePruning` project extends the scripted adapter proof for
[#3353](https://github.com/elsa-workflows/elsa-foundation/issues/3353). It installs real fixture archives,
enrolls its owned native store, retains old serving scopes, executes public pruning and serves a real
loopback HTTP Range request overlapping reconciliation. See the
[decision and limits](../../../docs/reports/studio-package-store-qualification.md).

```sh
dotnet restore tools/studio/HostingReuse.Probe/StorePruning/StorePruning.csproj --locked-mode
dotnet run --project tools/studio/HostingReuse.Probe/StorePruning/StorePruning.csproj -c Release --no-restore
dotnet run --project tools/studio/HostingReuse.Probe/StorePruning/StorePruning.csproj -c Release --no-build -- --release-reader-before-read
dotnet run --project tools/studio/HostingReuse.Probe/StorePruning/StorePruning.csproj -c Release --no-build
```

Expect baseline 0, control 1 at `Admitted HTTP range response failed`, restoration 0. The platform must
support Nuplane native store coordination; refusal is a failed qualification, never a reason to skip or
fall back to ordinary deletion. The 90-second cancellation bounds the run; request gates use causal task
signals. This executable is a manual qualification tool, not a test project or production package.
