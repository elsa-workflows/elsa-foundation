# Two-host failure and remote-control qualification

Bounded operator tooling for Runtime Efficiency #2531 M5. It is not a benchmark, CI gate or production tool.

## Topology

Two task-owned D2s_v6 workers (owner A, recoverer B) and one PostgreSQL 16 container on a separate host, all on one
private subnet with no public addresses. `two-host-controller.py` and `cluster-controller.py` run as root on the
database host and drive the workers over a task-only SSH key. Every case uses its own fresh database. The database
password, transfer token and SSH key are task-only, are never printed or archived, and are deleted at cleanup.

## Retained-boundary cases (`two-host-controller.py`)

Uses the [retained-boundary Worker](../retained-boundaries/README.md) with `Provider: PostgreSql`:

- `crash`: A runs to a gate (before/after the boundary commit, after queue advance, after the third body, and the
  parent-decision variants), A's process group is killed with SIGKILL, then B recovers on the other host. Checks
  completion, preserved committed activity identities and no re-run of committed bodies.
- `takeover`: A pauses after a body or committed parent decision, B recovers with an expired lease and completes,
  then A is released. A must be rejected by the stale fence and leave every core row byte-for-byte unchanged.
- `reclaim`: as `takeover`, but B also deletes the terminal executions through the real deletion store, retires
  their source references and runs a real executable sweep before A is released. Nothing may be resurrected.
- `interrupt`: A pauses before its boundary commit, the PostgreSQL container restarts, A is released and B recovers.
  With the default retry policy the expected outcome is one blocking `SchedulerWorkPoisoned` incident for the
  connection termination, with no lost, duplicated, queued or undelivered work. `--retry=RetryNow` (or `RetryAfter`)
  registers a host policy that retries transient provider/socket failures; `RetryNow` must then complete the run.

## Cluster cancellation (`cluster-controller.py`)

Starts the real Workbench as two durable-membership nodes on shared PostgreSQL (runtime, design, publishing, placement,
command transport, database locking and Data Protection). A workflow is started through node A's authenticated
loopback API (`api.py`). Node B loads the [cluster-control hook](../cluster-control/README.md), which sends two
identical actor Cancel envelopes through the real durable transport. The controller then checks the database state,
placement and node A's own API view independently. `loop-ab.py` runs the progressing-loop shape on one node for a
branch/main comparison.

The evidence README and program report record the actual runs, binary hashes and failures.

## Conflict diagnostics hook

`conflict-hook-StartupHook.cs` is a diagnostics-only .NET startup hook used to identify the progressing-loop failure.
Build it as a plain `net10.0` class library with no package references (assembly name `ElsaEfficiencyConflictHook`),
then load it with `DOTNET_STARTUP_HOOKS` and `ELSA_EFFICIENCY_CONFLICT_LOG=<file>`. It appends, for each first-chance
`DbUpdateConcurrencyException`, the conflicting entity types and their original/current `Revision`. `loop-ab.py ... diag`
deploys it. It changes no runtime behavior and is not a production component.
