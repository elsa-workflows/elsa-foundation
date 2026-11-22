# Receipt: Elsa 3 database commands for the three reference workflows

These counts come from a run, not from reading code. Every number below is in a `result.json` or a SQL log under
`results/`, and each case ran three times, each time on a fresh SQLite database file in a fresh host.

- Measured on 2026-11-15. The run started at 15:22:21Z and took 2 min 13 s for 27 runs.
- Machine: macOS 27.0.1 (26A434), arm64.
- .NET SDK 10.0.402, runtime Microsoft.NETCore.App 10.0.13 and Microsoft.AspNetCore.App 10.0.13. The harness targets `net10.0` and was built in Release.

## Packages (nuget.org, restored with a `<clear/>` NuGet.Config pointing only at nuget.org)

| Package | Version | Note |
|---|---|---|
| Elsa | 3.10.1 | The latest stable release, published 2026-11-14. Its nuspec names repository commit `1a95b12f`. |
| Elsa.Persistence.EFCore.Sqlite | 3.10.1 | In 3.6 and later this replaces `Elsa.EntityFrameworkCore.Sqlite`, whose last version is 3.5.3. |
| Elsa.Http | 3.10.1 | |
| Elsa.Expressions.JavaScript | 3.10.1 | Replaces `Elsa.JavaScript`, whose last version is 3.5.3. |
| Elsa.Persistence.EFCore.PostgreSql | 3.10.1 | Referenced for the PostgreSQL path, which was not run. See "PostgreSQL". |
| Microsoft.AspNetCore.TestHost | 10.0.0 | In-process HTTP through Elsa's middleware. |
| Microsoft.EntityFrameworkCore, .Relational, .Sqlite | 10.0.9 | Transitive. |
| Microsoft.Data.Sqlite | 10.0.9 | Transitive. |
| SQLitePCLRaw.bundle_e_sqlite3, SourceGear.sqlite3 | 3.0.3, 3.50.4.5 | Transitive. |
| Jint | 4.17.0 | Transitive, through Elsa.Expressions.JavaScript. |

The Elsa 3 testing helpers (`Elsa.Testing.Shared*`) are not used. Their fixtures build in-memory hosts, and this
measurement needed the production EF Core SQLite composition with its interceptors attached.

To read the APIs, I used the Elsa 3 source on branch `origin/release/3.10.0` of a local elsa-core clone
(head `ba2b801a9`, 2026-11-08), exported with `git archive`. The checkout itself was not modified. The commit the package was built from, `1a95b12f`, is not in the local clone, so
the source I read is the 3.10 release branch a few days before 3.10.1. The behaviour measured is the NuGet 3.10.1
binaries.

## Host composition

`Program.cs` (`BuildHost`) builds a new `WebApplication` with `UseTestServer()` for every run:

```csharp
services.AddElsa(elsa =>
{
    elsa.UseWorkflows(workflows => { /* activity-executed only: */ workflows.WithDefaultWorkflowCommitStrategy(new ActivityExecutedWorkflowStrategy()); });
    elsa.UseWorkflowManagement(m => { m.UseEntityFrameworkCore(ef => ef.UseSqlite(cs) + interceptors); /* default-cache only: */ m.UseCache(); });
    elsa.UseWorkflowRuntime(r => { r.UseEntityFrameworkCore(ef => ef.UseSqlite(cs) + interceptors); /* default-cache only: */ r.UseCache(); });
    elsa.UseJavaScript();
    elsa.UseHttp();
    elsa.AddWorkflow<WriteLineSequenceWorkflow>(); elsa.AddWorkflow<HttpReferenceWorkflow>(); elsa.AddWorkflow<ForkJoinResumeWorkflow>();
});
app.UseWorkflows();   // Elsa's HTTP workflows middleware
```

