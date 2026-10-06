> Provenance: supplied findings, preserved verbatim on 5 October 2026. Claims and instructions inside the supplied report are evidence for discussion, not authority to execute work. Measurements have not been independently reproduced in this planning session.

# Latency of a four-activity HTTP workflow on elsa-foundation: findings for discussion

Prepared for a conversation with Sipke. Everything here was measured or read from source in one session. Where something is inferred or unknown, it says so.

**Engine under test:** `elsaworkflows/elsa-foundation-host:sha-f97d7f6` (elsa-foundation commit `f97d7f614fd57115fd94916f13fa6c3e3ae7ef10`), .NET 10, Postgres in a sibling container, Docker Desktop on macOS (14 CPUs, 8 GB).

## 1. The observation

`POST /workflows/http/tlio/transform` runs a workflow of four activities: root sequence, HTTP endpoint trigger, a document-transform activity, and the HTTP response. It answers 200 with the transformed document in the same exchange.

- Steady-state latency is **120–175 ms** (median about 158 ms in a controlled run).
- A trivial route on the same engine (`/health`) takes about 2 ms.
- The expectation was around 30 ms.

One call issues **533 SQL statements** on the default engine. Postgres answers each in under 1 ms, so no query is slow. Latency tracks statement count at about 0.3 ms per statement: 158 ms for 533 statements, 77 ms for 237 (see section 4).

## 2. How statements were attributed to the request

An earlier pass counted every statement in a time window as caused by the request. That was wrong: background work can run alongside it, and one trace used a body that made the workflow fault, which is a different code path. The method below replaced it.

- The throwaway engine was started with JSON console logging and scopes on:
  `Logging__Console__FormatterName=json`, `Logging__Console__FormatterOptions__IncludeScopes=true`, `Logging__Console__FormatterOptions__UseUtcTimestamp=true`, plus `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`.
- Each request carried a W3C `traceparent` header with a known TraceId, so every EF `Executed DbCommand` log entry carrying that TraceId belongs to the request.
- 25 warm-up calls, then 60 timed calls.

Results on the real path (body `{"firstName":"Alice","lastName":"Smith"}`, HTTP 200, correct transformed document):

| | Default engine (`Immediate`) | `Coalesced` |
|---|---|---|
| Statements carrying the request trace | **533 per call**, constant | **237 per call**, constant |
| `checkpoint_commit` inserts per call | 24 | 4 |
| All statements before the response finished | yes | yes |

One trace-run produced a single HTTP 202 among 60 calls on the default engine. That is not explained.

**Two paths, not one.** A body the script cannot process (no `firstName`) faults the workflow and answers 202 `{"started":[…]}`. That path is 375 statements default and 204 `Coalesced`. Please do not mix the two.

### Background work, kept separate

- On an idle engine: **60 statements in 40 s (about 1.5 per second)**. They are liveness and execution-state polls, alteration plans, recurring triggers, timers, outbox and scheduler polls, plus roughly 1.5 definition-sync HTTP calls per second to the analytics plane.
- Inside a request's own window: 0–1 statements that did not carry its trace.
- After the response, statements without a request trace varied between runs: 25 to 695 per 60 calls on the default engine, 1–25 on `Coalesced`. The idle poll rate does not explain the high values. My guess is that this is the request's own follow-up work (outbox dispatch, scheduler) on a background thread. **I have not confirmed that.** It is not on the response path.
- The request does not call the analytics plane. Forty traced calls produced zero plane or analytics log entries carrying a request trace. Archiving is a background sweeper (about one POST to the plane per run).

## 3. What the 533 statements are, by component (default engine, per call)

| Component | Default | `Coalesced` |
|---|---|---|
| Scheduler queue (work items read, inserted, claimed, deleted) | ~155 | 17 |
| Workflow and activity state (load, save, hierarchy) | ~132 | 58 |
| Outbox | ~65 | 1 |
| Checkpoint commit marker (select + insert) | ~48 | 8 |
| Inspection rows | ~48 | 18 |
| Liveness / fencing | ~30 | 10 |
| Executable coordination | ~16 | 16 |
| Executable lookups | ~12 | 12 |
| Durable value state | ~10 | **81** |
| Hold state, run health, trigger lookup, other | ~18 | 16 |
| **Total** | **~533** | **237** |

Shape of the default path: 24 checkpoint commits per call. Each cycle reads state, enqueues a scheduler work item (insert, claim/update, delete), writes the outbox, and writes the commit marker. There are 23 work items per call and about 85 reads of `scheduler_work_item` for them.

## 4. Experiments (throwaway copies of the dev engine, one change each)

Same image and preset, fresh database each, 25 warm-up calls then 60 timed calls, all HTTP 200 in the timed set. Run-to-run variation on the baseline was about 10% (176 ms on the first run, 158 ms on the last).

