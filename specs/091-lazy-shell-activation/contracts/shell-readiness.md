# Contract: Shell Readiness and Cold-Start Evidence

## Root health endpoints

Both endpoints are excluded from shell routing and require no authentication.

### `GET /health/live`

Always returns HTTP 200 while the root ASP.NET Core process can dispatch requests.

```json
{
  "status": "live"
}
```

### `GET /health/ready`

Returns HTTP 200 only when the configured default shell has an Active generation. Active implies its initializers, startup tasks, workflow HTTP route refresh, and endpoint registration completed.

```json
{
  "status": "ready",
  "shell": "default",
  "generation": 1,
  "durationMs": 1234.5
}
```

Returns HTTP 503 immediately otherwise:

```json
{
  "status": "starting",
  "shell": "default",
  "code": "shell_activation_pending"
}
```

Allowed unavailable statuses/codes are bounded: `not_started`, `starting`, `disabled`, `failed`; codes describe the category and never contain exception text.

## Host configuration

```json
{
  "Elsa": {
    "Readiness": {
      "WarmDefaultShell": true,
      "DefaultShellName": "default"
    }
  }
}
```

- `WarmDefaultShell=true`: begin one activation after the application starts listening.
- `WarmDefaultShell=false`: retain lazy activation; readiness only observes an independently activated shell.
- `DefaultShellName`: non-empty configured shell name; default `default`.

## SQLite materialization repair knob

The `GroundworkRuntimePersistenceSqlite` shell feature exposes:

```json
{
  "RematerializeOnStartup": false
}
```

- `false` (default): an exact committed schema-history tuple opens directly; absent/changed history performs full materialization.
- `true`: always execute the existing full materialization/backfill path for repair or verification.

## Telemetry

Activities and metrics use the following stable source/meter, activity, and histogram names:

| Concern | Source / meter | Activity | Histogram |
|---|---|---|---|
| Default-shell preparation | `Elsa.Server.Readiness` | `elsa.shell.activation` | `elsa.shell.activation.duration` |
| Startup tasks | `Elsa.Tasks.Startup` | `elsa.startup_task` | `elsa.startup_task.duration` |
| Groundwork SQLite initialization | `Elsa.Persistence.Groundwork.Sqlite` | `elsa.groundwork.initialize` | `elsa.groundwork.initialization.duration` |
| HTTP route-table refresh | `Elsa.Workflows.Runtime.Http` | `elsa.http.route_table.refresh` | `elsa.http.route_table.refresh.duration` |

Required bounded tags:

- `elsa.activation.phase`
- `elsa.activation.outcome`
- `elsa.task.type` only for the registered startup task type
- `elsa.route.count` when route initialization completes
- `elsa.groundwork.initialization` = `history_hit` or `materialized`

Default-shell phase values are `overall`, `feature_discovery`, and `shell_activation`. Outcome values are bounded to `success`, `failed`, `cancelled`, and—where the operation supports it—`skipped`.
The warmup reports every top-level phase it reaches. If an earlier phase fails, later phases are absent rather than receiving invented zero-duration values. Provider initialization, startup-task execution, and route refresh retain their dedicated sources above and are parented by the active shell-activation activity when listeners are enabled.

No tenant, workflow, artifact, connection-string, exception-message, or arbitrary shell-name value is emitted as a metric dimension.

## Cold-start command

The command accepts a prebuilt server DLL, content/data baseline, loopback URL, readiness/workflow paths, expected response, boot count, report paths, and optional p95 budgets. Exit status is non-zero when launch, readiness, response validation, shutdown, or an enabled budget fails.
