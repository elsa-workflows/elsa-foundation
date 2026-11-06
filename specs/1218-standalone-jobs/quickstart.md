# Jobs validation quickstart

The standalone Core/runtime and SQLite/PostgreSQL EF authority are implemented; their complete first
release is still in progress. The commands below run the actual qualification host. Default scheduling and its current-source process qualification are in progress; native/BPMN adapters,
operations API/Studio and complete failure/security qualification remain open.

## Run the packaged standalone witness

Commit the source and harness first, then freeze both input commits. From the repository root:

```sh
python3 tools/jobs/verify-packaged-jobs.py \
  --source-ref "$(git rev-parse HEAD)" \
  --publish-state-ref "$(git rev-parse origin/publish-state)" \
  --evidence-dir /tmp/elsa-jobs-package-evidence \
  --provider sqlite --stage execute
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