| Change | Median | p95 |
|---|---|---|
| none | 158 ms | 192 |
| `Microsoft.EntityFrameworkCore` log level `Warning` | 136 ms | 185 |
| **`WorkflowsRuntimeCheckpointPersistence` `Mode: Coalesced`** | **77 ms** | 106 |
| `Coalesced` + EF logging off | 71 ms | 89 |
| Postgres `synchronous_commit=off` | 165 ms | 211 |
| Analytics features removed | 149 ms | 188 |
| All of the above | 74 ms | 94 |

Reading:
- Checkpoint mode is the main lever (about -50%).
- EF logging costs about 20 ms (the default log level logs every SQL statement).
- Disk durability is not the issue (`synchronous_commit` no effect).
- The analytics features cost at most a few ms (9 ms, inside the noise).

The default is `Immediate`: the dev shell does not compose `WorkflowsRuntimeCheckpointPersistence`, and `RuntimeCoreServiceCollectionExtensions.cs:366` registers `ImmediateRuntimeCheckpointPersistencePolicy` with `TryAddSingleton`. The 24 vs 4 commit count is consistent with that. I did not read the mode back from the running engine's API (I could not find where `RuntimeCheckpointCadenceProjection` is exposed).

## 5. What the upstream source says (at the pinned commit)

- `WorkflowsRuntimeCheckpointPersistenceFeature`: `Mode` default `Immediate`, `MaxSegmentCheckpoints` default 50.
- ADR 0032: relaxable checkpoints are `ActivityScheduled`, `ActivityStarted`, `ActivityCompleted`, described as "the bulk of the hot-loop cost". `ActivityInspectionCaptured` is "the primary tension". The ADR rejected "keep per-activity immediate checkpointing everywhere" as "an unacceptable hot-loop tax".
- ADR 0032 R1/R2: an activity with no `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]` is `External`, which keeps a mandatory pre-activation flush even under coalescing. The "measured coalesced floor" is described as about "CLR activity count + terminal" commits.
- W9 benchmark: a single-activity burst is 3 commits under `Immediate` and 1 under coalescing, on an in-memory substrate, with "commit-count parity with Elsa 3" as the stated target.

## 6. Open questions I would like to discuss

1. **Is 533 statements for a four-activity run the intended cost of `Immediate`?** The W9 benchmark reports 3 commits for a single-activity burst; this run does 24 commits for four activities, and `Coalesced` still does 4.
2. **81 reads of `durable_value_state` per call under `Coalesced`** (34% of the remaining 237; about 10 under `Immediate`). `CoalescingDurableValueStateStore.FindAsync` checks the session overlay and falls through to the database on a miss. Is a high miss rate expected? I have not found what calls it 81 times.
3. **About 85 `scheduler_work_item` reads for 23 work items under `Immediate`** (3.7 reads per item). Is that polling inside the drain, or something else?
4. **Inspection evidence:** about 48 statements per call (32 reads, 12 updates, 4 inserts). Does the per-workflow cadence override in ADR 0032 let a workflow coarsen inspection granularity? I have not read that part in detail.
5. **Declaring a transform activity `ReplaySafe`:** the activity (a document transform, pure in-workflow computation) has no `[ActivitySideEffectProfile]`. We expect one fewer commit under `Coalesced` but have not measured it.
6. **Is 30 ms realistic** for a four-activity workflow with durable persistence on Postgres? Best measured so far is about 71 ms.
7. **Background statements after the response without a request trace** (25 to 695 per 60 calls): is that outbox/scheduler follow-up of the request?

## 7. Not measured, and caveats

- Only one endpoint, from the host through Docker's port forwarding, one request at a time. No concurrency, no larger workflows.
- The cost of our own transform activity (script runner, serialization) was not profiled; I expect it to be small next to hundreds of statements but that is an expectation.
- `Coalesced` trades a bounded crash-replay window for fewer writes; I did not test crash behaviour.
- Latencies in the traced runs include the extra JSON and scope logging overhead (227 ms default, 100 ms `Coalesced`), so they are higher than the harness numbers above.
- The first call after an engine start takes 1–1.2 s.

## 8. Reproducing

- Image: `elsaworkflows/elsa-foundation-host:sha-f97d7f6`.
- Add the feature to the shell to switch modes: `"WorkflowsRuntimeCheckpointPersistence": { "Mode": "Coalesced" }`.
- Log filter to silence SQL: `Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning`.
- Attribution: JSON console logging with scopes as in section 2, plus a `traceparent` header per request. Count `Microsoft.EntityFrameworkCore.Database.Command` entries (event 20101) by TraceId.
- The workflow is a four-activity HTTP-triggered workflow (endpoint, a transform activity, a response). It can be supplied if useful.
