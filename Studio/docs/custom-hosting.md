# Build your own Studio host from packages

Use `Elsa.Studio.Hosting` for the common server pipeline and the prebuilt standard browser. Your application chooses its modules and configuration. It does not copy the private infrastructure from `Elsa.Studio.Web` or build the standard frontend.

The [small runnable sample](../../tools/studio/PackageHost.Sample/) selects Dashboard. Its [Program.cs](../../tools/studio/PackageHost.Sample/Program.cs) is the complete composition root:

```csharp
using CShells.DependencyInjection;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddElsaStudio()
    .ConfigureShells(shells => shells.WithAssemblies(typeof(DashboardStudioFeature).Assembly));

var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.Run();
```

That C# is the whole composition root. The sample still needs the downloaded `CustomStudio.csproj`, `shells.json` (enabling `StudioApi` and `DashboardStudio`), `appsettings.json` and `NuGet.Config`. `AddElsaStudio` loads `shells.json` from the content root unless you set `ShellsFile = null`.

## Choose packages

| Package | When to use it |
| --- | --- |
| `Elsa.Studio.Hosting` | Common hosting, selected-shell activation, runtime configuration, authorization/bridge, discovery and standard browser assets. Api/Core arrive transitively. |
| `Elsa.Studio.Dashboard` | The sample's Home module. Select other existing Studio module packages deliberately. |
| `Elsa.Studio.Hosting.Themes` | Optional Theme Store, directory sources, picker and management capabilities. Independent of Nuplane. |
| `Elsa.Studio.Hosting.Nuplane` | Optional package/feature administration using the shared CShells/Nuplane adapter and native store protection. Independent of themes. |
| `Elsa.Studio.ConsoleStream` | Optional console capture and streaming. Its host services and hub belong in the application root. |
| `Elsa.Studio.Api` / `Elsa.Studio.Core` | Public policy and module-author contracts when writing a module. Core remains lightweight; it is not a host infrastructure bucket. |

The reference distribution's [module assembly list](../src/apps/Elsa.Studio.Web/StudioFeatureAssemblies.cs) is an example of application-owned selection, not a required class to copy. The [accepted contract and class disposition](../../docs/reports/studio-hosting-contract.md) explain where the former Web classes went.

## Run the sample outside the repository

These commands use a .NET 10 SDK and a Bash-compatible shell. The producer currently pins SDK 10.0.300 with `latestFeature` roll-forward in [global.json](../../global.json). No Node, npm, pnpm or frontend source is needed by the consumer.

This is a **preview candidate recipe**. Program #3340 validates actual NuGet archives in Studio CI; it does not publish a release or activate a feed publisher. Obtain a retained successful **main** Studio CI artifact, or matching packages from a maintainer-provided feed. CI artifacts expire after 14 days: choose a current verified run rather than treating an old run ID as a permanent distribution.

