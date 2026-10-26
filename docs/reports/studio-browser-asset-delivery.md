# Studio browser asset delivery

Architecture spike [#3352](https://github.com/elsa-workflows/elsa-foundation/issues/3352),
[Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340).
Source baseline: `4a6bba1a3e461a7567719e9d4c37034865fbe2b5`.

## Work-unit plan

Prove actual prebuilt Studio shell assets can be packed into a Razor library, restored into a tiny
independent ASP.NET Core host and served after publish without the source checkout or Node/pnpm.
Select one shell asset owner and a URL/file-provider contract, preserving `/studio`, the import map,
SDK/vendor/CSS/font URLs and a separate module's `/_content` convention. Consider a separate shell-assets
package only if a real consumer or lifecycle requirement demands one.

Use the actual built Web shell and Dashboard module, copied into owned temporary producer projects.
The tiny consumer uses only PackageReference archives in an isolated NuGet cache, no project references
or checkout paths. Inspect archive/publish bytes and actual HTTP requests; exercise the real browser ESM
imports. Record customization/override limits and negative controls. This is asset delivery qualification,
not production host API extraction, authentication or final full Studio consumer acceptance. No release,
publisher change or new production package is part of this spike.

## Selected asset owner and URL contract

Use **Elsa.Studio.Hosting as a single Razor class library**, containing the prebuilt standard browser shell.
No separate shell-assets package is justified by the consumer/lifecycle evidence: the Razor SDK already packs,
restores and publishes the complete payload without a consumer frontend build. Existing module Razor libraries
retain their own assets. Api/Core consumers that do not host the standard browser shell remain separate.

Set the Hosting library's `StaticWebAssetBasePath` to `/` and keep its payload under `wwwroot/studio`.
The package's root base is deliberate: the actual built index/import map/bundles have absolute `/studio/...`
references. The default `_content/Elsa.Studio.Hosting` base would not satisfy those URLs without an additional
mapping layer or rebuild. The qualified route uses the SDK's publish and ordinary static-file provider;
no embedded-resource server, asset proxy, generic file-provider framework or consumer copy target is needed.

| Responsibility | Owner and delivered location |
|---|---|
| Standard index, app bundles, CSS, images, fonts | Hosting Razor library; `/studio/...` |
| Import map | The actual packed `studio/index.html`; points to the packed SDK/vendors |
| Browser SDK and shared React/vendor bundles | Hosting; `/studio/sdk/...`, `/studio/vendor/...` |
| Dashboard or another installed/reference module | Its own Razor library; `/_content/<package-id>/studio/modules/...` |
| Runtime configuration script and server API routes | Hosting code, not immutable browser files; retain `/studio-runtime.js` and the existing server contracts |
| SPA routing and static caching | Hosting middleware/endpoints; preserve stable-entry revalidation and real missing API/asset responses |

Move the shell's frontend source/build ownership with Hosting in #3361, update workspace SDK/UI references and
build paths, and make package CI build it before packing. Do not leave a second shell payload/source copy in
the reference Web application. Keep browser build inputs out of the NuGet archive and consumer build graph.

## Customization and override boundary

The prebuilt assets reserve `/studio`. Module libraries use their existing `_content/<package-id>` namespace.
The shell currently uses absolute asset URLs and application routes such as `/dashboard`; moving the entire
application below an arbitrary PathBase is not a supported prefix switch proved by this spike. Keep this
constraint explicit rather than expose an option that rewrites only some URLs.

On .NET SDK 10.0.402, adding consumer `wwwroot/studio/index.html` alongside the root-based library fails in
`GenerateStaticWebAssetsDevelopmentManifest` (`Sequence contains more than one element`). An arbitrary same-path
file overlay is therefore not the customization contract. Adding a differently named consumer document works:
the probe publishes `custom-index.html` with the same real import map/assets and serves it through an explicitly
selected fallback, preserving a custom title. #3355/#3357 should support deliberate fallback-document selection
and the existing runtime/module/theme extension points. No caller should need to copy host classes or rebuild
the standard frontend to use those capabilities. Custom frontend source/bundles remain an explicit advanced
choice, with their own build; this package does not promise mutation of its compiled files.

For a fully custom frontend, the same package can retain its compiled .NET API while the consumer disables
its bundled asset integration with `ExcludeAssets="build;buildTransitive"` on the direct PackageReference.
The probe publishes that profile with a consumer-owned `studio/index.html`: the Hosting fixture DLL is
unchanged, its packaged .NET marker API executes over HTTP, standard vendor files are absent, and all independent
Dashboard assets remain available. The consumer uses the same verified archives/cache, runs after source/cache
locations are hidden and records no frontend-tool invocation. This is an explicit asset opt-out; the custom
frontend owns its SDK/vendor compatibility and build. Hosting must keep its build/buildTransitive integration
limited to the bundled assets so excluding it cannot disable essential server behavior. #3363 must repeat this
profile against the actual production Hosting API; the marker here belongs only to the temporary wrapper.

A separate shell-assets package would encounter the same URL collision and does not resolve this constraint.
The qualified opt-out also serves the custom-frontend consumer without another permanent package. Revisit that
split only for a demonstrated independent release/consumer requirement.

## Executed archive, publish and HTTP proof

The [reproducible tool](../../tools/studio/AssetDelivery.Probe/README.md) builds temporary Razor producers from
**actual** Web/Dashboard frontend output. The frontend input source is committed and frozen-install succeeds
for all 23 workspace projects. The selected frontend build includes Web/Dashboard dependencies. Source assets
are copied rather than linked into the producers; the resulting archives contain exact asset bytes.

The probe then creates a tiny ASP.NET Core consumer with exactly two PackageReferences, a fresh NuGet cache and
local archives as its only package source. It checks the assets graph has package types only, exactly one cache,
and both cached archives and restore SHA512 values match the actual packed archives. Locked restore and Release
publish pass. The entire published web payload matches the archives/source bytes. Producer directories, consumer
source, feed and cache are renamed out of their build locations before the published server is started.

Consumer restore/publish/server run with failing `node/npm/pnpm/npx` shims first on a restricted PATH; no frontend
invocation is recorded. Node drives the separate producer/browser automation, outside that consumer profile.
No repository project/source reference or consumer frontend build participates in delivery.

On macOS ARM64 with .NET SDK **10.0.402**, the final baseline and restored profile both passed:

- **140 resources** fetched from the published host with exact bytes, MIME and `no-cache` revalidation: 134 shell
  assets and six Dashboard assets, including 18 JS, three CSS, 117 font files, index and image.
- Actual import-map targets exist. Root and SPA fallback return the packed document; missing API/JS paths return
  404. A real JS Range request returns 206 with exact bytes. A separately named consumer fallback document works;
  the actual same-path collision is rejected during normal publish. The advanced asset opt-out publishes and
  serves the consumer's own document while retaining/executing the packaged .NET fixture API and module assets.
- Chromium **149.0.7827.55** executes the packed import map, SDK and shared React and imports the actual Dashboard
  ESM module. Calling its registration function with a contribution collector produces one navigation entry,
  three routes and six home widgets. This checks executable asset delivery, not backend data or page rendering.
- Removing published `vendor/react.js` makes the internal resource verifier fail; restoring its actual bytes
  passes again. The separate `--omit-published-vendor` run exits **1**, specifically at
  `HTTP asset mismatch: /studio/vendor/react.js (status 404)`. Normal baseline/restoration exit **0**.

Raw archive/source/publish digests, locks, graph, logs, browser output, probe hashes and all HTTP rows are retained
in the printed owned artifact directories. The local run index is `/tmp/studio-asset-optout-final-runs.json` and the
baseline/control/restored logs are `/tmp/studio-asset-optout-final-{baseline,negative,restored}.log`. These are local
receipts; this committed tool and report make the profile reproducible. No temporary package is published.

## Handoff and remaining acceptance

#3355 records the full class/package/public-API contract using this asset owner and the selected
[existing-service reuse](studio-hosting-reuse.md). #3357 implements host/middleware/runtime configuration;
#3361 owns actual Hosting frontend source, prebuilt archive payload and build-before-pack checks.
#3362 converts Web to that public package API. #3363/#3364 prove the actual production Hosting packages, full
Studio boot/backend/authentication, optional capabilities and custom modules. #3365 automates package inventory
and consumer/probe checks so the asset contract cannot silently rot.

This spike's producers contain real frontend assets with minimal .NET wrappers and temporary package IDs;
they do not contain the production Hosting implementation or the full module manifest/server feature.
The browser uses an explicit qualification document/contribution collector, not a simulated claim of complete
Studio application acceptance. Development-time static-manifest consumption, arbitrary application PathBase,
Windows/Linux execution, full page/data behavior and dependency closure of the final packages are not proved
by this profile. The program retains those required delivery/consumer gates; this decision does not close them.
