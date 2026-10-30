# Studio browser asset delivery probe

Manual archive/consumer qualification for [#3352](https://github.com/elsa-workflows/elsa-foundation/issues/3352).
See the [decision, executed scope and customization limits](../../../docs/reports/studio-browser-asset-delivery.md).
Requires a .NET10 SDK, Python3, the committed Studio frontend inputs and the Studio Playwright Chromium install.
Node/pnpm build the producer assets and Node drives browser automation. Consumer restore, publish and the
published server use a restricted PATH with failing node/npm/pnpm/npx shims.

From the repository root, build the actual shell, its dependencies and Dashboard:

```sh
(cd Studio && pnpm install --frozen-lockfile && pnpm --filter "@elsa-workflows/studio-web..." --filter "@elsa-workflows/studio-dashboard..." build)
python3 tools/studio/AssetDelivery.Probe/qualify.py
python3 tools/studio/AssetDelivery.Probe/qualify.py --omit-published-vendor
python3 tools/studio/AssetDelivery.Probe/qualify.py
```

Expect exits **0, 1, 0**. The control must fail at
`HTTP asset mismatch: /studio/vendor/react.js (status 404)`. The normal profile also performs an internal
missing-file/restore control. Artifacts are retained in a fresh temporary directory printed at startup;
`--output /absolute/new/directory` selects a new explicit location. Nothing is published remotely.

The generated Razor libraries pack exact copies of actual built shell/module assets. The consumer has only
two PackageReferences, an isolated verified NuGet cache, and no project/source reference. Its publish is
checked byte-for-byte. Producers, consumer source, feed and cache are renamed out of their build locations
before the server starts. Every packed resource is fetched and checked for bytes, MIME and revalidation;
index/SPA fallback, missing API/assets, Range, duplicate-path rejection and a separately named consumer
fallback document are checked too. Chromium executes the actual import map/SDK/React and registers Dashboard.
An advanced consumer excludes Hosting's `build;buildTransitive` assets: its own frontend publishes without
collision, the same packaged .NET fixture API executes, bundled vendors are absent, and module assets remain.
Both published hosts execute after their source/cache locations are hidden and use frontend denial shims.

The probe owns and stops only its server process. The artifacts retain logs, archives, copied inputs,
consumer lock/restore graph, HTTP digests, browser results and source/probe hashes. The denial shims and
this execution profile use POSIX paths. This is a bounded macOS/.NET10 proof, not the full Studio application,
backend/authentication journey, final production host package or a supported-platform matrix.
