# NuGet lock files

Every project in `Elsa.Server.slnx` restores against a `packages.lock.json` committed beside its project file (#2122).
`RestorePackagesWithLockFile` in `Directory.Packages.props` turns them on for every project in the repository.

A lock file records, for each target framework, every package the project's restore resolved, direct and transitive,
with its version and content hash, and every project its references reach. Two things follow:

- **CI restores in locked mode.** A locked restore takes exactly the versions each lock file names, and fails with
  `NU1004` when a lock file no longer matches its project: a version changed in `Directory.Packages.props`, a pin added
  or removed, a `PackageReference` or `ProjectReference` added or removed. A dependency change therefore lands only as a
  reviewed lock-file diff, and a version the feed has since removed fails the restore rather than resolving to another.
- **Generating and checking the maps needs no restore.** The dependency map's pinned-transitive edges
  ([spec 149](../../specs/149-canonical-dependency-map/spec.md) FR-012) are read from each packable project's lock file,
  so `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` is a scan of the checkout alone.

## Updating lock files on a package bump

1. Change the version in `Directory.Packages.props`, or add or remove the reference.
2. Run a plain restore of the solution:

   ```bash
   dotnet restore Elsa.Server.slnx
   ```

   Not `--locked-mode`, which refuses to write, and not `-p:CustomBeforeDirectoryBuildProps` (see
   [Computed versions](#computed-versions)). NuGet rewrites every lock file the change reaches and leaves the others
   untouched.
3. Review the `packages.lock.json` diffs and commit them with the change. They name every project the change reaches,
   test projects and transitive packages included.
4. Regenerate the maps, `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`: a pin moving moves the dependency
   map's edges to it. The generator refuses a lock file the tree has moved past, naming the project, so running it
   before step 2 says what to do.

A plain restore leaves a lock file that still matches its project as it is. `dotnet restore Elsa.Server.slnx
--force-evaluate` resolves every project afresh and rewrites every lock file; it is for deliberately taking whatever
the feeds resolve now, and its diff deserves the same review.

**When CI's locked restore fails with `NU1102` for a version your machine restores**, the lock file names a version the
feed no longer serves and your package cache still holds. NuGet resolves a range to the lowest version it can find, so a
machine whose package folder still holds a version resolves it, while a clean machine resolves the next one up; the
workflows tolerate the resulting `NU1603`, and without lock files the two simply built different versions. Write the lock files as a clean machine
resolves them, with an empty package folder and no HTTP cache:

```bash
NUGET_PACKAGES="$(mktemp -d)" dotnet restore Elsa.Server.slnx --force-evaluate --no-http-cache
```

The first lock files were written this way. They locked `ConsoleLogStreaming.AspNetCore` at `1.0.0`, what CI had
already been resolving through the tolerated `NU1603`, because nuget.org no longer served the `1.0.0-preview.13` that
`Directory.Packages.props` pinned at the time. `Directory.Packages.props` now pins `1.0.0` directly (spec 149 FR-005),
so that mismatch, and the warning it produced, are both gone; the pin and the lock files agree.

**A new project** gets its lock file from its first restore; commit it with the project. A locked restore of a project
with no lock file does not fail: NuGet writes one and carries on, unlocked. So
`tests/essentials/Architecture/Tests/NuGetLockFileTests.cs` fails for any project in `Elsa.Server.slnx` without a committed
lock file beside it, for two projects sharing a directory (and so one lock file), and for a committed lock file with no
project beside it.

## Where restores are locked

- Every `dotnet restore` step in `.github/workflows` passes `--locked-mode`: CI's build and test, architecture guards,
  core-only, EF container suites and Secrets EF composition jobs, the nightly integration run, and the Packages
  workflow's pack. `tools/architecture/restore-ci-project-graph.sh` passes its arguments through, and CI calls it with
  `--locked-mode`. Its Release and Debug restores share each project's lock file, which works because no project
  conditions a reference on the configuration.
- Every `dotnet run`, `dotnet build`, `dotnet test`, `dotnet pack` and `dotnet publish` step is locked too, either
  because a locked `dotnet restore` step already ran earlier and it passes `--no-restore` (or `--no-build`, which
  implies it), or because the command carries `-p:RestoreLockedMode=true` itself: `dotnet run --project
  tools/maps/Elsa.Maps.Generator` and `dotnet build tools/versioning/Elsa.Versioning.Publisher`, the tool projects a
  workflow builds and runs with no separate restore step, restore themselves locked against their own committed lock
  file this way. `WorkflowRestoreLockTests` (`tests/essentials/Architecture`) scans every workflow for one of these
  four markers on the same command and fails on a new step that restores unlocked.
- Local restores are not locked, so the restore after a change is what updates the lock files.
- **The one place CI does not restore locked**: Elsa.Workbench's Dockerfile restores its RID-specific ReadyToRun
  publish (`dotnet restore -r <rid>`) without `--locked-mode`. A locked restore requires each lock file to lock
  exactly one runtime, and one committed lock file cannot hold `linux-x64`, `linux-arm64` and CI's runtime-less
  restore at once; the Dockerfile's own comment on that step and [Container images](#container-images) below explain
  why. Its preceding runtime-less restore is still locked, so every package version this restore can land on is one
  that restore has already shown the feed still serves at the pinned version.

## Computed versions

The Packages workflow restores, builds and packs with `-p:CustomBeforeDirectoryBuildProps=<package-versions.props>`,
and so do the image builds; it gives every project under `src/` its computed version
([tools/versioning/README.md](../../tools/versioning/README.md#packing)). A lock file records the version of each project
reference its project's references reach, as the range a project entry depends on (`"Elsa.Primitives": "[4.0.0-dev, )"`),
so a lock file written under computed versions would differ from the committed one. Locked mode, though, compares a
project reference by name alone and never by version (NuGet leaves the version out on purpose, NuGet/Home#7935), so a
locked restore with computed versions passes and rewrites nothing.

Never update lock files with the computed-versions file, which would commit one publish's versions into them. For the
same reason, the dev version in project entries stays as it was written when `VersionLines.props` opens a new
`major.minor`: nothing compares it, so nothing fails, and the next restore that has another reason to rewrite a lock file
brings it up to date.

## Container images

- **Elsa.Foundation.Host** restores without a runtime identifier. Its Dockerfile copies each project's lock file with its
  project file into the restore layer, and both of its restores are locked. `HostEndpointMetadataTests` requires each
  copy, since a missing one would restore that project unlocked without a word.
- **Elsa.Workbench** publishes ReadyToRun, which needs a runtime identifier, and `dotnet restore -r <rid>` gives every
  project in the graph that runtime. A locked restore then requires each lock file to lock exactly that runtime, and one
  committed lock file cannot lock `linux-x64`, `linux-arm64` and CI's runtime-less restore at once. So the Dockerfile
  first runs a locked restore without a runtime identifier, which holds every lock file in the graph to its project and
  fetches exactly the locked versions, and the runtime-specific restores after it are not locked.

## Versioning

A lock file is a file its project owns, so the version calculator counts it among the package's inputs
([spec 150](../../specs/150-package-version-computation/spec.md) FR-002): a lock-file change advances its package. A
pin bump that reaches a package's nuspec advances it anyway (FR-003). A lock file also changes when a bump reaches the
package only privately, through a `PrivateAssets="all"` reference such as an analyzer or a source generator whose output
is compiled in, and then advances a package whose nuspec is unchanged. Adding `RestorePackagesWithLockFile` to
`Directory.Packages.props`, outside any `PackageVersion` entry, advances every package once (FR-004), and so does the
first commit of the lock files.

## Out of scope

- **Packages Nuplane loads at runtime.** Feature packages a host takes from its module feed are resolved by Nuplane, not
  by a project restore; Nuplane has its own lock file for them
  ([Pinning and integrity](../foundation-host-feeds.md#pinning-and-integrity)).
- **Repository-local .NET tools.** `dotnet tool restore` reads `.config/dotnet-tools.json`, which pins each tool's version
  and has no lock file.
- **Projects outside `Elsa.Server.slnx`**, such as the repros under `docs/reports/repros/`. Restoring one writes a lock
  file, which stays uncommitted.
