# Matched response-profile command accounting

T010/T011 are root-accepted for progression on 8 October 2026. The bounded SQLite comparison observed 131 EF command attempts for the preserved External artifact and 101 for the newly published ReplaySafe candidate. The declaration remains conditional on causal mutation and remaining delivery gates. This is neither a PostgreSQL measurement nor a latency claim.

## Matched conditions

Both children used the same prebuilt runtime, SQLite provider, Coalesced/50 cadence, fusion settings, test keys and eligible page reuse. Normal publication prepared a seed before measurement. SQLite online backup made coherent copies; the External copy used normal unpublish and historical-source reconciliation to select the unchanged baseline. Fresh measured children performed equivalent startup/preflight checks, disabled setup-source reconciliation, and verified their sole active route and exact executable/profile/hash before counting. Publication and parent database observation were outside the count window.

The workflow is Sequence / HttpEndpoint / deterministic Set / WriteHttpResponse. An exact POST correlation marker and actual workflow identity separate request/drain, target background-resumption and unattributed work. Immutable snapshots follow full client response receipt and independent parent confirmation of correct Completed state and settled target scheduler/outbox work. Client receipt is not an exact server socket-write timestamp.

| Observed counter | External | ReplaySafe | Difference |
|---|---:|---:|---:|
| Logical checkpoint attempts / successes | 22 / 22 | 22 / 22 | 0 / 0 |
| Logical activity-claim attempts / successes | 6 / 6 | 6 / 6 | 0 / 0 |
| Deferred persistence decisions | 19 | 20 | +1 |
| Immediate persistence decisions | 3 | 2 | -1 |
| Durable checkpoint attempts / successes | 3 / 3 | 2 / 2 | -1 / -1 |
| Durable claim checkpoint attempts / successes | 2 / 2 | 1 / 1 | -1 / -1 |
| Segment flush attempts / successes | 3 / 3 | 2 / 2 | -1 / -1 |
| Dispatch observations | 17 | 11 | -6 |
| Drain-cycle observations | 10 | 6 | -4 |
| Activity-execution observations | 10 | 6 | -4 |
| EF command attempts / successes | 131 / 131 | 101 / 101 | -30 / -30 |
| EF Reader commands with LinqQuery source | 86 | 68 | -18 |
| EF Reader commands with SaveChanges source | 45 | 33 | -12 |

All observed work fell in request/drain in this finite run. Background-resumption and unattributed buckets were zero. Response-received and settled snapshots were identical: every between-window delta was zero. Failed/canceled commands and checkpoints, in-flight commands, carry-in outcomes and orphan outcomes were zero. [The raw report](matched-counts.json) retains every bucket/window; this does not establish that background work is universally absent.

These are EF Reader/Scalar/NonQuery execution attempts, not SQL statements or network round trips. Reader success means the provider returned a reader, not that every row was consumed. Claims exclude later writes that retain claim metadata. Activity-execution observations include container continuations; both databases contain four authored activity states.

## Independent acceptance

Root parsed the actual TRX (2 executed, 2 passed, zero failed/skipped), verified all eight fixture source hashes were unchanged, reviewed final source and bounded fixture corrections, and checked all five child-stage exits were zero. Separate read-only SQLite inspection used the actual encoded identity contract and confirmed both target executions Completed with four Completed activity states, exact artifact pins, original Alice/Smith request/correlation, `referenceText = Alice Smith`, and a committed HTTP 200 / text/plain / `Alice Smith` instruction with empty authored headers. Both had zero target incident, scheduler and outbox rows.

| Identity | External | ReplaySafe |
|---|---|---|
| Execution | `14BC3Tf296n` | `14BC3nyX4j3` |
| Artifact | `artifact-b542eafc273a` | `artifact-087fede776d2` |
| Artifact hash | `sha256:b542eafc273a2257ccc70481530f44448f0d9470f71144d1d12c686f49ae8574` | `sha256:087fede776d29cd1ceb406d15a04ddbdc0e9f59542c2d933897c9c96818da164` |

The completion observer matches actual metadata plus the completed activity upsert. Its `pinnedResponseProfile` comes from the preflight-verified executable; production completion metadata does not itself contain that profile.

## Frozen evidence and limits

Attempt 7 used HEAD `2fccc92b2bb4c3569c962daa060f197e8e1771ea` plus the test-fixture changes committed with this report. Production source did not change during this work. The focused HTTP integration run selected `ResponseReplayObservationTests` and `MatchedCoalescedProfiles`, with evidence retention enabled and the normal build-slot wrapper.

| Artifact | SHA-256 |
|---|---|
| Accepted TRX | `be2238df0e97c32e9e872b4bb4c37b27c3f9c304c50903fc8cb9585a5c106661` |
| Child DLL | `b1db8afd93822de279ac73f700d088fc033623da27bb05943e515ae92f96c2bf` |
| Raw report | `2a4a72955e6311dc6a06aeb7567cedaf13da8b459589681eaa11b3b664a35d19` |
| Baseline closure | `bde1e9a961e0c91734a86c9e565ee28e225f3d6637ebbd345e90db30f2656a13` |
| Process test | `551a9c66cfe106fb39639c7ee3aaece9a7aacb89f0d9df9d9bd3767d9766c1d7` |
| Observation core | `08d2778f87be07ae2163165e5cf063893adae673aced73788df8d1932df29b9d` |
| Observation registration/interceptor | `8f975f3e5edf2b3545f88bb6bfd422757715b9fee13275f9bafe4551d0a7ef3a` |
| Observer unit test | `0fb5bd30d7e2a9d0ebda4889a85b4089d987e062b572250002652492e31f00b6` |

The private program journal retains `2400-t010-t011/attempt7/`, all source manifests and `root-attempt7-acceptance.json`. Owned databases, child logs and IPC remain in `elsa-response-replay-t010-t011-600ca49b0e8a4e45aa35dd2665b53c9d` under the host temporary directory.

Earlier attempts remain failed evidence: 1–3 stopped on fixture imports before tests; 4 exposed public-only CShells feature discovery; 5 exposed an omitted transport-ready handshake; 6 incorrectly expected side-effect-profile metadata on completion. Source review established the corrected completion predicate above. Attempts 4–6 each passed the observer unit test and failed the matched process test. These corrections repaired test infrastructure, not production behavior.

The later [causal mutation and restored full integration proof](causal-mutation.md) passed. The retention decision remains conditional on the FR-004 external-reference restart proof, architecture/maps and final review in [the task plan](../tasks.md). Earlier affected-project suites and crash proof retain their own exact source scope. Final PostgreSQL/Azure measurements and integrated program acceptance remain separate downstream work.
