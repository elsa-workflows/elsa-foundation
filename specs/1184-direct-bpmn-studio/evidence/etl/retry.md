# Retained service retry in Studio

T025 source `7a8cf8767ed00efe944998007882c87a217a25cf` adds exact native-attempt links and the held incident/retry UI. The native links name the recorded worker execution and explicitly retain the selected backend; each attempt exposes its immutable executable pin. Original failures and replacement attempt identities remain inspectable.

Retry uses the existing server operation and scoped browser command receipt. The new command-kind storage key leaves old start/completion keys unchanged. An unconfirmed request survives reconnect without automatic transmission; Resume reuses the exact incident, invocation, failed attempt and request identity. Check outcome is read-only, including when the first response was lost. Recovery remains available after the server reports the incident Retried or the model terminal. New retry remains disabled for pending receipts, exhausted budgets, obsolete work/attempt identities, unknown profiles or missing backend capability. The server rechecks authorization and the current incident.

The positive rendered control failed without the implementation because no retry action existed. With the change, **68/68 focused tests pass**, including25 new page controls,22 existing page controls and21 runtime-client controls; no failures or skips. Workflows typecheck and targeted lint pass. The initial typecheck found two fixture errors (nullable capabilities and mutation of a readonly property); both were corrected before the successful run. See [machine receipt](retry-qualification.json) for individual log digests and outcomes.

Commands from `Studio/src/essentials/Elsa.Studio.Workflows/Client`:

```sh
node ../../../../node_modules/vitest/vitest.mjs run src/__tests__/bpmnWorkflowsPage.test.tsx src/__tests__/bpmnRuntimeApi.test.ts
node ../../../../node_modules/typescript/bin/tsc --noEmit -p tsconfig.json
node ../../../../node_modules/eslint/bin/eslint.js src/bpmn/BpmnRunDetails.tsx src/bpmn/BpmnCommandNotice.tsx src/bpmn/useBpmnCommand.ts src/__tests__/bpmnWorkflowsPage.test.tsx
```

This is component/client proof with mocked HTTP. T021/T026 still require the actual published Sequence+RunEtl worker, physical HTTP/file effects and failure, restart/fresh browser, restored dependency and explicit same-request UI retry. Wider ETL acceptance, claims/interruption, full final qualification and independent review remain open. No persisted runtime writer changed. This increment stays local for the next batched journey push, avoiding an extra hosted CI run.
