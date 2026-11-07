# Standalone Jobs runtime

`AddElsaJobs` registers the ordinary scoped `IJobClient`; set `JobsOptions.EnableWorker` and explicit
trusted `WorkerScopes` to run an independent worker. Configure exactly one `IJobCommands` and
`IJobQueries` provider addressing the same durable authority. The provider owns every acceptance,
pause, lease, capacity, retry and result transition. This module requires no workflow, Studio,
ASP.NET or mutable design data.

`AddJobHandler<THandler,TInput,TResult>` contributes a scoped typed handler under an explicit
`JobHandlerSupport`. Duplicate logical handler/version contributions are refused. A worker advertises
the exact input/result schemas and implementation identity, then rechecks its claimed request and
provider-selected implementation pin before physical entry. The first start grant pins deployment
implementation identity; later attempts require matching installed support. Hosts own that identity;
use a fingerprint of the actual semantic implementation rather than an unversioned display name.
It is deployment compatibility evidence, not publisher authentication.

Options and serializer configuration are copied during registration. Handlers receive their exact
trusted `JobScope`, JobId, AttemptId and a stable external-effect idempotency key hashing the complete
partition/tenant/JobId tuple (including distinct null tenant) under `elsa.jobs.invocation.v1:`. JSON
input/result envelopes default to a maximum of 1 MiB each, including schema and the fixed durable JSON
escaping. `MaximumPayloadBytes` may lower that ceiling; it cannot raise it. Runtime and provider share
`JobPayloadLimits.GetByteCount` and a complete-payload nesting ceiling of 32 so valid handler results fit
durable completion's surrounding envelope. Serialization failures retain their original inner cause
behind a safe domain error; messages persisted by the worker remain fixed safe summaries. An oversized result
becomes a small nonretryable failure receipt; it is not repeatedly submitted as an unretainable result.
Complete progress envelopes default to a maximum of 16 KiB; `MaximumProgressBytes` may only lower it.
Progress messages/data must already be safe for permitted
inspection; handler authors must not include credentials or protected raw input. The runtime retains
fixed safe diagnostics for handler/serialization failures and never logs raw exceptions or payloads.
The optional [Secrets adapter](../Secrets/README.md) adds explicit declared-reference and literal-disclosure policies.

The worker uses one slot per physical attempt, at most `MaximumConcurrentAttempts` (1–64). Polling
rotates explicit scopes and named queues. Each slot retains one exact pull `RequestId` across
unavailable or uncertain claim responses; it clears that identity only after a definitive empty/held/
capacity response or recognition of the granted lease. Parallel slots have independent pull identities.
Missing, unavailable or mismatching retained request/lease proof refuses physical entry without
fabricating a handler failure. Each attempt owns a service scope; renewal and progress
use separate command scopes and serialize ownership-sensitive commands. At most one progress report
may be in flight; its short I/O timeout cannot occupy the heartbeat's lease-renewal slack. Provider renewal supplies
authoritative cancellation/absolute-deadline signals; the host clock only schedules renewal/polling
and timestamps physical reports. Unknown renewal responses retry the same original lease/duration
under bounded I/O/reconciliation time; only confirmed renewal replaces the local token. Unresolved
authority requests cooperative cancellation. The local slot
remains occupied until the actual handler exits, even if a successor runs elsewhere. Lease expiry
does not establish physical exclusion or exactly-once external effects.

The default `IJobRecurrenceCalculator` uses the pure `UtcRecurrence` arithmetic in `Elsa.Tasks.Schedules`.
Intervals use its positive ISO-duration/invariant-TimeSpan rules; cron uses five fields or an optional
seconds field in UTC. Each calculation advances strictly after the supplied cursor without consulting
a host clock. A valid recurrence with no representable next instant returns null. Invalid expressions
produce `jobs.recurrence.invalid` with the original cause. Register a replacement before `AddElsaJobs`
or explicitly replace its descriptor; startup requires exactly one calculator.

An enabled worker also runs one schedule-materialization loop by default. `MaterializeSchedules=false`
disables that loop, while producer-only registration starts neither background loop. The materializer
rotates the same trusted scopes in fresh command scopes, independently of physical handler slots.
It requests at most `MaximumOccurrencesPerMaterialization` (default 32, range 1–1000), then waits
`MaterializationInterval` (default 250ms, range 10ms–1min) after every call, including arithmetic-only
progress. `MaterializationTimeout` bounds cooperative provider I/O (default 30s, range 100ms–10min).
The provider owns pause arbitration, occurrence/origin identities, accepted jobs, catch-up/replay and
cursor commits. An uncertain response resumes that bounded provider drain; the host never recreates
or resubmits an occurrence job. Malformed retained schedules and temporary calculator unavailability
remain provider-owned inspection/recovery concerns; the host emits fixed safe diagnostics.

The same worker is an `IHostedService` in an ordinary host and an `IBackgroundTask` under the optional
`JobsFeature` (Tasks lifecycle). It starts once per service provider. Plain hosting waits for application
startup; shell activation already follows its Prepare-phase migrations. Activation cancellation does
not own an activated worker. A shared configuration validator is both an ordinary hosted service and
a shell `IStartupTask`; Tasks awaits startup tasks before starting background tasks, so missing or
duplicate authorities/calculators fail activation before polling. Stopping immediately halts claims
and materialization, renews existing attempts through the configured
grace period, then requests cooperative stopping and waits for actual exit. A caller's shutdown token
can stop waiting without proving exit. Cancellation-ignoring code retains its scope and slot.

Normal completion asks the provider to select a result with the current fence and one physical
receipt. Uncertain acknowledgement retries retain that receipt identity. Once authority is lost,
the worker only appends exact late physical evidence; it cannot replace a selected result or release
successor-owned permits. Handler exceptions are retryable by default; unreadable input/unretainable
results are non-retryable. The accepted provider policy remains the sole retry/budget authority.

This project implements standalone execution and the default schedule adapter/materialization loop.
Optional native/BPMN, Secrets and operations adapters have separate source and qualification gates;
their presence does not qualify a provider/deployment or the complete #3586 product. See the [Core catalog](../Core/EXTENSION_POINTS.md) and
[specification](../../../../specs/1218-standalone-jobs/spec.md).

## Cross-domain contributions

The optional shell composition contributes `JobsConfigurationValidator` as `IStartupTask` and
`JobsWorker` as `IBackgroundTask` to `Elsa.Tasks.Core`. Validation runs at startup before background
work. The worker polls at `JobsOptions.PollInterval` (default 250ms) and uses the same singleton
instance as plain .NET hosting. Neither contribution publishes or handles a Tasks event. See the
[Tasks catalog](../../../../Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md) and
[Jobs contract catalog](../Core/EXTENSION_POINTS.md).
