# Durable messaging validation guide

**Status: PROSPECTIVE — NOT RUN.** The inbox, consumer and message-specific journeys described here are planned, not delivered. Existing build commands do not establish message support. Execute and update this guide against the delivered implementation before claiming a runnable quickstart or closing [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583).

Read the [specification](spec.md), [delivery design](contracts/delivery-design.md), [submission/API contract](contracts/submission-api.md), [implementation plan](plan.md) and [acceptance matrix](acceptance-matrix.md) together. Q1–Q3 are selected: durable acceptance receipt with delivery separately observable; acceptance-time eligible definitions only; ready matching waits before starts. Provider/schema/API details remain engineering contracts until implemented and qualified.

## Prepare an owned environment

Use .NET 10 selected by the repository's `global.json`, the pinned Studio dependencies and PowerShell as documented in the [backend source quickstart](../../docs/contributing/backend-source-quickstart.md) and [e2e guide](../../e2e-tests/README.md). Docker or an explicitly owned disposable database is needed for each native provider being qualified. Never point a destructive crash/restart/cleanup journey at a personal or shared store.

From the repository root, record source identity and machine load before building:

```bash
git rev-parse HEAD
git status --short
dotnet --version
uptime
```

Use a clean committed candidate for final evidence. Record source SHA, PR head, configuration, physical providers/store boundaries, persistence mode, model, worker count and selected test names. Do not print connection strings, bearer tokens or raw payloads in evidence. Use fresh uniquely named output directories.

The build-slot wrapper on `PATH` owns `dotnet` scheduling. A waiting message means queued work; do not bypass it. Run only touched projects and the composed hosts needed for the selected journey. Timing failures at high machine load need diagnosis and a lower-load rerun, with the failed run retained separately.

## Source checks and existing host build

These project paths exist today. The new message tests must be added and named in the implementation plan; an existing green suite or a filter matching zero tests is not #3583 acceptance.

```bash
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -p:RestoreLockedMode=true
dotnet test Essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj -p:RestoreLockedMode=true
```

Select relevant new tests within these projects and the touched activities/BPMN/EF provider projects rather than running an unrelated solution build. Preserve failed, skipped, unavailable and zero-test selections. Broader required CI remains a separate gate.

The Workbench build is an input to owned composed journeys. Do not start its ordinary development profile against an existing local database for crash qualification. The planned message harness must copy configuration into its own temporary content root, allocate free loopback ports, choose disposable stores and wait for readiness from the processes it actually started.

## Intended consumer and API walkthrough

**Planned location:** `tools/messages/Consumer/Consumer.csproj`. The repository has backend executable probes under `tools/bpmn/`; no `Essentials/samples/` convention currently exists. Use a documented public-package consumer here, with source-probe and package-consumer evidence distinguished. This proposed project/path must be created and reconciled with the final plan before use.

The following is a **planned command shape**, not a currently executable recipe. Final implementation must document supported arguments, authentication and owned-host composition without embedding credentials:

```bash
# PLANNED: project and command interface do not exist yet.
dotnet run --project tools/messages/Consumer/Consumer.csproj -- --help
```

The delivered example must perform this actual public API journey using capability-advertised operations from the submission contract:

1. Authenticate into the intended tenant/producer namespace. Discover message capability and effective policy; refuse an unsupported durable composition.
2. Publish eligible Fulfillment and Accounting message interests. Include a receive-only definition with no open wait or message start. Submit `PaymentReceived`, business key `order-123`, a stable producer ID and a harmless payload before its receive opens.
3. Record the acceptance receipt and effective independent buffer/deduplication deadlines. Acceptance is not delivery or workflow completion. Query authorized overall and per-definition outcomes rather than assuming immediate completion.
4. Lose/retry a response using identical ID/content; obtain the original acceptance and preserve every pending definition. Reject changed content under the same live deduplication address without modifying it.
5. Reach the later receive, then observe committed consumption for that selected recipient. An unrelated key receives nothing; different eligible definitions progress independently. A newly introduced logical definition cannot consume this historical message; an eligible replacement version can supply its definition's still-pending recipient.
6. Exercise a ready wait competing with a published start. Select the wait by matching address/order before compatibility; an incompatible selected wait fails visibly without falling through to another wait/start. A later distinct message can use a later eligible recipient.
7. Submit with omitted lifetime, an explicit override and immediate-only. Immediate-only captures/reserves ready recipients at admission; an unmatched receipt never acquires a listener opened after that cutoff, including after delayed work/restart. Selected delivery remains owed after zero buffer lifetime.

