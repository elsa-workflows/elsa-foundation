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

Nuplane reads installed add-on metadata and browser files from the acquired `.nupkg` after verifying its recorded SHA-512 hash.
The shipped apps use this reader with their ordinary package stores.

The app supplies three files beside its `Program.cs`, as the shipped apps do:

- `shells.json` enables the shell features, at least `StudioApi`.
- `nuplane-management.json` registers the `studio-local-packages` directory feed. Set
  `Studio:Hosting:Nuplane:DirectoryFeedName` to upload to another registered directory feed.
- `appsettings.json` sets `Studio:BackendBaseUrl` and the `Studio:Auth` and `Themes` sections.

A host that needs a different composition, for example no Nuplane, composes the four packages itself; see the
[custom-host guide](../../../docs/custom-hosting.md). App-specific root services stay in the app. The Workbench app, for example,
installs the Console Stream capture hook and maps its hub in its own `Program.cs`, because Console Stream is a Workbench module.

The package declares no extension points, events or contributor contracts, and its only public types are the two composition
methods.
