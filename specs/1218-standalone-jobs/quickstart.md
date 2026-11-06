# Jobs validation quickstart

The first-release source includes standalone execution, schedules, native/BPMN adapters and operations
API/Studio. Delivery remains incomplete: M1/M2 receipts qualify their recorded sources, while the final
integration, security, retention, UX and resulting-main gates are still open. See the
[acceptance matrix](acceptance-matrix.md) for the evidence boundary.

## Compose a producer and worker

Use exact computed package versions from the chosen feed. The qualification command below creates
an owned temporary feed; this feature has not published a release. A standalone host needs
`Elsa.Jobs` and its selected persistence adapter. Follow the
[typed report implementation](../../tools/jobs/PackageProbe/Program.cs) and
[runtime registration guide](../../Extensions/Jobs/src/Elsa.Jobs/README.md) for complete
consumer source rather than adding workflow or Studio dependencies to the worker.

Register `AddJobsEntityFrameworkCore` against the same durable database in producer and worker hosts.
Use matching partition and tenant identities in the producer's `JobScope` and the worker's
`WorkerScopes`. Configure the selected provider's migrations through the existing
[persistence policy](../../Extensions/Jobs/src/Persistence/EntityFrameworkCore/README.md). Register
`AddElsaJobs` in both: leave `EnableWorker=false` in the producer; enable it in the worker, add explicit
trusted `WorkerScopes` and select the named queues it processes. Register each typed handler through
`AddJobHandler<THandler,TInput,TResult>` with its exact logical handler/version, input/result schemas
and semantic implementation identity.

Resolve `IJobClient` in a service scope and call `SubmitAsync(jobScope, new JobSubmission(key, request))`.
Keep that original producer key and immutable request after a lost response. The returned
`JobAcceptance` contains the canonical JobId and minimum recognition deadline. A fresh
`IJobQueries.FindAsync(jobScope, jobId)` reads the same retained invocation. Check its lifecycle and
`UnavailableDiagnostic`; a missing result is not completion. After configured cleanup,
`ResultPayloadState == JobPayloadState.Expired` exposes the original `ResultMetadata` instead of a
fabricated payload. Do not keep polling an expired result as though its handler were still running.

Choose [declared Secrets binding](../../Extensions/Jobs/src/Secrets/README.md) when a handler needs a
secret reference or protected scalar input. Accepted input stores the reference. The worker resolves
it through the exact accepted tenant and the scoped `IJobSecrets` contract. The
[Connections composition controls](../../Extensions/Jobs/tests/Secrets/Tests/README.md) show the
explicit tenant and declared-reference contract for an existing connection resolver.

## Operate and integrate

An operations host adds `JobsApiFeature`/`AddJobsApi`, current Foundation authentication and
authorization, and the same Jobs authority. Enable `JobsStudio` in the Studio shell for its Jobs
navigation. Use the [API routes and recovery rules](../../Extensions/Jobs/src/Api/README.md) for
submission, schedules, holds, cancellation, linked retry and audit. Retain an operation's original
ID, revision and reason when reconciling an unconfirmed response. Release the addressed hold and
inspect the other effective reasons before treating work as eligible.

Add the optional [native adapter](../../Extensions/Jobs/src/Workflows/Runtime/README.md), its Design
pin contributor and, for genuine BPMN, [Jobs BPMN integration](../../Extensions/Jobs/src/Bpmn/README.md)
to the model/coordinator host. `RunJob` awaits a retained result. `SubmitJob` uses an explicit
Independent or Detached pin and returns a `PendingAdmission` submission receipt; only canonical Jobs
acceptance proves admission. Keep handler execution in the separate worker if the coordinator should
not execute business code.

Payload retention defaults to indefinite. Optional `TerminalInputRetention`/`TerminalResultRetention`
are captured at acceptance; changing host settings does not shorten an accepted promise. An owned
maintenance process calls bounded `IJobCommands.ExpirePayloadsAsync`, continuing its returned cursor.
Owed model delivery blocks expiry. See the persistence guide for cleanup, retained identities and
migration limits; key recognition and payload expiry are separate operations.

## Run the packaged standalone witness

Commit the source and harness first, then freeze both input commits. From the repository root:

```sh
python3 tools/jobs/verify-packaged-jobs.py \
  --source-ref "$(git rev-parse HEAD)" \
  --publish-state-ref "$(git rev-parse origin/publish-state)" \
  --evidence-dir /tmp/elsa-jobs-package-evidence \
   --provider sqlite --stage execute --witness-group all
```

Repeat with `--provider postgresql` to use an owned PostgreSQL container. Both commands create an
isolated clean source clone, compute and pack its standalone closure, restore/publish
an external PackageReference application with a fresh cache, and launch separate producer, worker
and inspector processes. The SQLite host explicitly carries the source's reviewed native bundle pin;
it does not inherit ambient central package settings. The receipt records actual package versions,
archive/assembly hashes, engine versions, commands, cases and cleanup. No package feed is published.

The useful report result has two line items, five units and a subtotal of 52.00. The focused cases
exercise equal/conflicting producer keys, deliberately discarded producer response, missing-handler
preservation, result inspection across fresh processes, pause/start ordering, killed-worker recovery
and stale completion. These cases do not establish every row below. A failed profile remains failed.
See [the qualification host instructions](../../tools/jobs/PackageProbe/README.md) for exact proof limits.

## Complete the remaining product journey

1. Pack the changed packages locally through the repository's computed-version/build-slot route;
   create a clean temporary application and a separate worker using only PackageReference inputs.
   Record package hashes, versions and complete transitive dependency closure. No workflow/Studio
   packages may appear in the standalone consumer/worker closure.
2. Register one versioned Generate Report handler with durable request/result schema. Submit one
   immediate and one delayed request; kill/restart producer and worker around lost acceptance/result
   acknowledgements, then inspect the same accepted JobIds and typed results within their advertised
   recognition window. Test expired recognition cleanup separately; pending responsibility extends protection.
3. Create a UTC recurring schedule. Exercise one catch-up default and explicit replay-every policy;
   retain occurrence identity and every already accepted job during pause/outage/edit recovery.
4. Run independent PostgreSQL worker processes and a separate producer. Exercise all four pause scopes,
   overlap/resume, queued retries, start-permission cutoff, handover and unrelated shared-queue progress.
   Repeat local restart checks with SQLite. Name every unqualified engine/deployment explicitly.
5. Invoke the same registered handler through an actual native saved wait and a genuine BPMN model.
   Finish physical work while instance/definition holds apply; inspect retained ResultId, resume and
   prove exact model consumption without another physical attempt. Include parent-cancel-first and
   delivery acknowledgement loss/restart controls.
6. Exercise automatic backoff, timeout/cancellation races, ignored cooperative cancellation, stale
   owners/late receipts, authorized Retry as new run, missing old handler versions, result retention
   and downstream idempotency controls. Separate logical authority from actual physical stopping.
7. Compose API/Studio and run [UX acceptance](ux-acceptance.md) with real tenants/permissions and
   downloadable current-head artifacts. Independently inspect the full affected experience.
8. Run affected whole projects with test discovery/execution agreement, required architecture/maps/
   restore checks, independent review and exact-head CI. Merge through current org rules; rerun the
   qualifying consumers/journeys on resulting main and reconcile [every criterion](acceptance-matrix.md).

Use temporary owned hosts/stores. The feature delivery does not authorize package release, production
credentials or shared/production deployment. No row is passed merely because its commands were drafted.