1. In [Studio CI](https://github.com/elsa-workflows/elsa-foundation/actions/workflows/studio-ci.yml), choose a successful main run that contains `tools/studio/PackageHost.Sample` and download `studio-package-qualification-<run-id>-<attempt>`. Check `studio-consumer-qualification/report.json`: `status` must be `passed`; use its exact `version` and `toolSourceRevision` (the producer commit). [Program delivery receipts](https://github.com/elsa-workflows/elsa-foundation/issues/3340) identify verified runs.
2. Keep the extracted artifact **outside the application directory**. It includes retained test programs and private mutated control packages. Those are evidence, not consumer source or feed contents.
3. Copy only the original archives from `studio-nuget/` and `studio-modularity-nuget/` into the application's `packages/` folder. Do not recursively collect every `.nupkg` from the artifact.

For example, using GitHub CLI instead of downloading through the browser:

```sh
mkdir studio-host-work
cd studio-host-work

# Supply a current verified main run that includes PackageHost.Sample.
: "${RUN_ID:?Set RUN_ID to the verified main Studio CI run}"
ATTEMPT=${ATTEMPT:-1}
STUDIO_VERSION="4.0.0-studio-ci.$RUN_ID.$ATTEMPT"
SAMPLE_REF=$(gh run view "$RUN_ID" --repo elsa-workflows/elsa-foundation --json headSha --jq .headSha)
gh run download "$RUN_ID" --repo elsa-workflows/elsa-foundation \
  --name "studio-package-qualification-$RUN_ID-$ATTEMPT" --dir candidate-artifact

mkdir app
cd app
sample_url="https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/$SAMPLE_REF/tools/studio/PackageHost.Sample"
for file in Program.cs appsettings.json shells.json NuGet.Config; do
  curl --fail --location "$sample_url/$file" --output "$file"
done
curl --fail --location "$sample_url/CustomStudio.csproj.in" --output CustomStudio.csproj

mkdir packages
cp ../candidate-artifact/studio-nuget/*.nupkg packages/
cp ../candidate-artifact/studio-modularity-nuget/*.nupkg packages/

dotnet restore CustomStudio.csproj --configfile NuGet.Config -p:StudioVersion="$STUDIO_VERSION"
dotnet restore CustomStudio.csproj --configfile NuGet.Config --locked-mode -p:StudioVersion="$STUDIO_VERSION"
dotnet publish CustomStudio.csproj -c Release --no-restore -p:StudioVersion="$STUDIO_VERSION" -o published

cd published
ASPNETCORE_ENVIRONMENT=Production dotnet CustomStudio.dll --urls http://127.0.0.1:5090
```

Confirm the derived `STUDIO_VERSION` matches the artifact report. For a main run, `SAMPLE_REF` is its producer commit; downloading the sample at that commit keeps the recipe paired with its candidate distribution. The older development proof in the ledger predates this sample and is historical evidence, not a permanent default run ID.

Choose a free port if 5090 is already used. Open the printed URL: Home should load, navigation should contain Home, and a direct visit to `/overview` should load the same standard shell. `GET /_elsa/studio/modules` should list `Elsa.Studio.Dashboard` at the selected version. `/studio-runtime.js` should assign the sample's `custom-studio-sample` HostId. The published application does not need the source, package feed or restore cache at runtime.

The template uses exact package-version ranges. Pass `StudioVersion` on restore, build and publish commands; its placeholder deliberately cannot select an implicit latest version. Keep the resulting `packages.lock.json` with your application and use locked restore for repeatable builds.

The supplied `NuGet.Config` clears inherited sources and maps candidate Elsa Studio/Modularity identities to `./packages`. It explicitly includes the CShells, Nuplane and Elsa Specifications preview sources used by the qualification. In particular, do not infer nuget.org-only availability for the selected CShells 0.0.31 dependencies from a successful restore through these sources. If a maintainer gives you another trusted candidate source, replace that source's URL and retain deliberate identity/version mapping.

The sample has `Studio:Auth:Enabled=false` and no backend URL: it is a local browser-shell demo. It does not start a backend host or demonstrate a deployed backend journey.

## Configure modules, backend and authentication

Two choices are required for an in-box module: reference its package and pass its public feature assembly to `ConfigureShells`; enable the desired feature ID in the selected shell. The sample's `shells.json` enables `StudioApi` and `DashboardStudio` under `CShells:Shells:Default`. Hosting always retains mandatory Api assembly discovery. Other modules may require additional feature/package choices; follow their READMEs and the reference distribution's explicit list.

Studio UI module suppression and server feature activation are different controls. Hiding a client module does not remove its server endpoints or authorize requests. Existing dynamic-module packing suppression remains deliberate; do not infer a complete static dependency graph from every optional UI module's nuspec. The public Hosting/Api/addon boundaries and the actual consumer graphs are qualified separately.

Set `Studio__BackendBaseUrl` to the backend URL reachable by the browser. `Studio__BackendServerBaseUrl` optionally supplies a different server-reachable URL. Keep `Studio__BackendModuleManagementApiKey` in server configuration, such as an environment variable or your normal secret provider. It is never a browser runtime option. An unconfigured backend or unsupported management surface remains visibly unconfigured/read-only.

Enable authentication with `Studio__Auth__Enabled=true` and the existing [Hosting authentication options](../src/essentials/Elsa.Studio.Hosting/README.md). Studio's backend bearer/permission policies authorize its endpoints; an application cookie does not authorize Studio management. Existing application authentication registrations must be made before Studio registration. If the application had exactly one scheme and no explicit default, Hosting pins that scheme into `DefaultScheme` when adding its own named scheme; multiple schemes still require explicit defaults. Configure the backend/session behavior for your deployment rather than copying private bridge handlers.

Call `AddElsaStudio` once. Add optional capabilities and root customizations before `Build`. Call `UseElsaStudio` before other explicit routing/authentication middleware, then map the core and optional endpoints. Hosting options bind `Studio:Hosting`, with the explicit callback winning. `ShellName`, `ShellsFile`, fallback `IndexFile` and trusted query-token prefixes are installed snapshots; change them by restarting. Relative paths use the application's content root. If the host replaces the shell provider/registry or uses `ShellsFile=null`, file administration treats that configuration as host-owned/read-only. Full option and lifetime details belong to the [Hosting README](../src/essentials/Elsa.Studio.Hosting/README.md).

## Add optional capabilities

### Themes

Reference `Elsa.Studio.Hosting.Themes` at the same candidate version and import `Elsa.Studio.Hosting.Themes`. In the sample, retain the registration result by changing `builder.AddElsaStudio()` to `var studio = builder.AddElsaStudio()`, leaving its `ConfigureShells` chain in place. Before `Build`, call:

```csharp
studio.AddElsaStudioThemes(options =>
{
    options.StoreFile = "state/themes.json";
    options.AssetsDirectory = "state/materials";
});
```

Enable `ThemeStoreCore`, `ThemePicker` and/or `ThemeManagement` in the selected shell. `MapElsaStudio` maps their selected endpoints; no separate theme mapping call is needed. Keep managed store/material paths writable and application-owned. Directory/collection sources are read-only inputs, distinct from the managed store.

Use the typed `StudioThemeSourceMode` and existing `Themes` configuration described by the [Themes README](../src/essentials/Elsa.Studio.Hosting.Themes/README.md). Broken/cyclic or externally mutated source trees are not qualified by the normal-file/link profile; request-time link cycles can fail closed with 500. The program does not promise arbitrary-filesystem recovery or measured scaling behavior.

### Package and feature administration

Reference `Elsa.Studio.Hosting.Nuplane` at the same candidate version and import `Elsa.Studio.Hosting.Nuplane`. Retain the `studio` registration result as described above, call `studio.AddElsaStudioNuplane()` before `Build`, and call `app.MapElsaStudioNuplane()` after `MapElsaStudio`. The server addon and optional management UI modules are separate package/feature choices; enable the UI modules you actually want.

The addon loads `nuplane-management.json` by default. With no resolving directory feed it registers a `packages` fallback without a watcher; uploads trigger reconciliation, while direct file copies need manual or explicitly configured reconciliation. Select an effective directory feed deliberately when several exist. Feed and file choices are frozen for the process. The existing public Nuplane builder callback configures trusted feeds/loading/store ownership; the addon does not introduce another membership protocol.

Installed metadata/assets require the coordinated immutable extracted-store profile and explicit native admission/enrollment. Ordinary, unenrolled, archive or custom mutable stores receive refusal/diagnostics. Adding the addon does not perform automatic cutover or authorize pruning. Use the qualified public Nuplane ownership protocol for trusted enrollment, Preview and separately confirmed execution. Candidate paths are descriptive, never deletion instructions. See the [Nuplane addon README](../src/essentials/Elsa.Studio.Hosting.Nuplane/README.md) and [store qualification](../../docs/reports/studio-package-store-qualification.md).

The supported retention setting is `Studio:PackageManagement:RetentionPolicy:KeepNewestInactiveVersionsPerPackage`: `null` retains all installs; zero deliberately keeps no extra inactive versions. Active/protected versions remain protected by Nuplane. Legacy count/age/automatic cleanup settings are incompatible until an operator explicitly saves the supported policy; there is no silent migration or automatic deletion. A successful management-file save normalizes JSON and does not preserve comments. Custom/overlaid configuration stays readable but refuses local changes. Atomic saves and reloads share Hosting's process-lifetime configuration lease, not a cross-process file transaction.

CShells.Nuplane owns change sequencing and shell replacement. Use its public `NuplaneIntegrationOptions.OnReloadResults`/logging for diagnostics; the current addon logs result names/errors and successful reload counts. Additional correlation/change/catalog facts must come from their owning public contracts. Do not restore the former duplicate Studio coordinator. Optional zero-count log verbosity refinements are not a delivered behavior change here. The qualification observer lookup is pinned to `IShellGenerationBuildParticipant`, as noted at the fixture.

Workbench Server's pre-existing read-only administration is a separate profile. This program does not claim native Workbench maintenance convergence.

### Console Stream

Keep the capture hook, host service registration and hub/HTTP mapping in the application root. A live connection must outlast disposable shell generations. If selected, register the existing Console Stream public feature assembly and use its public host extensions, as the [thin reference Program](../src/apps/Elsa.Studio.Web/Program.cs) does. Its early hook runs before creating the builder; its services register before `Build`; mapping uses `StudioBridgeAuthorization.PolicyName`. Configure only the explicit trusted `ConsoleStreamStudioServiceCollectionExtensions.HubPath` for query tokens. Do not add another shell-owned streaming server or allow arbitrary query-token paths.

## Custom frontend and custom modules

The standard payload reserves root-mounted `/studio`; module assets retain `/_content/<package-id>/...`. A separately named fallback document can use the standard bundles through `IndexFile`. The option does not relocate absolute bundle/application paths or promise arbitrary PathBase support.

For a fully custom frontend, put `ExcludeAssets="build;buildTransitive"` on the final application's **direct** Hosting PackageReference. Hosting's .NET APIs remain available, its bundled assets are omitted, and independently selected module assets remain. Supply your own document and compatible SDK/vendor/import-map integration. Same-path overlays collide with the SDK. Transitive re-export/PrivateAssets shapes need their own qualification; do not generalize the tested direct-reference opt-out.

A module producer owns its feature/manifest and prebuilt browser assets; the application consumes its package. The custom-module proof uses actual packed module versions and contributions and observes a real native package update before claiming reload behavior. Building a new module's own frontend is a producer responsibility, distinct from a default Studio host consuming prebuilt packages. See [Composition qualification](../../tools/studio/Composition.Probe/README.md).

## Migrate a copied Web host

1. Choose Hosting, your module packages and optional addons; keep application-specific shell/configuration and module selection.
2. Replace copied bootstrap, runtime/discovery, bridge/auth, activation and static-asset plumbing with `AddElsaStudio` / `UseElsaStudio` / `MapElsaStudio`. Remove the private infrastructure copies once your host uses those public calls.
3. Compose optional themes/Nuplane and root Console Stream through their public entry points. Preserve your authentication and storage choices; do not copy internal handlers, stores, writers or reload coordinators.
4. Consume the prebuilt browser and module assets, or deliberately select the qualified custom-frontend shape. Producer frontend sources no longer belong in the consuming host.
5. Restore/publish/run the standalone host and verify your selected features and backend/auth configuration. Use the program's actual candidate proof as a baseline, not a guarantee for an untested deployment or filesystem/provider shape.

| Former Web responsibility | Reusable home / application choice |
| --- | --- |
| Common registration, selected-shell activation, runtime config, bridge/auth and browser delivery | `Elsa.Studio.Hosting`, using existing CShells contracts |
| Package/feature endpoints and safe configuration administration | `Elsa.Studio.Hosting.Nuplane`, using shared Nuplane store/lifetime protection |
| Theme store, sources and material assets | `Elsa.Studio.Hosting.Themes` |
| Console capture/server integration | Existing `Elsa.Studio.ConsoleStream` public helpers, composed at the application root |
| Distribution module list, root composition and application settings | Your application; the reference Web retains only Program and its module-selection class |

The [20-file disposition](../../docs/reports/studio-hosting-contract.md#complete-web-file-disposition) is the detailed source-linked record. Shell middleware resolution caching remains disabled because its host/path/method key omits endpoint/generation identity; this is a correctness boundary, not a throughput claim. Public unsealed/virtual theme feature types are intentional extension surface; private atomic writers remain package-local rather than introducing a general public helper.

## Evidence and supported limits

The [program ledger](../../docs/program-goals/studio-hosting-package-reuse.md) links M0–M3 accepted decisions, reviewed source delivery, actual candidates and resulting-main checks. The [qualification tools](../../tools/studio/README.md) describe the complete archive/consumer/security/theme/native/lifecycle controls and retained evidence. The [reports index](../../docs/reports/README.md) routes to the original hosting-reuse, browser-delivery, store and contract reports.

A passing basic sample does not stand in for optional/native qualification. Failed controls stay failed with separate recognition; quarantined tests, explicit skips, superseded runs and unsupported source/store profiles remain visible in receipts. No remote release, deployed backend behavior, automatic enrollment/pruning, arbitrary-filesystem safety or performance measurement is inferred.
