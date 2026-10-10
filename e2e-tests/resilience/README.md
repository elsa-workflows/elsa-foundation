# resilience - poisoned work & delivery recovery (Tier 2)

| Script | What it exercises |
|--------|-------------------|
| `Test-PoisonedWork.ps1` | A scheduler work item that throws during dispatch (a `WriteLine.text` bound to a **throwing JavaScript** expression) is **poisoned** and surfaces a `Critical`/`Blocking` incident (`failureType: SchedulerWorkPoisoned`) observable via `GET .../instances/{id}`; the workflow preserves its nonterminal lifecycle under `WaitForIntervention`. With the default `NoopRuntimeDomainRetryPolicy` (DoNotRetry) it is poisoned on the first failure (`runtime.poison.failureCount=1`, `retryMode=DoNotRetry`). |
| `Test-IncidentTroubleshooting.ps1` | Undefined JavaScript input persists exact activity/node/input association and payload-free failed evaluation evidence; two failing runs and a healthy control prove current health counts and filtering before cursor paging. |

## Not covered here, and why

- **Retry COUNT before poisoning** is not e2e-reachable: the reference server composes the default zero-retry policy,
  so there is no "N retries then incident" to assert without a host-supplied `IRuntimeDomainRetryPolicy`.
- **Dispatch redrive** (`POST runtime/workflows/dispatches/{id}/redrive`) — the full loop (a detached child dispatch
  truly dead-letters → operator redrives → the child materializes) is **not reachable over REST with the default
  server**: producing a real `DispatchFailed` needs delivery-failure injection, and a bogus child id fails at parent
  *publish* (pinned then), not at runtime. The redrive endpoint's bogus-id no-op is covered in-process by
  `WorkflowDispatchRedriveContractTests`. This is a harness limitation, not missing behavior — see the gap
  analysis; the redrive path is heavily covered by the in-process C# tests (`WorkflowDispatchRedriveTests`, etc.).

Requires the server from source (see ../README.md). `Test-IncidentTroubleshooting.ps1` requires an exclusively owned fresh database and host; it scopes its three runs by their creation time.
