# ETL Studio component checkpoint

Date: 2026-10-20. Program #2892, leaf #3109, draft PR #3111. The component evidence below is supplemented by the successful [rebuilt-host browser acceptance](studio-browser.md), completing T018/T029/T030/T032. T031 has separate [executed Workbench HTTP evidence](workbench-http.md). Final delivery gates remain open.

## Implemented surface

The ETL-owned module under `Extensions/Etl/studio/Elsa.Studio.Etl` registers definition and run routes through the public Studio feature-area API. The `RunEtl.Pipeline` editor uses the existing public property-editor slot and the `etl-published-selection` hint. General Workflows Studio owns no ETL-specific logic.

The structured editor covers the finite supported topology, source/lookup schema, host binding aliases, mapping, typed filters and limits. Definition, binding, version, run and partition pages use bounded explicit loading. Saving preserves the draft revision; validation supplies the exact revision/version/hash tuple used for publication. Published versions are read-only. Reload requires confirmation before discarding edits and refreshes the selected summary. A saved alias outside the loaded page is preserved and described as not loaded, without asserting that the host rejected it.

The run view displays retained input identity, terminal outcome, partition progress and partial-output limits. Cancellation returns and displays the actual terminal outcome, including a completion that won the race. It never promises physical worker termination or reversal of external effects. Stale-context responses and a cancellation confirmation resolved after unmount cannot update or submit against a departed view.

The capability-aware API client uses the authenticated Studio transport. Capability documents are scoped by HTTP client and base URL, with explicit refresh after missing capability. `lossless-json` 4.3.2 retains full Int64 and decimal filter literals and large Int64 limits as JSON numbers through editing and saving; it does not substitute numeric strings or narrow the backend contract to JavaScript-safe values.

## Executed frontend checks

Run from the ETL Client directory unless specified:

- `pnpm test`: **40 passed, zero failed, seven files** after the paging/reload fixes. An earlier run had 38 passed/one failed because the old selector assertion still expected the misleading unavailable label; that assertion was corrected to the intended not-loaded behaviour and the full run passed.
- `pnpm build`: production ES module and stylesheet emitted successfully. The final bundle was 105.55 kB JavaScript (22.51 kB gzip) and 5.86 kB CSS (1.51 kB gzip). These sizes are observations, not performance qualification.
- From repository root, `pnpm --dir Studio exec tsc --noEmit -p ../Extensions/Etl/studio/Elsa.Studio.Etl/Client/tsconfig.json`: passed.
- ESLint with the repository Studio configuration: zero errors. Hook dependency warnings remain visible; no lint rule was disabled. The two unused test imports were removed; the final frontend run includes that cleanup.
- Targeted stylelint and Studio layout check passed; layout found 21 projects, 22 packages and 21 module roots.
- Frozen filtered dependency installation passed; only the ETL importer and lossless-json addition were retained in the lockfile.

The seven test files cover API numeric fidelity/capability isolation, exact published selection and explicit paging, structured editor state, authoring save/validate/publish/reload, runs/cancellation lifecycle, retained inspection and public module registration. These are Vitest DOM/component tests, not an actual server or browser journey.

## Browser qualification

The [actual Chromium journey](studio-browser.md) now passes author/save/diagnose/correct/validate/publish, exact ETL version selection, native publication review, persisted execution, ETL inspection and following Sequence activity completion. The sanitized receipt and three visually inspected screenshots are retained there. Keyboard table scrolling and both 390px ETL content viewport measurements passed without hiding overflow.

Earlier failures found page margins/long-token overflow and a native publication-review tenant omission. The scoped HTTP regression fails without propagation; byte-exact restoration passed all 812 Publishing API tests. The rebuilt browser pass verifies the production integration, with no cross-tenant lookup fallback. The source-reviewed journey uses ordinary publication review and the Executables page, not transient Test Run.

After the final responsive changes, all seven frontend test files passed **40 tests** and the production build passed (105.88 kB module JavaScript, 5.90 kB CSS; observations only).

## Backend integration and remaining gates

The focused actual native catalog/published-parent and Design API HTTP run passed **15 tests, zero failures/skips**. Removing the new-publication approval check made its HTTP regression fail with expected `BadRequest` versus actual `OK`. The original source was restored byte-for-byte (SHA-256 verified); the full restored Release ETL suite passed **349 tests, zero failures/skips**. New publication must recheck scoped host approval for each role; immutable version replay must remain independent of current approval. Build verification caught and corrected an API-baseline namespace typo and a catalog-reader namespace import. These failed attempts are not passing evidence.

Workbench restored and built successfully with the opt-in ETL assembly references (zero errors). The first fresh-storage HTTP journey exposed a native start tenant mismatch. That failure, its narrowly scoped correction, the 41 focused tests and mutation/restoration, and the subsequent successful rebuilt Sequence/Flowchart HTTP journeys are recorded in [Workbench HTTP evidence](workbench-http.md). The full Studio frontend workspace also built successfully. The actual Studio .NET host build and `WebHostShellFeatureDiscoveryTests` passed (3 tests, zero failures/skips), including the ETL assembly/static assets. The subsequent full ETL suite passed 367 tests and indexed architecture passed 583, both with zero failures/skips. Browser keyboard/narrow-screen verification passed as linked above. Final integrated review and fresh-head CI remain required. Do not use this component checkpoint to mark the leaf, connector parent or program complete. Physical effects remain at least once; historical timeout causes remain unproved. The future task runner and pause semantics are outside this slice.
