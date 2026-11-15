# Bounded commit-clock seals: PostgreSQL protocol experiment

Status: **Research fixture; no kernel/provider capability or budget adopted.** Part of [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229), for the data-lifecycle design question. This program references Npgsql and no Elsa runtime, EF store or model assembly. It does not enable the current nullable CommitTimes map or implement lifecycle roots, erasure or clock-cache behavior.

A business transaction creates an immutable-source pending obligation. A later owned batch changes a finite number to sealed anchors in one atomic PostgreSQL statement. Its guarded partition UPDATE supplies a RETURNING relation to the pending-row selection; the source marker must already be committed and visible. The batch uses one implicit write transaction. It updates partition and obligation rows separately; SQL textual CTE order is not a sequencing contract.

## Measured scope

Initial owned-local observations used PostgreSQL 17.11/aarch64, Npgsql 10.0.3, READ COMMITTED, fsync/synchronous_commit/full_page_writes on and native commit timestamps off. They are a physical protocol fixture, not a whole-kernel or production performance comparison. The numerical dataset is [observed-batches.csv](observed-batches.csv).

| Obligations | SQL statements / driver calls / write transactions / observed exchanges | Incremental WAL bytes |
|---|---|---:|
|1|1 /1 /1 /1|352|
|16|1 /1 /1 /1|3,504|
|1,000|1 /1 /1 /1|328,312|

The relay counts actual post-handshake client-direction reversals following server output. Frontend P,B,D,E,S and backend1,2,T,D,C:SELECT1,Z:I were observed for each normal batch. WAL spans fence and marker work between observed LSNs; background/full-page effects can contribute. These are not per-workflow costs, isolated per-row constants, latency, sustained-bloat or throughput results. The extra write transaction remains a real background cost. No current owner-path or total-kernel budget is inferred from it.

The initial full fixture passed 55 assertion lines, including bounded 5/5/5/2 drain of 17 rows, lost business and seal answers, fresh epochs and immutable reloaded anchors, stale-owner refusal, actual PostgreSQL blocking between competing resolvers, and counterfactual missing-fence/anchor-rewrite checks. A separate additive 11-assertion clock follow-up compared clock_timestamp with an old transaction_timestamp. The latter's computed expiry preceded a primary-clock observation before either business transaction began; the positive anchor followed independently verified commit visibility in the same old transaction. The two original source/binary revisions remain distinct research evidence; new runs must identify their own exact source.

This shows the earlier separately awaited five-exchange seal is not a protocol minimum. It does not prove portable support, exactly-once effects, actual kernel reset/replay, crash durability, replica/failover behavior, clock continuity, mixed versions, lineage/recreation, overflow or full-capacity scheduling. The original business body is not re-invoked after the fixture observes its committed marker. SQL Server, SQLite, MySQL and MongoDB are unqualified by this experiment.

## Run only against an owned fixture database

Build this project with the normal targeted build command. Start a separately owned PostgreSQL 17.11 primary with the durability settings above and a loopback port. Put its password in a local 0600 file, outside the checkout; never place credentials in source, arguments, logs or evidence. The program reads the file path. Create a fresh writable output directory first. The runner reserves observations.json with CreateNew before connecting; existing evidence is never overwritten. Invalid arguments are rejected before reservation or database access. Failures after reservation update only that invocation's file.

```text
dotnet build prototypes/kernel-model/experiments/CommitClock.Batch/Kernel.CommitClock.Batch.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet prototypes/kernel-model/experiments/CommitClock.Batch/bin/Release/net10.0/Kernel.CommitClock.Batch.dll <loopback-port> ksb_<eight-lowercase-hex-digits> <password-file-path> <output-directory>
```

Only exactly four arguments or exactly five with the recognized flag are accepted; database names must match the whole string, including no final newline. The full run checks that the named database is absent and creates it. It never drops or resets data. Do not point it at shared/user infrastructure or rerun it into a used full-run database. A failed attempt's database and logs remain evidence; use a new absent name for a corrected full run. Stop only the verified owned container after qualification and retain its data.

After that successful full run, a second invocation with the same four arguments and `--early-clock-control` exercises only the additive lower-bound pair. Use a different output directory. It first requires the matching database OID and versioned successful full-completion marker; partial, seeded, failed and older fixtures are refused before any write. It then requires empty fixture scopes 9/10, refuses an existing partition 10, and creates only that explicit additional case. It is not a cleanup command and is not repeatable against already-used scopes. The version/completion marker is fixture bookkeeping in separate setup/finalization transactions, outside the normal batch measurements; it is published only after all full checks and its PASS evidence have been written.

The relay withholds all server response bytes until a fresh ReadyForQuery with idle transaction status, then lets another primary connection verify committed data while the caller remains unanswered. It finally closes the connection. The first physical commit is observed before any claimed loss is classified as an unknown answer. Setup/observer traffic is separate from the measured normal seal. The focused old-transaction control uses a primary-clock condition, not a fixed sleep as its timing oracle.

The first research build had a Task-return helper compile error. An early run's positional-parameter multistatement command was refused 42601 before sealing. Another run reached the lost-answer case but its finally logger accessed a closed connection's ProcessID; the PID is now captured while open. Those retained failed attempts are not passing protocol results or kernel defects. Reproduction must keep its own failed/setup evidence and exact source, parameters, binary and observation hashes.

PostgreSQL's primary documentation describes [idle protocol status](https://www.postgresql.org/docs/17/protocol-message-formats.html), [statement snapshots](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-READ-COMMITTED), [clock functions](https://www.postgresql.org/docs/17/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT) and [data-modifying CTE dependencies](https://www.postgresql.org/docs/17/queries-with.html#QUERIES-WITH-MODIFYING). The minimum-window reasoning remains conditional on the selected trusted nonregressing time/durability domain. Do not substitute transaction-start or guessed node time, and do not transplant this fixture into a provider without its complete contract and affected cost qualification.

Safety regression: `python3 safety-checks.py <owned-container> <owner-label-value> <loopback-port> <0600-password-file> <fresh-evidence-directory>` resolves the full container ID, checks the owner label `kernel4229.clock-seal-r2.owner` and actual loopback PostgreSQL mapping before any SQL (also under Python `-O`), uses only newly named databases, and retains all resources/results. Build first. It runs full/additive success and checks evidence collisions, unwritable output, strict arguments and incomplete baselines. Interruption credit requires its exact checkpoint, sent SIGTERM, signal termination, an existing incomplete fixture and IN_PROGRESS evidence; the same binary's startup-refusal control cannot earn that credit. Stop the verified owned container separately afterward.
