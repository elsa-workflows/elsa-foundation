# Elsa.Studio.Hosting.Nuplane

Optional Studio package and feature administration. The base Elsa.Studio.Hosting package remains independent
of Nuplane and this addon. Public composition, installed configuration choices and supported store profiles
are implemented and qualified under [spec1010](https://github.com/elsa-workflows/elsa-foundation/tree/main/specs/1010-studio-optional-admin).

This package is delivered and qualified under Program #3340. The [custom-host guide](../../../docs/custom-hosting.md) explains candidate sources, optional composition and migration; remote release/publication is a separate action.

The optional routes use the installed selected shell, a frozen effective directory feed and the shared
configuration mutation lease. Feed edits take effect on restart. A cache-only directory can receive uploads
without declaring new desired packages; reconciliation/loading outcomes remain authoritative. Source-only
directories that do not resolve packages cannot be selected as the upload destination.

Nuplane verifies the retained package archive against the hash recorded for the active package. Studio
reads browser assets and package metadata from that archive, applying entry size and response limits.
Browser assets from packages use content ETags for revalidation.
Physical static providers pointing into the mutable install root also use the archive reader. Both ordinary
and coordinated stores can serve these files. Pruning still uses Nuplane's existing coordinated store protocol.

An install acquired by an older Nuplane version needs a fresh acquisition to supply the retained archive.
Reconciliation can reuse an existing install of the same version. That leaves the archive absent.
Keep deployed state and protected installs. An archive read can report `HashNotRecorded` before reconciliation
records the expected hash, or `ArchiveMissing` when the install has no retained archive. Publishing and acquiring
a new package version supplies its archive.

Custom hosts using a coordinated store may see `503` with `Retry-After` on the Features catalog, apply,
or module registry routes while reconciliation holds the store's root lock.

## Compose a host

Reference Elsa.Studio.Hosting.Nuplane alongside your chosen Studio modules. Configure shell features in the
selected Hosting shell file, then compose before Build:

```csharp
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;

var builder = WebApplication.CreateBuilder(args);
var studio = builder.AddElsaStudio();
studio.AddElsaStudioNuplane();
// studio.ConfigureShells(shells => shells.WithAssemblies(typeof(MyStudioFeature).Assembly));
var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.MapElsaStudioNuplane();
app.Run();
```

The addon loads `nuplane-management.json` by default, relative to the content root. Options bind
`Studio:Hosting:Nuplane`; the callback can override `ManagementFile` and `DirectoryFeedName`. The optional
second callback uses the existing NuplaneBuilder for trusted store/loading configuration. Configure feeds
before this composition call: their registration is frozen for the process. Select an upload directory feed explicitly
when several are available. Studio excludes directories declared with `Directory.Role=Cache` from upload selection
and returns a conflict when management requests try to edit or remove those feed entries. The addon registers a
`packages` fallback without a filesystem watcher when no eligible directory is configured. Configure a separate
upload feed if a cache already uses `packages`. Uploads trigger reconciliation. Direct copies require manual or
configured periodic reconciliation.
Root-owned Nuplane services and the shared CShells adapter handle discovery and active-generation replacement.

Only an exclusively owned standard JSON source is writable. Custom/overlaid shell or management sources stay
readable but refuse local changes. Writes hold the same process-wide mutation lease through read/revision/write/
reload; an error after a completed atomic save explicitly reports that the configuration was saved but reload failed.
This is not a cross-process configuration transaction. A successful save normalizes JSON; comments are not preserved.

## Manual maintenance

The supported policy is `Studio:PackageManagement:RetentionPolicy:KeepNewestInactiveVersionsPerPackage`:
null retains all installs, zero deliberately keeps no extra inactive versions. Active and protected versions
remain governed by Nuplane's protection rules. Older count/age/automatic cleanup configuration is incompatible
until an operator explicitly saves the supported policy; it is never silently converted or used for deletion.

Studio presents Preview, native diagnostics/protection/inventory/candidate facts, then a separate execution
confirmation. Execute sends the observed root label/identity, enrollment epoch and retention policy; candidate
paths are descriptive and never execution inputs. Nuplane verifies confirmation, recovers graph uses when
appropriate, inspects again and replans under ownership. A preview does not authorize deletion, and execution
can refuse or stop after partial work. UI reports preserve observed physical outcomes on failure/cancellation.
Ordinary stores remain unenrolled unless a trusted operator performs the existing Nuplane cutover protocol.

Read/manage routes retain Studio's module-management permission policies. The browser receives no backend
management credential; older Server registry/policy data remains read-only through the existing bridge.
