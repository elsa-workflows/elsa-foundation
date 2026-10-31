# External Studio module author

This fixture is materialized outside the repository. It imports only React and the actual packaged public Studio SDK. It has no Dashboard/private/source aliases or product ProjectReference. Its RCL consumes prebuilt `wwwroot/studio/modules/acme-home/module.js` and `module.css`; ordinary host restore/build/publish never invokes Node.

The module's JSX build emits `React.createElement` through its existing React import. Its only external runtime imports are the host-provided `react` and `@elsa-workflows/studio-sdk` entries; it does not require an additional `react/jsx-runtime` import-map entry.

Replace these tokens in the copied templates, then remove the `.in` suffixes:

| Token | Required input |
| --- | --- |
| `__PRODUCT_VERSION__` | Exact compatible local `Elsa.Studio.Core` NuGet version from the product receipt |
| `__CSHELLS_VERSION__` | Exact `CShells.Abstractions` version supplied by the producer |
| `__FIXTURE_VERSION__` | This independently authored `Acme.Home.Studio` package version |
| `__SDK_TGZ__` | Absolute path to the real retained Studio SDK tarball; dependency is `file:<path>` |
| `__REQUIRED_SDK_VERSION__` | `*` for the compatible fixture; the qualifier selects a genuine unsupported range for its incompatible variant |

Provide the copied author's `NuGet.Config` with deliberate mappings to the retained product feed and public dependency sources. The author needs Node/pnpm, TypeScript/Vite and .NET 10; the consuming host needs only the packed RCL and its public dependency closure.

From the copied external author's directory:

```sh
pnpm --dir Client install --lockfile-only
pnpm --dir Client install --frozen-lockfile
pnpm --dir Client typecheck
pnpm --dir Client build
dotnet restore Acme.Home.Studio.csproj --configfile NuGet.Config
dotnet restore Acme.Home.Studio.csproj --configfile NuGet.Config --locked-mode
dotnet pack Acme.Home.Studio.csproj -c Release --no-restore -o /absolute/owned/feed
```

Retain the SDK tarball hash, author's lockfile/resolved SDK declaration entry, typecheck/build logs, packed RCL hash and packaged/served JS/CSS hashes. No command here publishes a package or starts a host.

The settings route `/qualification/acme-home` stays registered regardless of the native widget's saved visibility. It launches the generic protocol fixture's JSON tool requests (`toolName`, `arguments`) with module-owned labels. Its registered context provider supplies the exact current resource reference; the prompt does not add a second reference to that same resource. Both read and reviewed visibility use the normal Agent path; the frontend never writes the preference document. Result presentation refreshes the authorized owning endpoint and exposes an ordinary native Home reload link only after matching server receipt/readback. The browser driver must click that link and observe the native Home layout independently.
