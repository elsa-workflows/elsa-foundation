# Extensions

Optional modules live here. Backend essentials are in [`../Essentials/src`](../Essentials/src),
deployable backend hosts in [`../Essentials/apps`](../Essentials/apps), and the Studio shell in
[`../Studio`](../Studio).

The tooling that enumerates modules was taught about this folder before any module moved into it, so
that each move is a pure move rather than a move plus a set of silent tooling regressions. See
[issue #1815](https://github.com/elsa-workflows/elsa-foundation/issues/1815) for the classification
that decides what belongs here, and for the staged plan.

## Layout

```
Extensions/
  <Name>/
    src/     one folder per project, mirroring the shape the module had under src/
    studio/  optional Studio projects and their Client workspaces, where present
    tests/   the module's test projects
```

`<Name>` is the capability bucket, not a project name. Agent, Elsa3 and ExtensionBuilder retain their
existing backend classification. Studio components occupy their named capability buckets; the two
Weaver projects share `Weaver/studio/`. This consolidation does not reclassify all optional backend
features or move backend essentials merely because a Studio component has a matching name.

The five backend extension test projects stay beside their capabilities under `tests/`. This is the
final placement for #2579 and supersedes the intermediate `tests/extensions/` move in closed PR #2620.

## Extensions a host composes at its root

Most extensions are CShells features: a shell lists them in `shells.json`, and nothing else is needed. One
is a root-hosted subsystem the host has to compose itself:

- **ExtensionBuilder** (`Elsa.ExtensionBuilder.Api`): see *Extension Builder* in the
  [Elsa glossary](../docs/glossary/elsa.md). Its root singletons, build worker and host-mapped routes cannot
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
site already covers `Extensions/`, and this is the complete list:

| Site | What it enumerates | How it would have failed |
|---|---|---|
| `.github/workflows/packages.yml` | nothing itself: it packs the dependency map's packable projects, which `RepoLayout.cs` below enumerates | the version calculator refuses a map that misses a project, so packing fails |
| `.github/workflows/ci.yml` | container-free test projects | the module's tests stop running, job still green |
| `Essentials/apps/Elsa.Workbench/Dockerfile` | restore inputs and the source copy | image build fails (the one that fails loudly) |
| `.dockerignore` | what reaches the build context | extension tests bloat the context |
| `tools/ef/generate-module-migrations.sh` | EF modules and model snapshots | finds no module, generates nothing |
| `tools/maps/Elsa.Maps.Generator/RepoLayout.cs` | projects, sources and catalogs for every map | the module vanishes from every map |
| `tools/solution-filters/profiles.json` | the integration profile's `Extensions/` prefix | the module's Testcontainers suites drop out of the fast lane's exclusion and the EF suite inventory check, so they run in fast CI without Docker, where their fixtures can self-skip |

Some sites name a specific project **by full path** rather than enumerating a directory. Those do not
fail silently in the same way, but they do have to move with the module, and they are easy to miss:

| Site | What it names |
|---|---|
| `.github/workflows/ci.yml`, the `ef-container-suites` matrix | one `.csproj` path per container suite |
| `Essentials/tests/Architecture/Tests/ArchitectureGuardTests.cs` | the name-to-path convention, which has a branch per root |
| `Essentials/tests/Architecture/Tests/EfCoreDependencyGuardTests.cs` | the admitted-EF-path allowlist |
| `Essentials/tests/Architecture/Tests/EndpointSecurityTests.cs` | the directory scanned per endpoint group |
| `Essentials/tests/Architecture/Tests/ReusableActivityArchitectureTests.cs` | project and directory paths, and a path-prefix assertion |
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
  `Essentials/tests/Architecture/ExtensionBoundary/Tests`, which fails and names the offending edge.
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
   they reach the nightly EF container matrix: `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filter-roots
   Elsa.Server.Persistence.Integration.slnf` must list them, and `tools/ci/ef-suites.json` must name each one
   (`-- ef-suites-self-test` fails until it does).
4. Check that the dependency map still derives with `dotnet run --project tools/maps/Elsa.Maps.Generator -- dependency-map`
   (its output is git-ignored), and commit the solution filters only when they change. The dependency map and the
   documentation maps are not committed; see [Generated maps](../docs/contributing/generated-maps.md).
5. If the move introduces an extension-to-extension edge, declare it in the guard's allowlist with a
   comment saying why it is legitimate.