- `cs` is `Data Source=<run dir>/elsa.sqlite.db;Cache=Shared;`, which is Elsa's own default shape with a fresh file for each run.
- What Elsa 3 persists through EF Core with this composition:
  - `ManagementElsaDbContext`: workflow definitions and workflow instances.
  - `RuntimeElsaDbContext`: triggers, bookmarks, the bookmark queue, activity execution records, the workflow execution log, key-values (including the dispatch outbox) and the workflow inbox.
- These are the defaults, all left unchanged:
  - Execution-log and activity-record persistence are on.
  - `UseWorkflowCommitTransaction` is off.
  - The runtime is the local runtime, whose distributed lock is a file lock in `./App_Data/locks`.
  - There is no workflow commit strategy, so state is committed when a burst of execution ends.
- The same interceptor instances go on every Elsa DbContext. `Configure<TFeature,TDbContext>` wraps the feature's `DbContextOptionsBuilder` after `UseSqlite`.

## The workflows (`Workflows.cs`, code-first `WorkflowBase`)

| Name | Elsa 3 definition |
|---|---|
| `writeline-sequence` | `Sequence { WriteLine("line 1") … WriteLine("line 10") }`, started directly with `IWorkflowRuntime.CreateClientAsync()` and `CreateAndRunInstanceAsync(ByDefinitionId("writeline-sequence", Published))`. |
| `http-reference` | `Sequence { HttpEndpoint { Path "budget/http-reference", SupportedMethods [POST], CanStartWorkflow true, Authorize false, RouteData → var route, ParsedContent → var content }, SetVariable(referenceText = JavaScript "getVariable('content').firstName + ' ' + getVariable('content').lastName"), WriteHttpResponse { 200, "text/plain", Content = JavaScript "getVariable('referenceText')" } }`. One real `POST /workflows/budget/http-reference` with body `{"firstName":"Alice","lastName":"Smith"}` goes through Elsa's middleware on TestServer. Every run checked for `200 "Alice Smith"`. |
| `fork-join-resume` | `Parallel { WriteLine("forked"), Event("budget-resume") }`, started directly. Every run checked that the instance was Running/Suspended with one bookmark. It was then resumed with `IWorkflowResumer.ResumeAsync<Event>(new EventStimulus("budget-resume"), instanceId)`, which finds the bookmark by instance and stimulus hash and runs the instance in-process. Every run checked that the instance was Finished with zero bookmarks. |

Every run also checked that the instance's final status was Finished/Finished with zero incidents.

## Counting rule

- Elsa 4's rule, unchanged: a fresh SQLite database, and every reader, non-query and scalar command the Elsa DbContexts execute.
  - `Capture.cs` holds a `DbCommandInterceptor` that records `ReaderExecuting`, `NonQueryExecuting` and `ScalarExecuting`, sync and async, as each command starts. It records the DbContext type, the command kind and the full SQL text.
  - The same file holds a `DbTransactionInterceptor`, which records transactions started, committed and rolled back, and a `SaveChangesInterceptor`, which records `SaveChanges` calls. These events are not commands and are not in the counts.
- Duplicate callbacks are skipped. The management feature and its definitions and instances sub-features each call `AddDbContextFactory<ManagementElsaDbContext>`, so Elsa 3 registers that context's options three times. As a result, the interceptors sit in that context's chain three times. EF passes the same `DbCommand` object to every interceptor in the chain, so the recorder deduplicates by object identity.
  - The first trial counted each management command three times, at identical timestamps.
  - The database rows confirm that each command executed once, for example one `WorkflowInstances` row.
- On SQLite, EF Core does not batch: each row a `SaveChanges` writes is its own command. Elsa 3's activity execution records are the exception. They are written with one multi-row `INSERT … ON CONFLICT DO UPDATE` non-query per commit, outside `SaveChanges`.

### Counting window and what was kept out

1. Setup comes before the window opens:
   - host start and every startup task: migrations, the definition store populator that "publishes" the code-first definitions, trigger indexing and the HTTP route table;
   - then a wait until the database has been quiet for 2 s.
