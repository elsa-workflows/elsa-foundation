# runtime-alterations — durable plan e2e coverage

These are true backend e2e scripts for the durable runtime-alteration API. They exercise the public REST API,
EF Core persistence, the hosted orchestration pump, and the runtime checkpoint path against a source-built
`Elsa.Workbench`; no script reaches into server DI or invokes a pump directly.

Run `Test-AlterationPlans.ps1` after rebuilding the server with a fresh development database as described in
[`../README.md`](../README.md). `Test-AlterationReplayAndRestart.ps1` needs only the build: it starts its own server.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./e2e-tests/runtime-alterations/Test-AlterationPlans.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1
```

`Test-AlterationPlans.ps1` proves accepted plan shape, bulk `CancelWorkflow`, successful root-variable replacement,
Sequence-owned `ScheduleActivity` and its visible child completion, `RescheduleActivity` and its visible supersession
lineage, plus stable job paging, cancellation, and redacted plan/job reads. Its Migrate case is intentionally a
retained-identity smoke path: the public authoring API does not yet expose clone-to-new-version creation, so it proves
payload decoding, liveness, quiescence, compatibility, and checkpoint staging but does not claim a cross-artifact
repin. Root-variable-frame values are not projected publicly, so the replacement secret is verified through a
successful protected alteration outcome and redaction assertions rather than being echoed through workflow output.
`Test-AlterationReplayAndRestart.ps1` proves idempotent replay, restart survival, continuation from a durably
captured first page, and terminal acknowledgement evidence. The restart script owns its server: it launches the
built Workbench from a temporary content root on a free loopback port (or on the port of `-BaseUrl`, which must
be free), and restarts and stops only that process. It never stops a server you started; if the port is held by
another process it fails with the port and the pid in the message (issue #2329). For the weaker replay-only run
against a server you started, pass `-UseExternalServer -RestartServer:$false -BaseUrl <url>`.
