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
bounded; it is not the final public JSON canonicalization contract. Receipts have a separate capacity. The subsequent terminal probe captures an explicit experimental retention
window; it does not select a product default or qualify every retention/transport/value obligation.

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

## Terminal receipt slice

`--list-terminal` inventories nineteen compatible-state schedules and seven isolated controls; `--terminal` runs
them. The slice adds an optional model-owned metadata contract at the workflow actor, cold receipt queries and
retries, native normal/forced terminal settlement, persisted commit-time references and fenced normal delete refusal. Pending inputs have no expiry; each final disposition starts the full captured duration.
Queries do not restamp it. The fixture removes its definition from the registry and forbids receiver construction.
The recreation diagnostic now waits out its explicit promise before normal deletion; its identity bug remains.

Native state v3 supports v1/v2 reads. Legacy receipts get no invented retention window and remain protected;
legacy inboxes without a captured policy refuse new admissions. An older model refuses deleting newer state.
Forced termination preserves unreadable future private state and captures the workflow-owned end fact. A compatible
reader can project pending dispositions from that original fact. If the fact is absent, lookup reports
`TerminalMetadataUnresolved` without inventing a time; existing ID/content conflicts still resolve first. This
slice does not yet qualify actual future native binaries or close the full terminal plan.

The current nineteen schedules do not yet qualify completion/cancellation/query commit uncertainty, actual stale
delete execution, re-entry, equal frame IDs in different actors, active receipt budget reuse after expiry or legacy-version fixtures. Shared identity, remaining time/version obligations,
transport/value retention roots, provider/process restart, full business-input adapter and Studio remain open.
No execution result is asserted until the exact-source packet is recorded. No product schema/default is selected.


The earlier delayed-commit T4 failure remains in its separate raw packet. This source replaces staged absolute
expiry with an immutable reference to the actual finalizing commit. The nineteen schedules include the delayed
closing commit, cold definition-free receipt access, a normal failed-start JSONnull lookup/Delete, and different
invocations sharing a commit fact with different receipt windows. Cleanup must keep the shared fact until the
last reference expires and must preserve the workflow-owned end fact. The new exact-source packet is still
required before claiming that the original T4 failure is resolved.

The cancellation-after-earlier-work cases commit one handled input before a later terminal commit, and assert
its outcome, order, reference and original deadline through query/retry. Deletion also waits for the latest
remaining receipt promise. Shared-reference completion uses normal event publication and fan-out to both waits.

A real dispatched workflow child also completes under an ordinary caller. The harness records actual committed
child-finished message IDs and ordinary return-work calls, then issues repeated cold child receipt queries.
No second parent notice or caller execution is allowed. An isolated source mutation of that guard is required
alongside the seven internal controls; it is not counted as a passing ordinary run.

The metadata hook preserves main's terminal late-message draining. If the saved model state is newer than the
reader, metadata requests receive a generic ModelMetadataUnavailable result after a fenced opaque-state commit;
ordinary late input still drains. The shared clock runner asserts this through a deliberately older model. This
is a protocol/model-version fixture, not an actual future-native binary rollout qualification. The consuming
slice also clarifies the exact Committed.Now contract and centralizes the model clock-reference prefix.

Latest-main integration supplies explicit reply deadlines and advances the recreation diagnostic in bounded
lease-renewed steps. The diagnostic must retain the ordinary worker lease checks and normal deletion path.
