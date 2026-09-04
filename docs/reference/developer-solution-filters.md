# Developer Solution Filters

`Elsa.Server.slnx` remains the only authoritative full solution. The committed `.slnf` profiles are
smaller, task-oriented views for IDE navigation and inner-loop `dotnet build` / `dotnet test` work.
They do not replace the full build, architecture, generated-map, E2E, or integration gates required
by the affected work unit.

## Profiles

| Filter | Use it for |
|---|---|
| `Elsa.Server.Workflows.Publishing.slnf` | Publishing engine, API, persistence, and their normal tests. |
| `Elsa.Server.Workflows.Runtime.slnf` | Workflow and activity runtime, scheduling, resumption, tracing, and their normal tests. |
| `Elsa.Server.Workflows.Design.slnf` | Workflow and activity authoring/design and their normal tests. |
| `Elsa.Server.Foundation.Identity.slnf` | Identity, authorization, authentication providers, and their normal tests. |
| `Elsa.Server.Persistence.Groundwork.slnf` | Groundwork adapters across foundation and workflow domains, plus their container-free tests. |
| `Elsa.Server.Persistence.Groundwork.Integration.slnf` | All Testcontainers-backed tests, currently the Groundwork provider integration surface. |
| `Elsa.Server.Workbench.slnf` | Debugging the reference host without loading unrelated tests, samples, or benchmarks as roots. |

The Workbench profile is intentionally broad: the reference host directly composes much of the
product and therefore pulls a large source dependency closure. Use one of the domain profiles when
the host is not the thing being debugged.

## Use a filter

Open a `.slnf` file directly in an IDE that supports solution filters, or pass it to the .NET CLI:

```bash
dotnet build Elsa.Server.Workflows.Runtime.slnf
dotnet test Elsa.Server.Workflows.Runtime.slnf --no-build
dotnet sln Elsa.Server.Workflows.Runtime.slnf list
```

The generated files contain the complete in-solution `ProjectReference` closure in ordinal path
order. A benchmark or test-support project can therefore appear as a dependency even though it was
not selected as a profile root. Project references outside `Elsa.Server.slnx` remain buildable by
MSBuild but cannot be listed in a solution filter; the existing Groundwork provider-evidence importer
is one such tool dependency.

Microsoft documents that filtered MSBuild builds follow project dependencies automatically:
<https://learn.microsoft.com/visualstudio/msbuild/solution-filters>. Visual Studio's project-loading
behavior and filter UI are documented at
<https://learn.microsoft.com/visualstudio/ide/filtered-solutions>.

## Change or refresh profiles

`tools/solution-filters/profiles.json` is the source of truth. Profiles normally select normalized
project-path prefixes so new nested modules join the appropriate profile automatically. Required-root
assertions make renamed or accidentally omitted anchors fail closed. Testcontainers profiles are
selected from parsed `PackageReference` elements rather than raw text, and any project reference that
leaves `Elsa.Server.slnx` must be explicitly allowlisted. Exclusions apply only to roots; required
transitive dependencies are never removed.

After changing the manifest, a project name, or a `ProjectReference`, regenerate the committed files:

```bash
tools/solution-filters/generate-solution-filters.sh
```

PowerShell:

```powershell
tools/solution-filters/generate-solution-filters.ps1
```

The freshness check regenerates into a temporary directory and byte-compares every committed filter:

```bash
tools/solution-filters/generate-solution-filters.sh --check
```

PowerShell:

```powershell
tools/solution-filters/generate-solution-filters.ps1 -Check
```

The generator's dependency-free contract suite covers mixed path separators, transitive closure,
stable ordering and serialization, root exclusions, parsed package selectors, and missing versus
allowlisted external project references:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-self-test
```

CI runs the same check and asks `dotnet sln` to parse every committed filter. A new matching project
or changed dependency therefore makes the check fail until the generated profiles are refreshed.
The fast CI lane also asks the generator for the integration profile's explicit roots, ensuring each
parsed Testcontainers test belongs to the nightly lane while comment-only mentions remain in fast CI.

## Completion gate

A filtered green build is deliberately only an inner-loop signal. Before completing a work unit, run
the exact full gates named by its spec or quickstart. The repository-wide baseline remains:

```bash
dotnet build Elsa.Server.slnx
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```
