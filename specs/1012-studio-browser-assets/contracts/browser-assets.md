# Browser asset delivery contract

The canonical [hosting contract](../../../docs/reports/studio-hosting-contract.md) and [asset-delivery decision](../../../docs/reports/studio-browser-asset-delivery.md) govern; this work unit implements their actual package payload.

- Hosting ships standard index/app/CSS/images/fonts, `/studio/sdk/index.js` and all import-map vendors under its existing root static-web-asset base. Runtime configuration stays generated code at `/studio-runtime.js`.
- Modules keep `/_content/<package-id>/studio/modules/...` resources, including emitted chunks/styles. Host and modules share import-map React, SDK and UI.
- Existing AddElsaStudio/ConfigureShells/UseElsaStudio/MapElsaStudio APIs remain the consumer seam. No new public C# API or Web/Nuplane dependency is introduced.
- Packed browser source/tool manifests are excluded from the consumer graph. Producer-only bundle checks must not become essential consumer build hooks or invoke Node during consume/restore/publish.
- Existing revalidation, MIME/ranges, real unknown API/asset404 and selected fallback behavior remain unchanged.
- Deliberate direct-reference ExcludeAssets="build;buildTransitive" removes Hosting's frontend only, preserving server APIs and independent module resources. A separately named fallback works with the bundle; same-path overlay and arbitrary application PathBase remain outside the supported contract.
- Actual production package-only restore/publish/run, complete asset accounting and real browser boot are acceptance evidence. Wrappers, source hosts, development static manifests or copied consumer assets cannot stand in for that proof.
