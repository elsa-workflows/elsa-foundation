# Native metadata admission probe

Non-packable M0 research runner; see the [reviewed plan](../../../../specs/1210-state-machine/native-admission-plan.md).
It does not implement StateMachine or approve kernel/product placement.

From the repository root, through the normal build-slot wrapper on PATH:

```sh
dotnet build prototypes/kernel-model/models/Admission.Run -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet prototypes/kernel-model/models/Admission.Run/bin/Release/net10.0/Kernel.Admission.Run.dll
```

Bound one focused build to 600 seconds including queue time and one runner to 60 seconds. No retry or wider
build is implicit in a failure. Record actual source, command, exit, discovered/executed schedule/control counts
and any failure before another run. No host, provider, Docker, cloud resource or old Azure grant is used.

The planned 28 schedules cross root/nested Sequence placement, Balanced/Durable modes and five commit/owner
cuts, plus four ordinary-child-fault blockage and four combined blocked-signal/admission cases. Each simulation
action invokes the actual kernel invariants; the witness reads committed native metadata and captured transport
replies. Five deliberately broken modes must fail their named assertions for authored hydration, lost sender-ID
deduplication, capacity, pre-commit reply and metadata starvation behind a blocked signal.

The probe's invocation inbox policy is captured on normal activation. Control-only turns do not construct that
activity, bind its inputs or drain work. Request-correlated replies do not enroll a workflow observer and stay
post-commit even if the request opted into early ordinary output. The exact-string fingerprint is versioned and
bounded; it is not the final public JSON canonicalization contract. Receipts have a separate capacity but no
expiry policy here. Completed-invocation cleanup and terminal query/duplicate retention are deliberately unproved.

The fault variant only records a blockage through a normal composite callback. It does not implement or prove an
operator retry. Model reload/takeover is not a real process/host/provider restart. Causal trigger cohorts, the full
statechart, production authorization/API, retention/deletion, compensation and Studio acceptance remain open.
No result is asserted by this README until an exact-source run is recorded on its PR.

## Delete/recreate counterexample

`--recreate` is a separate diagnostic, not a passing semantic qualification. It uses normal start, ordinary child
completion, delete and start commands, then retries the old invocation input against the reused workflow ID.
Four cases cross Balanced/Durable and root/nested placement. Its expected observation is a reused local frame
address and a fresh acceptance into the new creation. The existing simulation oracle first reports I10 (the same ID went from its recorded Finished revision to a
new Running row); the diagnostic records that exact target/status/revision failure and rejects other reported
errors. Later checks, including I22 (deleted row reappeared), are not reached or independently qualified. No row is seeded or edited, no private nonce fixes the shared identity
contract, and no public/provider/host result is claimed. If identity is fixed, this expected-counterexample
assertion must fail and be replaced with a rejection regression. See the [terminal plan](../../../../specs/1210-state-machine/terminal-receipts-plan.md).
