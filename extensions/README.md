# Extensions

Optional modules live here. Required ones stay under [`src/`](../src).

This folder is empty on purpose. It was created ahead of the modules that will fill it, together with
the tooling that has to know about it, so that the first module move is a pure move rather than a move
plus a set of silent tooling regressions. See
[issue #1815](https://github.com/elsa-workflows/elsa-foundation/issues/1815) for the classification
that decides what belongs here, and for the staged plan.

## Layout

```
extensions/
  <Name>/
    src/     one folder per project, mirroring the shape the module had under src/
    tests/   the module's test projects
```

`<Name>` is the module bucket, not a project name: `extensions/Diagnostics` holds all eight diagnostics
projects, `extensions/Elsa3` holds all four Elsa 3 import projects.

## What a move does and does not change

Project names, assembly names, root namespaces and NuGet package ids are all driven by the `.csproj`
file name and its contents, so a folder move leaves every one of them alone. A consumer of
`Elsa.Activities.Bpmn` sees no difference.

What a move does change is every path-shaped reference to the project: the `ProjectReference` includes
that point at it, its entry in `Elsa.Server.slnx`, and any tooling that enumerates projects by path.
The last of those is the dangerous one, because most of it fails quietly rather than loudly. Every such
site already covers `extensions/`, and this is the complete list:

| Site | What it enumerates | How it would have failed |
|---|---|---|
| `.github/workflows/packages.yml` | projects to pack | the module stops shipping to NuGet, release still green |
| `.github/workflows/ci.yml` | container-free test projects | the module's tests stop running, job still green |
| `.github/workflows/docker.yml` | paths that trigger an image build | image stops rebuilding on changes to the module |
| `src/Apps/Elsa.Workbench/Dockerfile` | restore inputs and the source copy | image build fails (the one that fails loudly) |
| `.dockerignore` | what reaches the build context | extension tests bloat the context |
| `tools/ef/generate-module-migrations.sh` | EF modules and model snapshots | finds no module, generates nothing |
| `tools/maps/Elsa.Maps.Generator/RepoLayout.cs` | projects, sources and catalogs for every map | the module vanishes from every map |
| `tools/solution-filters/profiles.json` | solution filter membership | the module drops out of its filter |

Adding a project root to the repository means auditing that list again.

## Rules

- **Core never references an extension.** Enforced by `ExtensionBoundaryTests` in
  `tests/Elsa/Architecture/ExtensionBoundary/Tests`, which fails and names the offending edge.
- **An extension may reference another extension only when the edge is declared** in that same guard's
  allowlist. Undeclared extension-to-extension edges fail the same way.
- **Every project belongs to exactly one bucket.** A project nested inside another project's directory
  belongs to the bucket that owns the parent, because the parent's `Compile Remove` globs already tie
  them together.

## Adding an extension

1. Move the projects, keeping each `.csproj` file name unchanged.
2. Fix the `ProjectReference` paths that pointed at them, and their entries in `Elsa.Server.slnx`.
3. Add a profile to `tools/solution-filters/profiles.json` so the extension gets a generated filter,
   then refresh with `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters`.
4. Refresh the maps with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and stage every
   changed file under `docs/maps/`, `manifest.json` included.
5. If the move introduces an extension-to-extension edge, declare it in the guard's allowlist with a
   comment saying why it is legitimate.
