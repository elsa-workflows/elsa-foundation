# runtime-alterations — durable plan e2e coverage

These are true backend e2e scripts for the durable runtime-alteration API. They exercise the public REST API,
Groundwork persistence, the hosted orchestration pump, and the runtime checkpoint path against a source-built
`Elsa.Server`; no script reaches into server DI or invokes a pump directly.

Run after rebuilding the server with a fresh development database as described in [`../README.md`](../README.md):

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
captured first page, and terminal acknowledgement evidence. The restart script owns the server process by default
and therefore must be run alone on its configured port.
