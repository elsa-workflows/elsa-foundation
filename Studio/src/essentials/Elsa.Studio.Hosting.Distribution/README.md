# Elsa.Studio.Hosting.Distribution

The Studio host composition every shipped Studio app shares. Each app (Foundation, Workbench) keeps a `Program.cs` of a few
lines and chooses only which Studio modules it compiles in. The package is the Studio counterpart of `Elsa.Foundation.Hosting`
on the server side.

```csharp
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Hosting.Distribution;

var builder = WebApplication.CreateBuilder(args);
builder.AddElsaStudioDistribution([typeof(FeatureManagementStudioFeature).Assembly]);

var app = builder.Build();
app.UseElsaStudioDistribution();
app.Run();
```

`AddElsaStudioDistribution` composes, in this order:

| Step | Package |
| --- | --- |
| `AddElsaStudio`, with the optional `StudioHostingOptions` callback | `Elsa.Studio.Hosting` |
| `AddStudioBackendsHostConfiguration` | `Elsa.Studio.Backends` |
| `AddElsaStudioNuplane`, uploading to the `studio-local-packages` directory feed | `Elsa.Studio.Hosting.Nuplane` |
| `AddElsaStudioThemes` | `Elsa.Studio.Hosting.Themes` |
| `ConfigureShells` with the app's startup assemblies, plus the Backends feature assembly | `Elsa.Studio.Hosting` |

The Studio API and Theme Store feature assemblies are declared by `AddElsaStudio` and `AddElsaStudioThemes`, and the package adds
the Backends one, so an app passes only the modules it adds. Every other module arrives from the Nuplane feed. Call it once,
before `Build`. `UseElsaStudioDistribution` calls `UseElsaStudio`, `MapElsaStudio` and `MapElsaStudioNuplane`. Map any
app-specific endpoints after it.

The app supplies three files beside its `Program.cs`, as the shipped apps do:

- `shells.json` enables the shell features, at least `StudioApi`.
- `nuplane-management.json` registers the `studio-local-packages` directory feed. Set
  `Studio:Hosting:Nuplane:DirectoryFeedName` to upload to another registered directory feed.
- `appsettings.json` sets `Studio:BackendBaseUrl` and the `Studio:Auth` and `Themes` sections. The shipped apps also turn on
  `Studio:Hosting:Nuplane:EnrollPackageStore`; see below.

A host that needs a different composition, for example no Nuplane, composes the four packages itself; see the
[custom-host guide](../../../docs/custom-hosting.md). App-specific root services stay in the app. The Workbench app, for example,
installs the Console Stream capture hook and maps its hub in its own `Program.cs`, because Console Stream is a Workbench module.

The package declares no extension points, events or contributor contracts, and its only public types are the two composition
methods.

## Package-store enrollment

Nuplane serves an installed add-on's browser files (its `module.js` and styles) only from an enrolled coordinated package
store. Set `Studio:Hosting:Nuplane:EnrollPackageStore` to `true` and `AddElsaStudioDistribution` sets that store up over the
content root:

- The store is Nuplane's install root, `.nuplane/packages` by default, with `.nuplane/store-state.json` as its state file
  (`Nuplane:StoreRegistry:StateFilePath` or `Nuplane:Setup:StateFilePath` moves it). The app declares itself the store's only
  client.
- On first start, before any reconciliation, recovery or package load, a store that holds no files is enrolled.
- On every later start the enrollment is reused. A start that finds the store enrolled changes nothing.
- A store that holds files but is not enrolled stops startup with a diagnostic, because another process may still use it. Empty
  the directory, or enroll it with Nuplane's enrollment protocol while every process that uses it is stopped (see the
  [Nuplane addon README](../Elsa.Studio.Hosting.Nuplane/README.md)). An interrupted enrollment also stops startup and needs
  Nuplane's enrollment recovery.
- Two processes that share a content root take Nuplane's root and member locks, which never wait. If both start on a fresh store,
  one enrolls it and the other reads the enrollment once it is published. While one process reconciles, the other's package
  reads wait briefly or are refused; neither interleaves writes.

The content root must be writable where the store lives, and the store must be on a filesystem Nuplane's native admission
supports. Nuplane 0.0.13 qualifies ext4 on Linux and refuses a store inside a Linux container, even on an ext4 mount; there the
app stops at startup and says so. The Workbench `Dockerfile` therefore sets `Studio__Hosting__Nuplane__EnrollPackageStore=false`.

The switch is off unless configured, so a custom host keeps an unenrolled store and its add-ons' browser files answer `503`. The
shipped Foundation, Essentials and Workbench apps turn it on in their `appsettings.json`. Set
`Studio__Hosting__Nuplane__EnrollPackageStore=false` to run one of them without enrollment.
