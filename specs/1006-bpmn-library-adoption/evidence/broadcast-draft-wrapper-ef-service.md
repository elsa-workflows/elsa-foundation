# SQLite Test Run service recovery

Source parent `ee475bfaf` (full commit in the [machine receipt](broadcast-draft-wrapper-ef-service.json)).
**This qualifies Design/material/source/receipt service preparation and retry through fresh SQLite contexts.
The compiler is fixed; dispatch records and fails without executing a workflow. Test-scope, workflow-execution
and activity-execution stores remain in-memory. No process-kill, upgraded-host, complete Runtime execution-state,
Studio or other relational-provider recovery is established. T049 stays open for old-writer/quiescence and
qualified default cohort GC; saved broadcast publication remains disabled.**

Each attempt uses the actual Test Run service and separate SQLite Design, Runtime and Publishing receipt
databases. Design is seeded through its EF command and read through its real stores. The Runtime template,
artifact, source-reference and lease stores and the Publishing receipt store are real EF adapters. Every retry
and verification opens new contexts. Source-write cuts identify the actual wrapper reference by decoding its
stored UTF-16 source kind, rather than searching the encoded JSON for a plain source-kind string.

The five new controls cover:

- Failure before source SaveChanges, after native fence SQL has run inside the owned transaction. Source
  insertion and transactional fencing roll back; the receipt remains Preparing and dispatch is untouched.
- Failure from actual TransactionCommitted after the source commits. Fresh contexts observe the persisted
  source while the receipt still says Preparing. Retry preserves its exact payload bytes and incarnation.
- An actual ordinary collector pass with zero creation grace between either cut and fresh retry. Without
  source acceptance it collects the orphan wrapper; retry recreates material and publishes a live source.
  After committed acceptance it retains the wrapper and template. The collector explicitly reports no
  stimulus-delivery retention support; this does not activate or qualify default cohort collection.
- A separate later run after the older run expires, preserving current stored metadata/descriptor provenance
  and distinct wrapper identities; and peer retirement after committed acknowledgement loss, which retry
  refuses to restore or dispatch.
- Source expiry after actual source SaveChanges but before commit. The owned transaction rolls back, no
  wrapper source remains, and retry cannot extend the expired receipt's fixed source deadline.

The first two cut cases include the ordinary collector assertions; the remaining cases cover later-run,
retirement and expiry behavior. These are five executed cases, not five bullets plus two more cases.

Final restored Publishing EF suite: **176 passed, zero failed/skipped** (171 existing plus five new cases).
Discovery and TRX reconcile exactly. Three valid behavioral mutations are caught: replacing the service's
fenced handoff with raw source Save, removing the post-SQL/precommit liveness check, and rewriting the winning
source payload during adoption. Each fails a behavioral assertion; no compile-error mutation is counted, and
all production source bytes are restored. Generated-filter validation passes; API/schema and the frozen
authoring fingerprint remain unchanged. Other suites' earlier candidate results remain historical evidence;
this test/catalog increment does not restamp them as new executions.

Three independent final component source/catalog/receipt reviews are recorded separately from full-PR review
rounds, still zero. This increment changes controls and catalog/checkpoint documentation only. Actual current
host/fresh-provider execution remains T051 work. Old-writer exclusion and qualified default cohort composition,
T050 exact bookmark/immutable-input activation, other stories, duplicate-semantics deletion/guards, final
host/Studio, full-PR review/exact-head checks/resulting-main and ADR0063 remain open. #2898/#2907/program
production acceptance stays open; Orleans/task-runner remain deferred. No legacy execution-checkpoint writer
is deleted here.
