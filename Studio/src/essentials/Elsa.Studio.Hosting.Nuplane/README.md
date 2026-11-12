# Elsa.Studio.Hosting.Nuplane

Optional Studio package and feature administration. The base Elsa.Studio.Hosting package remains independent
of Nuplane and this addon. Public composition, installed configuration choices and supported store profiles
are implemented and qualified under [spec1010](https://github.com/elsa-workflows/elsa-foundation/tree/main/specs/1010-studio-optional-admin).

This package is delivered and qualified under Program #3340. The [custom-host guide](../../../docs/custom-hosting.md) explains candidate sources, optional composition and migration; remote release/publication is a separate action.

The optional routes use the installed selected shell, a frozen effective directory feed and the shared
configuration mutation lease. Feed edits take effect on restart. A cache-only directory can receive uploads
without declaring new desired packages; reconciliation/loading outcomes remain authoritative. Source-only
directories that do not resolve packages cannot be selected as the upload destination.

Installed metadata/assets require the coordinated immutable extracted-store profile. Nuplane owns admission,
identity/link checks and guarded bytes; the HTTP adapter adds preflight size and response limits. Ordinary,
unenrolled, archive and custom mutable installs receive explicit refusal/diagnostics. Host-owned nonphysical
static providers remain trusted immutable content; physical static-provider aliases into the install store
take the admitted path. No automatic enrollment, local prune algorithm or arbitrary external-writer guarantee.

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
before this composition call: their registration is frozen for the process. Select a directory feed explicitly
when more than one resolves packages. With none, the addon registers a `packages` fallback without a filesystem
watcher; uploads trigger reconciliation, while direct copies require manual or configured periodic reconciliation.
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
