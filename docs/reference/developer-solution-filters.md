# Developer Solution Filters

`Elsa.Server.slnx` remains the only authoritative full solution. The committed `.slnf` profiles are
smaller views for IDE navigation and inner-loop `dotnet build` / `dotnet test` work. They do not
replace the full build, architecture, generated-map, E2E, or integration gates required by the
affected work unit.

## Profiles

There are three views (#2577):

| View | Use it for |
|---|---|
| `Elsa.Server.Core.slnf` | The required core with no extensions and no apps, and its container-free tests. CI's core-only job builds and tests exactly this. |
| `Elsa.Server.slnx` (Full) | Everything. It is the solution itself, so it has no `.slnf` file. |
| `Elsa.Server.Workbench.slnf` | Debugging the reference host without loading unrelated tests or samples as roots. |

The Workbench profile is intentionally broad: the reference host directly composes much of the
product and therefore pulls a large source dependency closure. Open the full solution when you need
a single module and its tests instead.

`tools/solution-filters/profiles.json` also holds one profile that is never committed:
`Elsa.Server.Persistence.Integration.slnf`, every Testcontainers-backed test project plus its
project-reference closure. The fast CI lane reads its roots to leave those suites out, and the
nightly integration lane writes the filter on demand and runs it. To run that lane locally:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filter Elsa.Server.Persistence.Integration.slnf
dotnet test Elsa.Server.Persistence.Integration.slnf
```

Delete the generated file afterwards: the freshness check below rejects any uncommitted
`Elsa.Server.*.slnf` at the repository root.

## Use a filter

Open a `.slnf` file directly in an IDE that supports solution filters, or pass it to the .NET CLI:

```bash
dotnet build Elsa.Server.Core.slnf
dotnet test Elsa.Server.Core.slnf --no-build
dotnet sln Elsa.Server.Core.slnf list
```

The generated files contain the complete in-solution `ProjectReference` closure in ordinal path
order. A test-support project can therefore appear as a dependency even though it was not selected
as a profile root. Project references outside `Elsa.Server.slnx` remain buildable by
MSBuild but cannot be listed in a solution filter.

Microsoft documents that filtered MSBuild builds follow project dependencies automatically:
<https://learn.microsoft.com/visualstudio/msbuild/solution-filters>. Visual Studio's project-loading
behavior and filter UI are documented at
<https://learn.microsoft.com/visualstudio/ide/filtered-solutions>.

## Change or refresh profiles

`tools/solution-filters/profiles.json` is the source of truth. Profiles select projects by name, by
normalized project-path prefix, or by package closure, and a profile marked `"committed": false` is
generated only on demand. Required-root assertions make renamed or accidentally omitted anchors fail
closed. Testcontainers profiles are
selected from parsed `PackageReference` elements rather than raw text, and any project reference that
leaves `Elsa.Server.slnx` must be explicitly allowlisted. Exclusions apply only to roots; required
transitive dependencies are never removed.

`requirePackageReferencePrefixes` and `excludeWhenPackageReferencePrefixes` both read a project's
package closure: its own `PackageReference` elements plus every package reachable through the
`ProjectReference` graph. A test project that takes its container fixtures from another test project
therefore classifies as a container project even with no `Testcontainers` package of its own. While
only direct elements counted, the dashboard provider suite reached Testcontainers through a
`ProjectReference` alone, so it sat in neither the integration filter nor the fast lane's exclusion
set: the fast lane ran it without Docker, its fixtures self-skipped, and the job reported green.

After changing the manifest, a project name, or a `ProjectReference`, regenerate the committed files:

```bash
tools/solution-filters/generate-solution-filters.sh
```

PowerShell:

```powershell
tools/solution-filters/generate-solution-filters.ps1
```

The freshness check regenerates into a temporary directory and byte-compares every committed filter,
and fails on any `Elsa.Server.*.slnf` at the repository root that no committed profile owns:

```bash
tools/solution-filters/generate-solution-filters.sh --check
```

PowerShell:

```powershell
tools/solution-filters/generate-solution-filters.ps1 -Check
```

The generator's dependency-free contract suite covers mixed path separators, transitive closure,
stable ordering and serialization, root exclusions, parsed package selectors, on-demand profiles, and
missing versus allowlisted external project references:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-self-test
```

CI runs the same check and asks `dotnet sln` to parse every committed filter. A new matching project
or changed dependency therefore makes the check fail until the generated profiles are refreshed.
The fast CI lane also asks the generator for the integration profile's explicit roots, ensuring each
test that reaches Testcontainers -- directly or through a `ProjectReference` -- belongs to the nightly
lane while comment-only mentions remain in fast CI. The nightly lane writes the same profile with
`solution-filter`, after the freshness check, so the two lanes stay complementary.

## Completion gate

A filtered green build is deliberately only an inner-loop signal. Before completing a work unit, run
the exact full gates named by its spec or quickstart. The repository-wide baseline remains:

```bash
dotnet build Elsa.Server.slnx
dotnet test tests/essentials/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```
