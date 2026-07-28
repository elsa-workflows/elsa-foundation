# resilience - poisoned work & delivery recovery (Tier 2)

| Script | What it exercises |
|--------|-------------------|
| `Test-PoisonedWork.ps1` | A scheduler work item that throws during dispatch (a `WriteLine.text` bound to a **throwing JavaScript** expression) is **poisoned** and surfaces a `Critical`/`Blocking` incident (`failureType: SchedulerWorkPoisoned`) observable via `GET .../instances/{id}`; the workflow Faults. With the default `NoopRuntimeDomainRetryPolicy` (DoNotRetry) it is poisoned on the first failure (`runtime.poison.failureCount=1`, `retryMode=DoNotRetry`). |

## Not covered here, and why

- **Retry COUNT before poisoning** is not e2e-reachable: the reference server composes the default zero-retry policy,
  so there is no "N retries then incident" to assert without a host-supplied `IRuntimeDomainRetryPolicy`.
- **Dispatch redrive** (`POST runtime/workflows/dispatches/{id}/redrive`) — the full loop (a detached child dispatch
  truly dead-letters → operator redrives → the child materializes) is **not reachable over REST with the default
  server**: producing a real `DispatchFailed` needs delivery-failure injection, and a bogus child id fails at parent
  *publish* (pinned then), not at runtime. The redrive endpoint's bogus-id no-op is already covered by
  `write-endpoints/Test-RuntimeWrites.ps1`. This is a harness limitation, not missing behavior — see the gap
  analysis; the redrive path is heavily covered by the in-process C# tests (`WorkflowDispatchRedriveTests`, etc.).

Requires the server from source (see ../README.md).
