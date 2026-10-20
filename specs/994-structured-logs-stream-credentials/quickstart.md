# Qualification Guide: Authenticated Structured Logs Streaming

This guide describes the validation to run after implementation. The planning pass itself does not run tests, build, or start a host.

## Regression test checkpoint

1. In `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts`, first add a rendered-page regression that records the EventSource URL and init options and asserts `withCredentials === true`. Confirm it fails against the current one-argument constructor. Send this RED checkpoint for root review before making the product change.
2. Then implement the option at the existing `new EventSource` call in `src/module.tsx` and make the test pass. Exercise same-origin and separate-origin backend URLs while asserting the existing filter query remains unchanged.
3. Extend the existing EventSource stub only as needed to cover filter replacement (previous instance closed; replacement has current filters and credentials), unmount cleanup, and error state without the client creating a duplicate source. Preserve the existing pause/buffer behavior with deterministic page events. Native retry semantics require the real-browser host check below; a stub cannot prove cookie attachment on browser retry.

## Portable client checks

From the repository root:

```sh
(
cd Studio
pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs test
pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs build
)
```

From the repository root, run the existing server authorization and stream tests as separate preservation evidence. No backend change is required:

```sh
dotnet test Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj --filter FullyQualifiedName~StructuredLogsApiAuthorizationTests
dotnet test Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj --filter FullyQualifiedName~StreamEndpointTests
```

## Required normal-host product proof

After integrating PR #3133’s final independently reviewed candidate (local stacking is allowed while its CI runs), use the committed [isolated composed-host procedure](../993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison) to build this checkout and prepare fresh private normal-host configuration, databases, key ring and package feeds. That guide defines exact host paths, loopback allocations, readiness checks and guarded cleanup; do not reuse its old evidence roots. Use a fresh disposable browser context and standard operator cookie session. Keep Studio and Workbench on their configured separate origins. The shared guide's baseline profile is not needed to prove this normal-host credential defect.

- Confirm Recent and Sources still return 200. Open the actual Structured Logs product page and confirm its product-created stream request returns 200 with `text/event-stream` and the page reaches Live. Repeat with no log entries and distinguish a live empty inventory from denial or connection error.
- Trigger a bounded transient stream interruption in the isolated browser and confirm the actual retry request remains authorized and returns to Live. Deterministic page-event regressions separately prove level/source/category filtering, pause/buffer behavior, filter replacement and unmount cleanup. Do not add a telemetry ingestion fixture or enable another provider merely to qualify the credential option.
- Preserve the existing unauthorized/insufficient-principal outcomes and zero-event disclosure using the existing backend authorization tests. The browser credential option must not change policy.
- Inspect request summaries only; do not copy cookies, tokens, or secret-bearing headers into logs or saved evidence. No bearer-only support is implied.

### Genuine zero-entry case

Normal startup captures logs, so an empty category-filter result alone does not prove an empty inventory. Prepare a new private normal runtime with the shared procedure. Before launching it, set only `CShells:Shells:default:Features:DiagnosticsStructuredLogs:MinimumLevel` to `"None"` in `$RUN/workbench/normal/content/shells.json`. Preserve the other feature settings and keep both `DiagnosticsStructuredLogs` and `DiagnosticsStructuredLogsEntityFrameworkCore` enabled. This supported capture threshold suppresses real log severities while leaving the source, store, endpoints, permission policy and SSE available. Do not change tracked configuration, global logging or the Studio manifest.

After readiness, use a read-only SQLite connection to the private `$RUN/workbench/normal/data/diagnostics.db` and require zero from:

```sql
SELECT COUNT(*) FROM elsa_structured_log_records;
```

In the actual product page, choose All levels and All sources and leave Category blank. Require empty Recent 200 JSON, Sources 200 with its local source, product SSE200 and Live with the empty message. Repeat the same-session interruption/recovery and the read-only record count; the count must still be zero. Navigating Home should close the page's browser stream request. Preserve only safe outcomes and screenshots, then discard the private runtime using the shared guarded cleanup. This empty-case configuration complements the ordinary capture-enabled proof; it does not replace it or establish ingestion while capture is disabled.

The prior `{ withCredentials: true }` browser probe is useful reproduction evidence but does not satisfy product proof. Integrate PR #3133 base-first, then run local combined qualification on that head. Before this issue’s PR/delivery, integrate the parent’s verified main result and repeat proof affected by any source difference. Its Diagnostics index/copy and Recent-failure presentation changes are outside this fix. Root reviews the RED checkpoint before product edits and the finished evidence before acceptance.

Run workspace typecheck/lint, the production workspace build and both host builds for the final product candidate. Follow the shared guide's existing CI dependency-graph restore, architecture suite and maps check; keep heavy checks serial. Tie final review and hosted CI to the exact head, then qualify resulting main. No production deployment or new backend runtime behavior is included.
