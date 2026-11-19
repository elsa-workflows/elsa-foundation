# Resulting-main BPMN adoption qualification

Exact adoption main: `438001406d980051584234e5d656eb8e6a67f47f`, merged in
[PR #3338](https://github.com/elsa-workflows/elsa-foundation/pull/3338).
The complete tree `18d161d1086d2e2d964568bd9015a256d598e483` equals reviewed head
`d44b0a796e3de4abd4da4fa04084e6dceb85076b` and CI's merge checkout8426029b5.
[Round5](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6459660974)
was clean across correctness, standards and acceptance. The
[self-merge audit](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6460021281)
records required exact-head gates, the existing #3261 quarantine, declared skips and unverified
GitGuardian size skip. The merge used the Codex App and normal protection.

## Actual executable upgrade

Fresh locked Release standard Workbench and checked-reader builds use exact438001406. Actual
ProductVersion metadata for Workbench, Runtime.Core, DispatchWorkflow runtime, BPMN and interchange
contains that revision. Forty-eight retired public types remain absent; the released0.5.0 Model,
Semantics and Interchange DLL bytes equal the preceding qualified build.

Six actual running/terminal escalation and signal-plus-authored-failure cases, each interrupting and
noninterrupting, first reach their held states on executing409ee7ba3. Only the manifest-verified owned
backend is stopped. Exact main438 starts over the same Runtime/Design/config/identity stores while
Studio remains live. The whole six checked parent/child/control/physical/source observations equal
their pre-upgrade values. Normal public continuation finishes all six with truthful child fault and
handled-parent outcomes. Both matched and unmatched coded-error cases also pass on main.

Four whole closed module backups are portable: Runtime before upgrade, recovered held Runtime,
final Runtime and final Design. All pass integrity checks. The two final case sets are checked
against the same final Runtime snapshot. Four sets of six positive/invalid-copy controls give24;
invalid copies never enter a positive host. Asset hashes, exact source bindings, phase inputs and
API/reader/control records are in [the machine receipt](resulting-main-qualification.json).

This proves the stated409-to438 executable upgrade. Original old-writer legacy B/A/hold, saved-start,
lost-ack and pruning proofs retain their earlier source revisions in the
[implementation ledger](../tasks.md). The6829whole local cases and genuine regression reds likewise
retain their reviewed-source scopes; this receipt does not claim all were rerun locally on main.

## Normal Studio behavior

The retained source-built Studio3815806acb51475903f3ee3b44cf263ac572db26 has unchanged production
sources under Studio/src and Studio/apps through main438. Its served loader and referenced main
chunk remain byte-identical. This checks those two assets, not every downloaded dependency.

Normal authenticated UI reload reopens definition14iBsLEuiJS and runs its retained executable
artifact-cfadffc29096, hash
`sha256:cfadffc290962fdc6a6bbc4e070f867c305028a6343178957c5a5dd77e685dd5`.
Actual PublishedRun14iKTVzYS2o completes the process and Delay with zero incidents. Supporting normal
API reads verify the exact pin, PublishedRun and both Completed activities. Full reload preserves
the gateway name, default flow and Never condition. The complete stored authored-v1 document and
the actual UI-downloaded BPMN XML equal the earlier saved/exported values, including PT1S.
Published frozen contracts match2/2. Two screenshots were visually inspected. Catalog81f6ae7a is
retained publication provenance, distinct from the executing backend438 build.

## Push gates and scope

Exact-main [CI40627777571](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40627777571),
[Studio40627777523](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40627777523) and
[CodeQL40627777535](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40627777535) passed.
Existing automatic [Packages40627777748](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40627777748)
and [Docker40628033549](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40628033549) also passed;
no additional release/deployment was started. All nine main-CI outcome artifacts report no unquarantined
repeated failure, passed-on-retry case or aborted run. Workbench-rest retains the existing #3261
compensation-sample quarantine. Declared skips remain skips. Main push omits EF container shards;
the actual ready-PR Full tier qualified those on the identical tree. Resulting-main verification is complete.

This is adoption verification. Separate #2898/#2907 direct-hosting and production journeys,
remaining #2892 acceptance, arbitrary migration and the deferred Orleans/task-runner work are not
qualified by this receipt. ADR0063 acceptance is the third ordered PR after verified delivery.

For reproduction, unpack each gzip into a fresh temporary directory; build
`tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` at the pinned source and invoke
`e2e-tests/bpmn/test_opted_called_snapshot_reader.py` with that reader assembly, the closed Runtime
snapshot, its uncompressed hash, the corresponding API receipt and phase from the machine receipt.
Use a fresh configured Workbench and `e2e-tests/bpmn/Test-BpmnCalledControlHost.ps1` for new actual
process witnesses. Existing records identify the original executions and do not require old machine
paths or identity/token databases.
