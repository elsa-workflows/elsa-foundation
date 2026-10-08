# Bounded response crash-recovery proof

T008/T009 are root-accepted for progression on 8 October 2026. The `WriteHttpResponse` ReplaySafe declaration remains conditional on T010–T018. This evidence does not establish a database-command reduction, PostgreSQL recovery coverage or restoration of a lost HTTP socket.

## Executed boundary

The process test normally publishes the candidate alongside the preserved External artifact and a valid REST companion, then restarts the persisted candidate without reimporting the historical setup closure. The crash-stage host uses SQLite, Coalesced cadence and a 50-checkpoint cap. HttpEndpoint remains External.

After the real store commits the HttpEndpoint activity-attempt claim, the parent reads SQLite independently and checks the original request/correlation, trigger seeds, pinned artifact, ready unclaimed invoke backstop and separate child-owned execution lease/fence. It then continues the child. The test decorator observes the matching response completion after the real Coalesced store returns, confirms an active applicable session and its buffered activity completion, sends its attestation and waits indefinitely. No parent command releases this barrier.

The parent confirms that no response-node completion is durable, kills the child process, and repeats the durable-state checks. It waits for the actual persisted owner lease to expire, with a bounded deadline, then starts normal runtime resumption using the same database and keys. It does not resend the request, enqueue replacement work or edit durable timestamps. The test verifies the same execution, original request and artifact pin, equivalent response instruction and terminal output, and no additional execution.

The gate shares only immutable test correlation state across scoped wrappers, with atomic one-shot observations. Fused activity dispatch can open a new scope; the registered runtime commit stores retain their original lifetimes. Any unexpected normal return from the crash-stage HTTP request fails the proof.

## Accepted run and independent review

Attempt 12's TRX reports one executed test, one pass, zero failures and zero skips, in 1 minute 48.7 seconds. Root inspected the TRX and raw IPC, reviewed the parent assertions and frozen source, and independently opened the retained database read-only after recovery.

| Evidence | Observed result |
|---|---|
| IPC stages | ready → start-request → endpoint-claim-durable → continue → response-buffered |
| Buffered response | Deferred; applicable active session; buffered changes; hop count 11 |
| Child exit codes | Publication setup 0; deliberately killed child 137; recovery child 0 |
| Original/recovered execution | `14B6jNgHduJ`, Completed |
| Artifact | `artifact-643c42d6e25f` |
| Artifact hash | `sha256:643c42d6e25fd06cc92bc78984617cf4f16b65c4c2f8f096561a75b2d7985204` |
| Committed response | Status 200; body `Alice Smith`; content type `text/plain`; empty authored headers |
| Persisted computation | Original Alice/Smith input and `referenceText = Alice Smith` |
| Target incidents / scheduler / outbox rows after recovery | 0 / 0 / 0 |
| Execution inventory | Three setup controls plus the one recovered execution; no duplicate recovery execution |

The pre-kill absence checks are asserted by the reviewed parent test against independent SQLite reads. Root's separate final database read establishes the recovered outcome, not a retrospective observation of that earlier window. The original unflushed response activity ID is not required to survive replay.

| Frozen artifact | SHA-256 |
|---|---|
| TRX | `45d89848d42aaf2d116d5637c484293ef1b76004136b7ce5f9f1e2564e98eb71` |
| Process test source | `7bd7c4c4cdc8e04a6419a9a791620f0c1943ed0bc066f684c867e2fcb51633c9` |
| Gate source | `e22969d6a92718c27f2fd2577dfe64daa26edf2340effac2ee99ca84d61ca70d` |
| Publication host source | `7d889c84d50597d0f4f29604392ff9d25bb9e533ad67edef3c03d1101c016c81` |
| Child Program source | `b548520fd3bd4aeb0fe01f995383b8ead48ea8ca4af29dfa8db97a5bf0576186` |
| Integration test DLL | `31bf38d28cc062b9177e3eb1dd249f5f7bf8befdf5588497657b6a98849d2ead` |
| Child DLL | `2f75e207cf5f2904073122a57ce9f048e7ddf4a236beb32d24e96ed5e6f22684` |

The private program journal retains `2400-t008-t009/attempt12/`, `root-attempt12-source-review.json` and `root-attempt12-acceptance.json`. The successful owned evidence directory is `elsa-response-replay-t009-9f500be998eb4414baeb0c4340d12fe2` under the host temporary directory. The acceptance receipt records its exact path and per-file hashes. `ELSA_RESPONSE_REPLAY_RETAIN_EVIDENCE=1` retains only the fresh T009 evidence directory on success; normal CI success cleanup is unchanged.

The test emitted an xUnit collection-size style warning. The enclosing zsh command subsequently exited 1 because the runner assigned its readonly `status` variable; the numeric `dotnet test` exit was not separately retained. Acceptance relies on the completed passing TRX, passing test output and reviewed runtime evidence. No shell exit-zero claim is made.

## Earlier outcomes and remaining gates

Earlier attempts remain failed evidence. The socket-path, historical-reconciliation, JSON-decoding, scheduler-claim expectation and scoped test-correlation corrections repaired the fixture. Attempt 11 completed normally without reaching the intended barrier. None of those attempts demonstrated a runtime repair or passed crash recovery.

The existing Immediate correctness controls remain separate. Matched same-binary counts, the one-line profile mutation, the final retention decision, complete affected suites, rebuilt Workbench HTTP E2E, Architecture, Maps and final review remain required by [the task plan](../tasks.md).