2. The window opens just before the start call, the HTTP `SendAsync` or the resume call. For `fork-join-resume` there are two windows, `start` and `resume`, which are added together.
3. After the call returns, the window stays open until the database has been quiet for 1.5 s, so work the run hands to background loops is counted as well. Each record is tagged:
   - **in-flow**: its async flow came from the measured call. For HTTP, a middleware joins the flow for requests that carry an `x-elsa3cost-phase` header, because TestServer does not pass the caller's execution context to the request.
   - **background**: it ran on another loop.
4. Every recurring task Elsa 3 registers is moved to a 1-hour interval through `RecurringTaskOptions`, the same idea as Elsa 4's `BeyondAnyRun`:
   - `TriggerBookmarkQueueRecurringTask`
   - `PurgeBookmarkQueueRecurringTask`
   - `RestartInterruptedWorkflowsTask`
   - `ProcessWorkflowDispatchOutboxRecurringTask`
   - `RefreshWorkflowDefinitionActivityRegistryTask`
5. **Couldn't exclude: the bookmark-queue worker.**
   - `SignalBookmarkQueueWorker` signals it on `WorkflowInstanceSaved` and `BookmarkSaved`, so the run itself causes these polls; they are not a timer.
   - It is throttled to 500 ms. Each commit burst produces two polls of `BookmarkQueueItems` (a full select, then a paged select), which is 4 commands.
   - All of them are tagged background and reported separately, and their total is deterministic.
   - In `activity-executed`, some of them land before the call returns. That is why the "until the call returned" column moves by 1 in `fork-join-resume` (45/46/45), while the in-flow and settled counts do not move.