Do not infer exactly-once external effects from these observations. The example must print safe identities/progress, not payload/secret content, and must use the current principal rather than trusting a tenant or producer claim in message data.

## Authored activities and operator journey

Through the actual composed Studio, author, save/reopen and publish message send/receive/start activities from the real catalog. Verify configured name/key, payload contract, sender lifetime and producer-ID behavior against the same API semantics. Authored send must stage through its owning checkpoint and recover committed work without calling delivery before commit.

In Messages, find the receipt, inspect acceptance and mixed per-definition outcomes, follow the selected run and diagnose pending/expired/failed delivery. Retry only an authorized recoverable selected delivery under the same identity; never reset a delivered definition or select a new recipient for its old message. Verify keyboard/focus, empty/no-match/loading/failure states and light/dark/narrow layouts.

Use separate current permissions for submission, metadata inspection, payload reveal and recovery. Test direct APIs as well as buttons: foreign tenant/producer probes, metadata-only reader, denied reveal, permission revocation and backend/tenant switching. Clear previously revealed material on scope change and retain authorized reveal audit. A secret reference does not authorize resolving its material.

## Restart, workers and provider proof

**Planned journey:** `e2e-tests/messages/Test-DurableMessaging.ps1`, using the existing [owned server lifecycle](../../e2e-tests/_ServerLifecycle.ps1). It does not exist yet. Its final interface must be documented and executed; do not invent flags or attach its restart mode to an existing listener.

The harness must own its hosts, disposable databases and allocated ports, reject occupied ports, and stop/restart only its own process handles/groups. External-server mode, if provided, may send requests but must never stop that server. Use the existing lifecycle helpers; no process takeover or port-based kill. On Windows use `powershell -NoProfile -ExecutionPolicy Bypass -File`; on macOS/Linux use `pwsh`.

For each claimed provider/topology, run the matrix against actual durable stores. Two-worker proof requires independently composed workers sharing the intended persistence boundaries. Crash at acceptance acknowledgement, winner/reservation, pre-header start admission, consumption acknowledgement and partial multi-definition progress. Recover the same chosen recipients; distinct buffered messages racing one one-shot wait leave the loser unselected for a later wait.

Include same-key concurrent starts across versions/model representations, independent scopes, authoritative terminal release, a delayed old start after proven refusal/release, stale release after successor admission, deleted-terminal recovery using retained proof and an old queued resume after reservation transfer. Exercise independent expiry/cleanup, unavailable recipients, incompatible payloads, capacity/cohort rollback and Event/signal broadcast regressions. Use the actual provider-causal controls in B01–B27, not mocked delivery or an in-memory dictionary.

Record the final supported SQLite/PostgreSQL/SQL Server/MySQL and multi-node modes explicitly. An unavailable container, skipped provider or unsupported store composition remains incomplete/refused evidence; never remove its row to manufacture qualification. Package-backed consumer execution must verify public package boundaries separately from project-reference host tests.

## Composed Studio evidence and completion

This existing runner owns disposable Workbench/Studio hosts and requires a committed checkout. The commands exist; message journeys require implementation/registration before they prove this feature:

```bash
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
# Replace with a new, unused output directory for each run.
python3 tools/ux-acceptance/run.py --output /tmp/studio-ux-messages-unique-run
```

Retain the runner's source/head/dirty report, Playwright results, screenshots, accessibility snapshots and traces. Inspect the actual captures and provide downloadable exact-run artifacts for independent UX review under [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md). A green fixture/build or author summary is not product acceptance.

**Completion: NOT RUN.** Once implemented, replace planned paths/interfaces with verified commands, execute the consumer and all agreed journeys, reconcile evidence with the acceptance matrix, resolve findings and repeat affected suites on the committed candidate. Complete current-head review/checks/merge and resulting-main qualification, then reconcile issue/spec/program/configuration/examples. Keep failed, skipped, cancelled, unavailable and incomplete results distinct from passing evidence throughout.
