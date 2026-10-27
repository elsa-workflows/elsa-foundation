# Actual broadcast recovery cut

`Test-BpmnBroadcastRecovery.ps1` creates its own fresh SQLite content root and loopback port. It wraps the actual selected source store and resume dispatcher solely to identify and pause at a completed provider/dispatcher call. The committed source/cohort and actor checkpoints remain the authority. Only processes created by the runner are terminated.

Build two separate Workbench outputs outside the checkout. The fault feature is absent from the standard build; the runner verifies assembly metadata and binds the source files, host/reader dependency manifests and their hashes before/after execution.

```powershell
$probe = '/absolute/private/probe'
$normal = '/absolute/private/normal'
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release --artifacts-path $probe -p:ElsaBpmnRecoveryProbe=true -p:RestoreLockedMode=true
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release --artifacts-path $normal -p:ElsaBpmnRecoveryProbe=false -p:RestoreLockedMode=true
dotnet build tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj -p:RestoreLockedMode=true
./e2e-tests/bpmn/Test-BpmnBroadcastRecovery.ps1 `
  -ProbeAssembly "$probe/bin/Elsa.Workbench/release/Elsa.Workbench.dll" `
  -NormalAssembly "$normal/bin/Elsa.Workbench/release/Elsa.Workbench.dll" `
  -ReaderAssembly tools/bpmn/DeliveryRecoveryRead/bin/Debug/net10.0/DeliveryRecoveryRead.dll `
  -EvidencePath /absolute/private/recovery.json `
  -SourceRevision "$(git rev-parse HEAD)+explicit-candidate-worktree"
```

Use `-Phase after-resume` for partial callback/rearm recovery. Both signal-catch branches self-loop. The source router pauses after accepting its first saved resume, but the runner waits for the actual child to complete and the same authored listener to persist a distinct new wait. Before killing, checked state must show that completed callback, its new physical generation, the other original recipient still suspended, and the original keyed start already parked. Standard-host retry must leave the first rearm and completed attempt facts unchanged, execute exactly one resume attempt for the remaining original recipient, and leave its later wait too. The already parked keyed start and later listeners/binding must remain unchanged. This proves callback effects rather than treating a dispatch acknowledgment as completion.

Use `-Phase empty-cohort` to preserve a durably empty recipient decision. Initial waits/binding use a different event, so the actual first selection commits a nonnull 0-start/0-resume cohort. Only afterward does the runner create a matching listener and binding. The checked observer reads the actual source stimulus identity and active matching binding index; the later physical waits and binding must match and be newer than the cut. Standard recovery must settle the same empty cohort, leave both listener projections unchanged and create no start execution. Use `--expected-resumes 0 --expected-starts 0` for its observer controls. Capturing the distinct successor claim remains mandatory even when empty delivery finishes quickly.

Use `-Phase before-selection` for delayed first delivery. The first host is held after its canonical committed-source read and before matching. Only then does the runner create another matching two-catch listener and start binding. The stopped physical source must declare the protocol and have no cohort; both starts must still be absent. A standard host reclaims it and must select both start bindings and all four exact earlier waits/generations. Source record and recipient/binding timestamps supplement that causal ordering. The new start workflows then suspend on the same signal: their matching waits must stay outside the saved cohort and remain armed, proving pre-start wait exclusion. This mode expects the later listener/binding to join because selection has not occurred yet.

The default post-cohort case publishes one signal-start binding and arms two parallel catches before publishing the signal. After the actual cohort commit, it creates a different start binding and another two-catch listener, then kills the owned probe process. It reads a coherent snapshot with the actual checked provider, starts a read-only successor observer, and restarts the standard host on the same database. It waits for the real persisted lease deadline; it does not edit clocks, claims, recipients or checkpoint data.

The observer pins a read transaction when it sees a new owner/fence, backs up that exact SQLite view, and checks it through the provider. Missing that claim is a failed proof, even if the source subsequently completes. Final checks require the immutable cohort, both saved child activities completed, retired selected waits, the exact deterministic start parked on its next signal, and identical later bookmark IDs/generations/activity facts. A `Dispatched` result alone is not callback completion.

`DeliveryRecoveryRead` accepts only private closed snapshots without sidecars, checks their hash before/after, and uses SQLite read-only/immutable/query-only settings. Whole databases may contain identity configuration: keep those roots private. Only the minimal checked observations and manifests belong in portable evidence.

To run observer refusal controls against an explicitly owned completed fixture:

```sh
python3 e2e-tests/bpmn/test_inspect_broadcast_recovery.py \
  --database /absolute/private/content-root/elsa.db \
  --marker /absolute/private/content-root/recovery-control/CASE/reached.json \
  --reader tools/bpmn/DeliveryRecoveryRead/bin/Debug/net10.0/DeliveryRecoveryRead.dll -v
```

Those controls qualify observer mechanics only. For the before-selection fixture, pass `--expected-resumes 4 --expected-starts 2` to the observer controls.

These bounded crash cuts do not establish every T051 case, legacy upgraded-host recovery, Studio compatibility or production fleet acceptance.
