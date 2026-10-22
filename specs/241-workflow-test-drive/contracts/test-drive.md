# Test Drive contracts

POST `/publishing/workflows/drafts/test-run-options`: captured draft request → `{options}` with artifact-bound selectors and supported driver modes. Read-only: no publication/index/scope/execution.
POST `/publishing/workflows/drafts/test-runs`: existing request with optional `start:{kind,optionId,modeId,data}` and separate `inputs`. Immediate driver returns dispatched view; external driver returns Prepared + `driverAction` without dispatch.
GET `/publishing/workflow-test-runs/{id}`: current test view with actual resulting execution association.
POST `/publishing/workflow-test-runs/{id}/stop`: close scope, cancel normal runtime root, release module resources.
HTTP module action URL is in an isolated namespace. It requires normal test management permission plus authored endpoint policy and only resolves the exact prepared target. Request body/header/query parsing and response delivery share production semantics.
Public registration: backend `ITestRunTriggerDriver`, runtime `ITestRunTargetDispatcher`, and Studio `workflowTestStimulusEditors`. Exact-one matching driver; missing/ambiguous contributions refuse rather than choose registration order. Direct Test Run remains valid without a driver.
