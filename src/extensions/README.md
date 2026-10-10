# Extensions

Optional modules live here. Required ones are in [`../core`](../core), deployable hosts in
[`../apps`](../apps).

The tooling that enumerates modules was taught about this folder before any module moved into it, so
that each move is a pure move rather than a move plus a set of silent tooling regressions. See
[issue #1815](https://github.com/elsa-workflows/elsa-foundation/issues/1815) for the classification
that decides what belongs here, and for the staged plan.

## Layout

```
src/extensions/
  <Name>/
    src/     one folder per project, mirroring the shape the module had under src/
    tests/   the module's test projects
```

`<Name>` is the module bucket, not a project name: `src/extensions/Diagnostics` holds all eight diagnostics
projects, `src/extensions/Elsa3` holds all four Elsa 3 import projects.

## Extensions a host composes at its root

Most extensions are CShells features: a shell lists them in `shells.json`, and nothing else is needed. One
is a root-hosted subsystem the host has to compose itself:

- **ExtensionBuilder** (`Elsa.ExtensionBuilder.Api`): see *Extension Builder* in the
  [Elsa glossary](../../docs/glossary/elsa.md). Its root singletons, build worker and host-mapped routes cannot
  live in a shell container, so a host calls `AddElsaExtensionBuilder(configuration)` and
  `MapElsaExtensionBuilderApi()` itself. Its settings, under `Elsa:ExtensionBuilder`:
  - `Enabled`: the Workbench composes it only when `true` (`Elsa__ExtensionBuilder__Enabled=true` as an
    environment variable); off when absent.
  - `TrustedRoles`: every route but `/capabilities` needs the caller to hold one; the management key's
    principal is given them.
  - `StoragePath`: workspaces and build output; outside the tree by default.
  - `GitExecutable`, `DotNetExecutable`, `ServerLocalRepositoryRoots`.

  The routes also need the Elsa host management key (`Elsa:ModuleManagement:ApiKey`, sent as
  `X-Elsa-Module-Management-Key`): with no key configured they answer 404, with a wrong or missing header 401.
  The Studio relay (studio ADR 0037) names these routes, so `Elsa.ExtensionBuilder.Api.Tests` pins them.

## What a move does and does not change

Project names, assembly names, root namespaces and NuGet package ids are all driven by the `.csproj`
file name and its contents, so a folder move leaves every one of them alone. A consumer of
`Elsa.Activities.Bpmn` sees no difference.

What a move does change is every path-shaped reference to the project: the `ProjectReference` includes
that point at it, its entry in `Elsa.Server.slnx`, and any tooling that enumerates projects by path.
The last of those is the dangerous one, because most of it fails quietly rather than loudly. Every such
site already covers `src/extensions/`, and this is the complete list:

| Site | What it enumerates | How it would have failed |
|---|---|---|
| `.github/workflows/packages.yml` | nothing itself: it packs the dependency map's packable projects, which `RepoLayout.cs` below enumerates | the version calculator refuses a map that misses a project, so packing fails |
| `.github/workflows/ci.yml` | container-free test projects | the module's tests stop running, job still green |
| `.github/workflows/docker.yml` | paths that trigger an image build | image stops rebuilding on changes to the module |
| `src/apps/Elsa.Workbench/Dockerfile` | restore inputs and the source copy | image build fails (the one that fails loudly) |
| `.dockerignore` | what reaches the build context | extension tests bloat the context |
| `tools/ef/generate-module-migrations.sh` | EF modules and model snapshots | finds no module, generates nothing |
| `tools/maps/Elsa.Maps.Generator/RepoLayout.cs` | projects, sources and catalogs for every map | the module vanishes from every map |
| `tools/solution-filters/profiles.json` | the integration profile's `src/extensions/` prefix | the module's Testcontainers suites drop out of the nightly integration lane |

Some sites name a specific project **by full path** rather than enumerating a directory. Those do not
fail silently in the same way, but they do have to move with the module, and they are easy to miss:

| Site | What it names |
|---|---|
| `.github/workflows/ci.yml`, the `ef-container-suites` matrix | one `.csproj` path per container suite |
| `tests/essentials/Architecture/Tests/ArchitectureGuardTests.cs` | the name-to-path convention, which has a branch per root |
| `tests/essentials/Architecture/Tests/EfCoreDependencyGuardTests.cs` | the admitted-EF-path allowlist |
| `tests/essentials/Architecture/Tests/EndpointSecurityTests.cs` | the directory scanned per endpoint group |
| `tests/essentials/Architecture/Tests/ReusableActivityArchitectureTests.cs` | project and directory paths, and a path-prefix assertion |
| `EXTENSION_POINTS.md` | the root index of extension-point catalogs |

This second list was **missing when stage 0 described the first one as complete**; it was found by
auditing for hardcoded paths while moving the first module. Before moving a module, run a path audit
rather than trusting either list:

```bash
git grep -l -E "(src|tests)[/\\]<Module>[/\\]" -- . | grep -v '^docs/maps/'
```

That catches paths written as strings. It does **not** catch paths assembled from segments, which is
how `Path.Combine(RepoRoot, "src", "Elsa3", ...)` evaded exactly this audit during the Elsa 3 move and
broke a guard the first sweep had reported clean. Run the second form too:

```bash
git grep -n -E '"(src|tests)"\s*,\s*"<Module>"' -- '*.cs' '*.ps1' '*.sh' '*.py'
```

Then look for guards that scan a **root** instead of naming the module. A test doing
`EnumerateFiles(FullPath("src"), ...)` keeps passing once the module leaves `src/`, but it is no longer
looking at anything. Those need widening to both roots rather than re-pointing, or every move quietly
shrinks what they cover. Two such sweeps were widened during the Elsa 3 move.

Anything under `docs/reports/` or `specs/` that the audit turns up is a point-in-time record and is
deliberately **not** rewritten: those describe where a file was when the report was written.

Adding a project root to the repository means auditing both lists again.

## Rules

- **Core never references an extension.** Enforced by `ExtensionBoundaryTests` in
  `tests/essentials/Architecture/ExtensionBoundary/Tests`, which fails and names the offending edge.
- **An extension may reference another extension only when the edge is declared** in that same guard's
  allowlist. Undeclared extension-to-extension edges fail the same way.
- **Every project belongs to exactly one bucket.** No project sits inside another project's directory
  (#2577, guarded by `DependencyMapTests.No_project_directory_is_nested_inside_another`), so a project
  belongs to the bucket its own path lies under.

## Adding an extension

**Run `Elsa.Architecture.ExtensionBoundary.Tests` before pushing.** It is the guard a module move is
most likely to trip and the easiest to forget, because the move itself compiles and the other suites
stay green. Stage 2 pushed without it and CI caught a real boundary question the local run would have.


1. Move the projects, keeping each `.csproj` file name unchanged.
2. Fix the `ProjectReference` paths that pointed at them, and their entries in `Elsa.Server.slnx`.
3. Extensions get no solution filter of their own (#2577). If the module brings Testcontainers suites, check
   they reach the nightly lane: `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filter-roots
   Elsa.Server.Persistence.Integration.slnf` must list them.
4. Refresh the maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and stage every
   changed file under `docs/maps/`, `manifest.json` included.
5. If the move introduces an extension-to-extension edge, declare it in the guard's allowlist with a
   comment saying why it is legitimate.
