# Production Hosting browser package qualification

This tool packs the actual Core, Api, Hosting and Dashboard projects from an owned committed source snapshot, or consumes those four production candidates from `--packages`. It uses public Hosting/CShells composition in external Web SDK consumers; it creates no replacement library or host implementation.

Build the Studio frontend and commit production inputs first, then run from the repository root:

```sh
python3 tools/studio/HostingAssets.Probe/qualify.py
python3 tools/studio/HostingAssets.Probe/qualify.py --omit-published-vendor
python3 tools/studio/HostingAssets.Probe/qualify.py
```

Expected exits:0,1,0. The negative must report `HTTP asset mismatch: /studio/vendor/react.js (status 404)`. Use `--packages /absolute/artifact-directory` for already packed candidates at one version; `--version` controls only local producer packing. Nothing publishes remotely.

The isolated base-host graph must contain neither Backends nor Preferences.Core. A separately named consumer module checks the real default session/capability snapshot and adds a capability-gated shell contribution; Chromium verifies it at root and on a deep link. Optional backend-management qualification is separate.

Each run retains an owned artifact directory and report: source/tool provenance, exact archive/cache/publish DLL and asset hashes, isolated package graph, locked restore, denied frontend-tool audit, every resource's HTTP/MIME/cache result and real Chromium application/Home-module route. It exercises standard bundle, separately named fallback, same-path collision rejection and direct-reference `ExcludeAssets="build;buildTransitive"` customization. Consumer restore/publish/runtime deny node/npm/pnpm/npx. Owned producer, feed, consumer sources and cache are hidden before published execution; the shared working checkout stays available to other active programs. Only owned server processes are stopped.

The dynamic `/studio-runtime.js` endpoint is checked separately from packed resources: HTTP200, JavaScript MIME,
the runtime-global assignment and the configured nondefault `hostId: package-consumer-proof`. Standard and fallback
browser journeys verify that global value after both root and deep-link navigation; missing runtime responses
are included in the browser resource-failure checks. The opt-out profile still verifies the server runtime endpoint.

Two controlled generated-host faults must fail at `standard-runtime`:

```sh
python3 tools/studio/HostingAssets.Probe/qualify.py --packages /absolute/artifact-directory --runtime-script-fault missing
python3 tools/studio/HostingAssets.Probe/qualify.py --packages /absolute/artifact-directory --runtime-script-fault empty
python3 tools/studio/HostingAssets.Probe/qualify.py --packages /absolute/artifact-directory
```

Expected exits are1,1,0. The missing fault must report `Runtime configuration script has status 404`; the empty
fault must report `Runtime configuration script did not assign window.__ELSA_STUDIO_RUNTIME__`. These are test-host
middleware faults against real package composition, not changes to the product package. Unrelated restore/build
failures do not count as valid controls. Restore the normal profile after them and verify its runtime values in
the HTTP and browser reports. The browser driver accepts `STUDIO_HOSTING_RUNTIME_HOST_ID` as an optional exact
expected value; every invocation still requires the actual global and its string HostId.

Requires producer Node/pnpm, .NET10, Python3 and the Studio Playwright Chromium installation. Browser automation runs outside the denied consumer environment. This is actual production asset/composition proof; full backend/authentication/custom-module/optional-addon journeys remain Program3340's later consumer leaves.
