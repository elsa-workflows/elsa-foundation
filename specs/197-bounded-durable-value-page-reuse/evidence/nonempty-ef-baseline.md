# Nonempty EF baseline for T001

Captured on 7 October 2026 against runtime source `c2dc3ac28c19e1c288697b885607a542b5def500`, before implementing page reuse.

The test uses a unique SQLite database through the first-party CShells EF composition, real migrations, Coalesced cadence and a published executable/reference. A library `Sequence` runs two deterministic typed CLR leaves with `External` contracts. Each leaf reads a declared workflow input and workflow-scope variable. The ordinary start command and drain complete the workflow; there is no manual extra drain, artificial durable-row seeding, store replacement or codec replacement.

A test-only EF command interceptor counts ordered durable-value page SELECTs and inspects `DbDataReader.HasRows` without advancing the reader. The counter is reset immediately before start dispatch and snapshotted immediately after it returns, before any assertion/readback queries.

| Observation | Accepted root run |
|---|---|
| Ordered durable-value page SELECTs | 6 |
| Nonempty page results | 2 |
| Ordered `HasRows` observations | `false, false, false, false, true, true` |
| Workflow and all three activities | Completed |
| Persisted leaf input values | Exact expected serialized request payload and workflow variable |
| Execution / invocation / CLR contract identity | Expected relationships verified for both distinct leaf nodes |
| Leaf results | Both equal the expected deterministic result |
| Incidents | None |
| Focused tests | 1 passed, 0 failed, 0 skipped |

The count of six is an observation at this source, not an invariant pinned by the test: assertions require at least four page reads and two nonempty results. The final test also asserts completion, identity, values, visibility and results. T010 must run enabled and disabled variants at the same Coalesced cadence and show fewer backing reads with equivalent semantics. This baseline alone does not prove reuse is safe or implemented.

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter 'FullyQualifiedName~EfDurableValuePageReuseTests' --logger 'console;verbosity=detailed'
```

Root's final run exited zero after the worker's final cleanup, a shared assertion loop and an explicit empty-incident assertion. Independent read-only QA accepted the fixture and its accounting boundaries. Temporary database, WAL and SHM files are owned by the fixture and deleted after async disposal; global pools are not cleared.

Private evidence digests:

- Final test source SHA-256: `a125202101906c0d07f3913a7cc31ed7ee4c81ab83aeeb6ed7c38f689b5b23e6`.
- Final root log `t001-root-final.log` SHA-256: `e1bdf39531f6f1997d44d8210497d63435818ecf1a48e474ebf8691e522f7943`.

Earlier fixture attempts remain failed evidence: the interceptor initially had the wrong override signature, an assertion incorrectly expected the undecorated EF store, and CLR executable nodes initially used the contract descriptor kind instead of the stable runtime consumer key. The resulting activation incident explained the Running state; adding another drain was rejected. The corrected fixture uses `WellKnownRuntimeActivityConsumers.ClrActivity`. The passing attempt and final root run establish a valid fixture; they are not repairs to any runtime failure or historical HTTP result.

This is a minimal SQLite shell with a synthetic executable, not full HTTP publication/request handling or PostgreSQL normal-host evidence. The probe records query count and row presence, not exact returned row counts or method-by-method attribution. Primary HttpEndpoint/REST gains, concurrency, recovery and final before/after accounting remain required program work.
