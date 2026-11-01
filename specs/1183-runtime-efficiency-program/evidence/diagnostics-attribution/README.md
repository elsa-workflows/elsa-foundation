# Non-timing diagnostics attribution

A one-off Runtime Efficiency #2531 observer for an owned Workbench process. Compile `Hook.csproj`, then pass its
absolute DLL path in that child's `DOTNET_STARTUP_HOOKS`. Set `ELSA_SQL_CAPTURE_OUTPUT` to a fresh absolute JSONL
path and `ELSA_SQL_CAPTURE_CONTROL` to an absolute phase-control file. Use a protected task-owned directory and
umask 077. Never install this globally, inject an existing service or use an instrumented run as timing evidence.

The hook observes successful/failed physical EF commands, transaction starts/commits and completed savepoint events.
It records context/connection *types*, SQL text, duration, UTC time, Activity trace ID and a bounded phase label. It
never reads parameter values or connection strings, and does not enable EF SQL logging or alter existing diagnostic
exporters. The host retains its actual diagnostics On/Off configuration. Consequently SQL logging cannot feed extra
entries into the structured-log store merely to count its work. This is EF-event attribution, not measured wire
round trips; background writes without a request trace must be identified separately from the marked request.

Before publication/setup, leave the control file absent. After warming and draining prior work, atomically publish
an ASCII phase label matching `[a-z0-9-]{1,32}` (write a temporary file then rename). Capture a quiet idle interval and
one uniquely traced request plus its asynchronous diagnostic writes in separate phases. Remove the control file
before final validation. Stop only the owned host normally so its process-exit receipt is written. Require every
JSONL record to parse, receipt row/byte counts to agree, `complete=true`, zero errors/drops and no truncated SQL.
A missing receipt or capture limit invalidates attribution. Limits are 100,000 records, 32 MiB and 65,536 SQL characters
per record. Keep failures and the uninstrumented performance series separately.

The actual local Workbench/direct-BPMN smoke captures 7,783 events with named Identity IAM and Runtime contexts,
complete receipt and known parameter values absent. It still passes direct-model terminal/replay/physical-row
checks. This validates observation and parameter exclusion; it does not yet attribute the persisted diagnostics
cost, and all instrumented/shared-machine timing is excluded. The DLL is a manual qualification artifact, not a
product module, benchmark service or CI gate.
