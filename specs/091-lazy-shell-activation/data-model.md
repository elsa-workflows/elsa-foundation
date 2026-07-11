# Data Model: Observable Shell Readiness and Cold Activation

No new durable business entity is introduced. The feature uses bounded process state and report records.

## ShellReadinessSnapshot

Represents one immutable observation of default-shell preparation.

| Field | Meaning | Rules |
|---|---|---|
| `Status` | `NotStarted`, `Starting`, `Ready`, `Failed`, or `Disabled` | Monotonic within one warmup attempt except that active-shell observation can establish Ready after disabled/external activation. |
| `ShellName` | Configured default shell | Non-empty; never a metric dimension when arbitrarily configured. |
| `Attempt` | Process-local attempt number | One for reference warmup; bounded integer. |
| `StartedAt` | UTC preparation start | Null before starting/when disabled. |
| `CompletedAt` | UTC terminal time | Present for Ready/Failed. |
| `Duration` | Monotonic elapsed preparation time | Present after terminal outcome. |
| `Code` | Stable bounded diagnostic code | No exception messages, paths, connection strings, or payload content. |
| `Generation` | Active shell generation when ready | Optional until active; positive when present. |

State transitions:

```text
NotStarted ──start──> Starting ──success──> Ready
                             └──failure──> Failed
NotStarted ──disabled──> Disabled
Disabled/Failed/NotStarted ──external active generation observed──> Ready (response only)
```

## ActivationPhaseObservation

One telemetry observation correlated to a warmup/activation activity.

| Field | Meaning | Cardinality rule |
|---|---|---|
| `Phase` | `feature_discovery`, `shell_activation`, `provider_initialization`, `startup_task`, or `route_table` | Fixed vocabulary. |
| `TaskType` | Registered startup task type | Only on startup-task observations; bounded by application registrations. |
| `Outcome` | `success`, `failed`, `cancelled`, `skipped`, `history_hit`, or `materialized` | Fixed vocabulary. |
| `DurationMs` | Monotonic elapsed milliseconds | Non-negative. |
| `RouteCount` | Number of initialized routes | Optional, non-negative. |

## ColdBootSample

| Field | Meaning |
|---|---|
| `Index` | One-based boot index. |
| `ListeningMs` | Process launch to socket accept. |
| `ActivationMs` | Listening to readiness success. |
| `ShellReadyMs` | Process launch to readiness success. |
| `FirstWorkflowRequestMs` | Duration of the first validated workflow request after readiness. |
| `FirstSuccessMs` | Process launch to validated workflow response. |
| `Status` | `passed` or stable failure category. |
| `LogPath` | Retained only on failure or explicit retain mode. |

## ColdStartReport

Contains immutable build/configuration/data provenance, all `ColdBootSample` rows, and nearest-rank p50/p95 aggregates. Reports from different lanes are comparable only when their provenance declares the same runtime, machine, configuration hashes, and frozen database hashes.