6. Not database commands, so not counted:
   - the file lock in `App_Data/locks` (Elsa 3's default `IDistributedLockProvider`);
   - EF's internal `PRAGMA`s on connection open;
   - `BEGIN`/`COMMIT`, which the command interceptor does not see in Elsa 3 or in Elsa 4. They are reported as transactions.

## How it was run

The harness is in `harness/`, with every file renamed to `.txt` so the repository's builds and guards never pick it up. To rerun, copy it to a scratch folder and drop the `.txt` endings, then:

```bash
dotnet build -c Release
dotnet bin/Release/net10.0/Elsa3Cost.dll --out ./results --provider sqlite --runs 3   # all 3 workflows × 3 configs
```

Exit code 0, and no run failed. `results/` keeps the summary and, per case, run 1's counted commands (`window.sql`) and `result.json`; runs 2 and 3 were identical.

## Results (SQLite, commands)

The configurations:

- `default`: Elsa 3's default, with no commit strategy.
- `activity-executed`: "Commit After Activity Execution" as the global default, the closest analogue to Elsa 4's Durable.
- `default-cache`: the default plus `UseCache()` on management and runtime, as the Elsa.Server sample host does.

The columns:

- **workflow-flow**: commands caused on the measured call's own async flow.
- **queue polls**: bookmark-queue worker polls that the run's commits trigger.
- **settled**: the total until the database was quiet.
- **until return**: Elsa 4's literal rule, which stops counting when the awaited call returns.

| Workflow | Config | Run | Workflow-flow | Queue polls | Settled | Until return | SaveChanges | Transactions | Phases (until return + after) |
|---|---|---|---|---|---|---|---|---|---|
| writeline-sequence | default | 1 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| writeline-sequence | default | 2 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| writeline-sequence | default | 3 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| writeline-sequence | activity-executed | 1 | 64 | 4 | 68 | 66 | 25 | 10 | start 66+2 |
| writeline-sequence | activity-executed | 2 | 64 | 4 | 68 | 66 | 25 | 10 | start 66+2 |
| writeline-sequence | activity-executed | 3 | 64 | 4 | 68 | 66 | 25 | 10 | start 66+2 |
| writeline-sequence | default-cache | 1 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| writeline-sequence | default-cache | 2 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| writeline-sequence | default-cache | 3 | 29 | 4 | 33 | 29 | 2 | 1 | start 29+4 |
| http-reference | default | 1 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| http-reference | default | 2 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| http-reference | default | 3 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| http-reference | activity-executed | 1 | 29 | 4 | 33 | 31 | 11 | 3 | request 31+2 |
| http-reference | activity-executed | 2 | 29 | 4 | 33 | 31 | 11 | 3 | request 31+2 |
| http-reference | activity-executed | 3 | 29 | 4 | 33 | 31 | 11 | 3 | request 31+2 |
| http-reference | default-cache | 1 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| http-reference | default-cache | 2 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| http-reference | default-cache | 3 | 15 | 4 | 19 | 15 | 2 | 1 | request 15+4 |
| fork-join-resume | default | 1 | 25 | 8 | 33 | 25 | 5 | 2 | start 13+4; resume 12+4 |
| fork-join-resume | default | 2 | 25 | 8 | 33 | 25 | 5 | 2 | start 13+4; resume 12+4 |
| fork-join-resume | default | 3 | 25 | 8 | 33 | 25 | 5 | 2 | start 13+4; resume 12+4 |
| fork-join-resume | activity-executed | 1 | 42 | 8 | 50 | 45 | 15 | 2 | start 28+2; resume 17+3 |
| fork-join-resume | activity-executed | 2 | 42 | 8 | 50 | 46 | 15 | 2 | start 28+2; resume 18+2 |
| fork-join-resume | activity-executed | 3 | 42 | 8 | 50 | 45 | 15 | 2 | start 28+2; resume 17+3 |
| fork-join-resume | default-cache | 1 | 24 | 8 | 32 | 24 | 5 | 2 | start 13+4; resume 11+4 |
| fork-join-resume | default-cache | 2 | 24 | 8 | 32 | 24 | 5 | 2 | start 13+4; resume 11+4 |
| fork-join-resume | default-cache | 3 | 24 | 8 | 32 | 24 | 5 | 2 | start 13+4; resume 11+4 |

**Run-to-run variance:**

- The workflow-flow, queue-poll and settled counts are identical across all three runs of every case.
- The full statement breakdown by context, operation and table is identical too.
- The only variation is the interleaving in the "until return" column for `fork-join-resume` / `activity-executed`.

## Breakdown by category (settled window, run 1; every run is identical)

Commands are grouped by the table they touch.

| Workflow | Config | Instance state (WorkflowInstances) | Bookmarks | Definitions | Triggers | Activity execution records | Execution log | Bookmark-queue polls | Total |
|---|---|---|---|---|---|---|---|---|---|
| writeline-sequence | default | 2 | 0 | 2 | 0 | 1 | 24 | 4 | 33 |
| writeline-sequence | activity-executed | 26 | 0 | 2 | 0 | 12 | 24 | 4 | 68 |
| writeline-sequence | default-cache | 2 | 0 | 2 | 0 | 1 | 24 | 4 | 33 |
| http-reference | default | 2 | 0 | 1 | 1 | 1 | 10 | 4 | 19 |
| http-reference | activity-executed | 12 | 0 | 1 | 1 | 5 | 10 | 4 | 33 |
| http-reference | default-cache | 2 | 0 | 1 | 1 | 1 | 10 | 4 | 19 |
| fork-join-resume | default | 5 | 5 | 3 | 0 | 2 | 10 | 8 | 33 |
| fork-join-resume | activity-executed | 15 | 9 | 3 | 0 | 5 | 10 | 8 | 50 |
| fork-join-resume | default-cache | 5 | 5 | 2 | 0 | 2 | 10 | 8 | 32 |

Grouped further for the comparison:

| Default config | State: instances + bookmarks | Lookups: definitions + triggers | History: records + log | Queue polls | Total |
|---|---|---|---|---|---|
| writeline-sequence | 2 | 2 | 25 | 4 | 33 |
| http-reference | 2 | 2 | 11 | 4 | 19 |
| fork-join-resume | 10 | 3 | 12 | 8 | 33 |

### By statement (default config, run 1)

- **writeline-sequence (33)**
  - ManagementElsaDbContext, 4: SELECT WorkflowDefinitions ×2 (by DefinitionId+IsPublished, then by Id); SELECT EXISTS WorkflowInstances ×1; INSERT WorkflowInstances ×1.
  - RuntimeElsaDbContext, 29: INSERT ActivityExecutionRecords ×1 (one upsert carrying the 12 records); INSERT WorkflowExecutionLogRecords ×24 (one per log row, inside one transaction); SELECT BookmarkQueueItems ×4 (background).
  - SaveChanges: 2 (runtime 1, management 1). Transactions: 1.
- **http-reference (19)**
  - SELECT Triggers ×1 (the route table itself is in memory) and SELECT WorkflowDefinitions ×1.
  - INSERT ActivityExecutionRecords ×1 (5 records) and INSERT WorkflowExecutionLogRecords ×10.
  - SELECT EXISTS + INSERT WorkflowInstances.
  - SELECT BookmarkQueueItems ×4 (background).
- **fork-join-resume (33): start 17**
  - SELECT WorkflowDefinitions ×2.
  - SELECT EXISTS Bookmarks, then INSERT Bookmarks.
  - INSERT ActivityExecutionRecords ×1 and INSERT WorkflowExecutionLogRecords ×6.
  - SELECT EXISTS + INSERT WorkflowInstances.
  - Queue ×4.
- **fork-join-resume (33): resume 16**
  - SELECT Bookmarks (find by instance and hash), SELECT WorkflowInstances, SELECT WorkflowDefinitions.
  - SELECT Bookmarks (diff), then DELETE Bookmarks.
  - INSERT ActivityExecutionRecords ×1 and INSERT WorkflowExecutionLogRecords ×4.
  - SELECT EXISTS + UPDATE WorkflowInstances.
  - Queue ×4.

By operation, default config:

| Workflow | SELECT | INSERT | UPDATE | DELETE |
|---|---|---|---|---|
| writeline-sequence | 7 | 26 | 0 | 0 |
| http-reference | 7 | 12 | 0 | 0 |
| fork-join-resume | 17 | 14 | 1 | 1 |

`summary.md` has the same tables for `activity-executed` and `default-cache`.

## Where this is not apples to apples with Elsa 4's gate (64 / 83 / 131 Balanced, 221 / 149 / 194 Durable)

1. **Guarantees bought.**
   - Elsa 3's default commit is three or four independent writes with no transaction across them: the activity-record upsert, the log `SaveChanges`, then the instance `SaveChanges` on a second DbContext. `UseWorkflowCommitTransaction` is off by default.
   - Elsa 3's default also has no database lease or fence on the instance (its lock is a file lock), no scheduler work items and no post-commit outbox rows.
   - Elsa 4's counts include whatever its lease, claim, fence and outbox machinery costs. The difference is partly price, partly different guarantees.
2. **Contexts.**
   - The Elsa 4 gate counts one context, `RuntimeSqliteDbContext`.
   - The Elsa 3 numbers cover both of Elsa 3's contexts, Management (definitions and instances) and Runtime (everything else), because that is all Elsa 3 persists.
3. **History is a large share of Elsa 3's count: 25 of 29, 11 of 15 and 12 of 25 commands in the default config.**
   - Execution-log rows are one INSERT per row on SQLite: 24 of the 33 for `writeline-sequence`.
   - Activity execution records are one multi-row upsert command per commit, however many rows it carries.
   - Without the log and records, Elsa 3's default is 4 / 4 / 13 workflow-flow commands, plus queue polls. These are derived from the measured breakdown, not a separate run.
   - Whether Elsa 4's "Full history" rows are in its 64 / 83 / 131, and at what per-row cost, has to be read from Elsa 4's own CommandCost breakdown.
4. **Unit.**
   - Both count DbCommands on EF Core SQLite, where EF Core does not batch, so per-row INSERTs count individually in both.
   - Elsa 3's bulk upsert for activity records is one command for N rows.
   - `BEGIN`/`COMMIT` are not counted in either.
5. **Window.**
   - Elsa 4 counts until the awaited call returns. Elsa 3's workflow-flow column equals that rule exactly in the default config: 29 / 15 / 25.
   - The settled column adds the bookmark-queue polls the run's commits trigger: +4 per commit burst, so 33 / 19 / 33.
   - Elsa 4 asserts its pumps have nothing left to drain but does not count them.
6. **Definition loading.**
   - Elsa 3 reads the published definition from the database on every start: 2 SELECTs for a direct start, 1 for HTTP.
   - It reads it again on resume: 1 SELECT.
   - `UseCache` saves only the resume read in a fresh host, because the first read fills the cache.
   - The HTTP path also does 1 SELECT on Triggers per request.
   - The Elsa 4 gate publishes a hand-built executable in the same host.
7. **Workflow shape.**
   - Elsa 3 workflows are code-first `WorkflowBase` classes, published at startup by the store populator, not designer JSON.
   - The Elsa 3 HttpEndpoint captures RouteData and ParsedContent but not the request object, because `HttpRequest` cannot be stored in a variable. Elsa 4 also captures `Request`.
   - Variables live inside the instance's `Data` column, at no extra command.
   - The HTTP path is `/workflows/budget/http-reference`, not `/workflows/http/...`.
8. **Resume API.**
   - `IWorkflowResumer.ResumeAsync<Event>(stimulus, instanceId)` resumes synchronously in-process. It is the analogue of Elsa 4's `BookmarkResumeDispatcher` by execution id and stimulus.
   - `IEventPublisher` would add a trigger lookup.
9. **Commit strategies.**
   - `default` commits once per burst: start, suspension, completion. It is closest to Balanced, but Elsa 3 has no 50-activity cap.
   - `activity-executed` commits after every executed activity, the closest analogue to Durable.
   - `activity-executed` flushes incrementally: each commit is an activity-record upsert, the log rows new since the last commit, and `SELECT EXISTS` + `UPDATE` of the instance. The log-row count (24) does not grow; the instance and record commands do (`writeline-sequence`: 12 upserts and 13 instance saves).
10. **Instance save.** Every Elsa 3 instance commit is `SELECT EXISTS` plus `INSERT` or `UPDATE` of the full row, 2 commands.
11. **Version.** These numbers are for Elsa 3.10.1. Earlier 3.x releases, which had no bookmark queue, no dispatch outbox and no bulk activity-record upsert, would give different numbers.

## PostgreSQL

Not run.

- The Docker daemon (OrbStack) was not running (`docker version` could not reach `~/.orbstack/run/docker.sock`).
- There is no local PostgreSQL install (`pg_ctl`, `initdb` and `psql` are absent).
- I did not start OrbStack.
- The harness has a `--provider postgresql --pg <admin connection string>` path, which creates and drops a database per run. That path is **untested**.
- A PostgreSQL run would count commands, not wire round trips. On PostgreSQL, EF batches the per-row log inserts into one command, so the numbers would differ a lot from SQLite.

## Files

- `Elsa3Cost.csproj`, `NuGet.Config`, `Program.cs` (host, runners, window), `Workflows.cs`, `Capture.cs` (interceptors and recorder), `Report.cs` (SQL statement parsing, categories, summary).
- `results/summary.md` and `results/summary.json`: every run, every breakdown.
- `results/<workflow>/<config>/run-<n>/` holds:
  - `window.sql`: only the counted commands, plus SaveChanges and transaction events, each tagged with phase, flow, context and kind, with the full SQL;
  - `commands.sql.log`: every command, including setup (migrations, publishing) and the outcome checks after the window;
  - `result.json`: that run's counts and breakdowns;
  - `elsa.sqlite.db`: the database the run left.
- `run-sqlite.console.log`: the console output of the measured run.
