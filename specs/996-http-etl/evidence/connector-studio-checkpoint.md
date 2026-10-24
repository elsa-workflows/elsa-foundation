# Connector Studio authoring checkpoint

This is the T011 Studio portion of [HTTP ETL](../spec.md), completing the scoped v2 API work
recorded in the [binding/authoring checkpoint](http-bindings-authoring-checkpoint.md).
The complete #3151 leaf and draft #3156 remain open. T018 operator inspection and T019 real
Workbench REST/browser acceptance are separate requirements.

## Implemented behavior

- `/etl/connectors` authors v2 HTTP JSON or CSV sources, JSON Pointer fields, all three pagination
  strategies, optional CSV lookup, typed mapping/filtering, and independent HTTP/JSONL routes.
  Host-approved aliases are filtered by both role and connector kind. Authored documents do not
  contain endpoint URLs or credentials.
- File and connector authoring use the same revision-aware workspace. Profiles bind the correct
  capability relations, document types, default document and editor. Save, validation, publication
  tuple checks, conflict handling, pagination and read-only immutable inspection remain shared.
- Connector limits and HTTP policy are editable without rounding Int64 values. Omitted retained-output
  and retained-metadata defaults remain omitted until changed; unrelated edits do not change their
  wire representation.
- The native activity property editor lists both catalogs. It recognizes a saved connector selection
  only when the v2 artifact matches the complete id/version/hash/token tuple. Both formats retain the
  existing `etl1:` selection envelope. Missing capabilities preserve saved values. Deliberate catalog
  choices apply to their saved token; changing the token re-enables detection. Late responses cannot
  override newer scope, catalog or definition choices.

## Review and tests

Three root review/fix passes, with bounded independent contract/editor review, ended without an
outstanding in-scope finding. The first pass repaired test mock typings, constrained generic numeric
budget fields and pagination callbacks, and stale automatic catalog selection. The second corrected
an overbroad manual catalog override: a later parent-supplied selection must be detected anew. The
third reviewed those fixes and final validation. Existing file authoring remains in the same test suite.

The negative control temporarily removed connector-kind filtering from `PipelineBindingSelect`.
The focused test failed because a CSV source appeared among HTTP choices (1 failed, 5 deliberately
unselected tests). The source was restored byte-for-byte (SHA256
`ebdad5ea17757024d0c0382c951c9f44eaabd25cb2ec984a263c7ad5bafacf5c`), and the complete client suite
passed afterward. This proves the binding-kind assertion detects the removed behavior; it is not a
server authorization proof.

## Validation

Commands are relative to the repository root unless a working directory is named.

| Gate | Command / result |
| --- | --- |
| Client typecheck | From `Studio`: `pnpm exec tsc --noEmit -p ../Extensions/Etl/studio/Elsa.Studio.Etl/Client/tsconfig.json`; passed |
| Client suite | From ETL `Client`: `pnpm exec vitest run`; 61 passed across 8 files, 0 failed/skipped |
| Production module | From ETL `Client`: `pnpm run build`; passed, 39 modules, 143.20 kB ESM before gzip |
| Targeted lint | Root `Studio/node_modules/.bin/eslint --config Studio/eslint.config.js 'Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/**/*.{ts,tsx}' Studio/tests/browser/etl-connector-authoring.spec.ts Studio/tests/browser/etlConnectorsFixture.tsx`; passed with existing hook dependency/ref warnings, no errors |
| Browser fixture | From `Studio`: `pnpm exec playwright test tests/browser/etl-connector-authoring.spec.ts --workers=1`; 4 passed at 320/768/1024/1440 px |
| Architecture | `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore`; 584 passed, 0 failed/skipped |
| Maps and solution filters | `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` and `-- solution-filters-check`; passed |
| Markdown links and diff | Relative links resolved; `git diff --check` and staged diff check passed |

The browser fixture mounts the real authoring, selection and dialog components with the Studio theme
and module CSS, against a deterministic intercepted API. It creates a connector draft, changes mapping
and the error route to JSONL, saves, validates, publishes the exact tuple and selects that version for
a workflow. It checks keyboard submission/focus, no document overflow, no page exceptions and zero
axe WCAG A/AA findings. Desktop/mobile screenshots were inspected. This is product-component evidence,
**not** a real Workbench or workflow-runtime journey. The fixture says so explicitly; T019 is still open.

Local logs are `/tmp/3151-studio-connector-{typecheck,vitest,build,lint,browser,control,architecture,maps,filters}.log`;
these paths are local evidence conveniences, not required repository inputs. Tests and this report are
committed so the checks are reproducible. Browser Vite is owned and stopped by Playwright.

No backend implementation changed in this checkpoint. The previous T010 backend results are historical,
not rerun claims. Fresh pushed-head CI remains required before the complete leaf may merge. The open
main-CI tracker #2293 was checked and was not closed or treated as resolved by these local frontend gates.

T011 is complete. **10/22 tasks** are complete in the full HTTP ETL leaf. T012 fenced attachment/acceptance, T013 process
recovery, T014 corruption/attempt replacement, T015 cancellation, T016 durable retry acceptance, T017 policy/telemetry,
T018/T019 operator/product proof, T003 frozen-worker proof and T020–T022 final delivery remain open.
