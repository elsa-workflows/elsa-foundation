# Generated maps

Moved from `AGENTS.md` (#2578).

## Refresh generated maps

Windows / PowerShell:

```powershell
tools/maps/generate-maps.ps1
tools/maps/generate-domain-map.ps1
tools/maps/generate-extension-point-map.ps1
tools/maps/generate-architecture-reference-map.ps1
tools/maps/generate-feature-dependency-map.ps1
```

macOS / Linux / Bash:

```bash
bash tools/maps/generate-maps.sh
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

All ten paths above are thin shims over one .NET tool, `tools/maps/Elsa.Maps.Generator`. Either shell
works and both produce identical output, because there is now a single implementation. To refresh
everything in one go:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
```

The tool needs the .NET SDK. If it is unavailable, ask the user to install it before refreshing maps.
It needs no restore of the tree: the dependency map reads the packages NuGet pins for each packable project
transitively from the `packages.lock.json` committed beside it, and refuses a lock file older than the project's pins
or references. After a dependency change, update the lock files first; see
[NuGet lock files](../reference/nuget-lock-files.md).

**After regenerating, stage every changed map file by explicit path, `docs/maps/manifest.json`
included.** The manifest holds no per-commit provenance, so a run that changed no map leaves it
byte-identical and there is nothing to stage. When it does change, the change is real and the
freshness check compares it like any other generated file, so leaving it out makes the gate go red.
This reverses the earlier "never stage the manifest" rule, which existed because the manifest used
to carry `git_head` and an input fingerprint that differed per commit: that is what made PR #1247 go
`CONFLICTING` the moment #1248 squash-merged, while #1243, which left the manifest untouched, stayed
mergeable through both merges. Those fields are gone (#1278), so the conflict surface is gone with
them.

Generated maps are committed snapshots and are never refreshed automatically. CI does not
regenerate them; it only runs `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`.
That check regenerates every map into a scratch directory and compares the bytes with what is
committed, `manifest.json` included. It deliberately does **not** gate on any fingerprint over the
inputs: that would move on every source edit and would oblige every code PR to regenerate and commit
thirteen map files even though most source edits change no map at all. So a red check means a
committed map genuinely stopped describing the tree, and is worth acting on rather than restamping
past. Refreshing stays a deliberate, human-initiated act. The check runs on every pull request and
on every push to `main` (the `Generated maps fresh` job in `.github/workflows/ci.yml`), so main going stale is reported against main
instead of against whoever opens the next PR. A red run on `main` cannot block anything after the
fact; treat it as a request to open a refresh PR. Before relying on any map for navigation or
verification, run `-- check`: green means every committed map still describes the tree, and it is
the only authoritative freshness answer. `docs/maps/manifest.json` summarizes what the maps cover
but does not report staleness. If the check is red, or you cannot run it, report that the snapshot
may be stale and ask the user to invoke or authorize the narrowest relevant map refresh. After an
explicitly authorized refresh, review any generated findings report before continuing. Maps v2
scripts are split so the user can refresh only the layer they need.
