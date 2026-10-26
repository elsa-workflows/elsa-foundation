# Production Hosting browser package qualification

This tool packs the actual Core, Api, Hosting and Dashboard projects from an owned committed source snapshot, or consumes those four production candidates from `--packages`. It uses public Hosting/CShells composition in external Web SDK consumers; it creates no replacement library or host implementation.

Build the Studio frontend and commit production inputs first, then run from the repository root:

```sh
python3 tools/studio/HostingAssets.Probe/qualify.py
python3 tools/studio/HostingAssets.Probe/qualify.py --omit-published-vendor
python3 tools/studio/HostingAssets.Probe/qualify.py
```

Expected exits:0,1,0. The negative must report `HTTP asset mismatch: /studio/vendor/react.js (status 404)`. Use `--packages /absolute/artifact-directory` for already packed candidates at one version; `--version` controls only local producer packing. Nothing publishes remotely.

Each run retains an owned artifact directory and report: source/tool provenance, exact archive/cache/publish DLL and asset hashes, isolated package graph, locked restore, denied frontend-tool audit, every resource's HTTP/MIME/cache result and real Chromium application/Home-module route. It exercises standard bundle, separately named fallback, same-path collision rejection and direct-reference `ExcludeAssets="build;buildTransitive"` customization. Consumer restore/publish/runtime deny node/npm/pnpm/npx. Owned producer, feed, consumer sources and cache are hidden before published execution; the shared working checkout stays available to other active programs. Only owned server processes are stopped.

Requires producer Node/pnpm, .NET10, Python3 and the Studio Playwright Chromium installation. Browser automation runs outside the denied consumer environment. This is actual production asset/composition proof; full backend/authentication/custom-module/optional-addon journeys remain Program3340's later consumer leaves.
