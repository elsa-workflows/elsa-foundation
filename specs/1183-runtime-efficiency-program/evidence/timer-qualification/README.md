# Actual timer-resume qualification

A bounded Runtime Efficiency #2531 / #1307 profile: one warmed actual durable-timer sweep resumes independent native
workflows through the ordinary provider scopes, bookmark dispatcher and mailbox. Every workflow is
`Sequence(Delay, ten ReplaySafe no-op leaves)`. The real typed Delay contract/target is linked from the existing
restart/crash tests. No dispatcher, actor, provider query or business I/O delay is substituted.

Setup publishes once, fully completes one warm timer, then suspends 1–100 fresh executions with actual timers and
bookmarks. The injected clock advances only before a sweep so all timers are due. This measures draining an existing
backlog, not timer deadline accuracy. The timed sweep excludes setup, validation and a second empty sweep. Compare
explicit degrees 1–8 on equal fresh fixtures; the production default remains one. Durable/Balanced/Ephemeral and
SQLite/PostgreSQL are selectable. PostgreSQL uses only the protected `ELSA_EFFICIENCY_POSTGRES_CONNECTION`, creates a
fresh task-owned database, and never serializes its connection. Existing output directories are refused.

Warmup and measured populations must fully settle. Every target must complete with exactly twelve activity rows and
ten probe calls, zero timers/bookmarks/queue/incidents/undelivered outbox, and unchanged workflow/activity bytes after
a second empty sweep. Bookmark identities are checked in their provider's raw representation, other runtime rows in
their actual encoded representation. CPU, allocation, peak memory and actual sweep duration are reported separately.
The profile is one node with a named provider; it does not prove clustered timer ownership or multi-host capacity.

Four-target SQLite correctness smokes pass at degrees one and four, including complete warmup and the second-sweep
checks. Shared-machine timing is excluded. Earlier attempts are retained: the first used a global-namespace probe
that ordinary type discovery did not register; the next used encoded IDs against the raw bookmark projection.
Both were fixture defects, corrected before timing qualification. No timing CI gate, benchmark service or default
concurrency increase is introduced.
