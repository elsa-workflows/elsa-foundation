# Standalone trigger qualification guide

**Status**: Planned qualification. No command/result below is a claim of feature acceptance. Run after implementing the corresponding tasks, using the committed candidate and owned temporary hosts/stores.

## Preparation

Confirm the #3582 claim/PR and actual branch/head. Check for an open `main is red: CI` issue before assuming a healthy base. Read [contracts](contracts/) and the [UX acceptance inventory](ux-acceptance.md). Preserve existing hosts/stores and other worktrees.

```bash
git status --short --branch
git rev-parse HEAD
uptime
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
```

Use the normal `dotnet` command on PATH. A build-slot wait is a queue, not a hang; do not bypass it. Do not run heavy suite builds concurrently. Compare timing failures with the machine core count/load and repeat at lower load before calling them defects.

## Affected project verification

For each changed project, enumerate tests then run the complete project and record executed versus listed counts. The main affected projects are:

```bash
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --list-tests
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Http/Tests/Elsa.Workflows.Runtime.Http.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj
```

Select further whole suites from the actual final diff (HTTP activities, Primitives, Publishing, Expressions or distributed providers). Do not count a filtered helper as whole-suite acceptance. Meaningful revert/mutation controls prove changed behavior fails without its implementation; retain original failure and corrected pass separately.

Provider tests and the full tier must exercise the actual required relational engines/modes with no silent container skip. Record unavailable engines explicitly and use required CI/provider evidence before claiming support. In-memory proves process-local behavior only. Public module consumers must use independently compiled contracts and real composition; an internal test constructor is insufficient.

## Real composed backend matrix

Use disposable scoped data to publish native Sequence/Flowchart and genuine supported BPMN models with declared inputs. Keep their immutable graph/content bytes before resource operations for comparison.

1. Create/reload a paused HTTP, Event, Timer and Cron resource. Save mappings and grant target authority through normal authorized APIs. Verify API access/read-only/source-owned/cross-tenant controls and capabilities.
2. Prove one Event and one actual due schedule each start two independent workflows. Start one unchanged workflow from two resource families with intended inputs. Include embedded starts, local Events and bookmark-resumption controls.
3. Add/edit/remove the same association from trigger and workflow surfaces; reject stale revisions and compare persisted results. Confirm no graph/draft mutation and that refused live changes preserve configuration/claim/schedule.
4. Follow a slot through compatible and incompatible publication replacement, unpublication and restoration. Observe fresh eligibility separately from accepted old captures. Revoke resource grant and remove creator membership as separate controls: revocation stops new admission; creator departure does not.
5. Send real HTTP requests for workflow-owned response, RequireAll async and AcceptAvailable async. Cover mixed/zero acceptance, malformed/oversized/mapped invalid input, unavailable store/targets, duplicate/conflicting keys, endpoint conflicts both ways and paused route reservation. Observe exact response instruction, timeout/disconnect and peer-worker completion separately.
6. Interrupt execution at source-before-commit, source-after-commit, recipient-before/after dispatch seal, queue handoff/ack loss and first-checkpoint successor. Restart actual processes and compete two workers. Inspect stable source/recipient/command/execution IDs and recover only unfinished recipients.
7. Race cancel-pending against the durable dispatch seal. A cancellation winner prevents dispatch; a commitment winner remains owed and reports commitment without claiming queued/running. Reconcile actual queue/model receipt and first state before reporting target acceptance.
8. Exercise actual interval/Cron occurrence delivery, overdue catch-up, intentional pause/resume, update/timezone change, backward/forward clock and DST cases. Distinguish controlled calculator/fault tests from an actual due source firing.
9. Qualify genuine BPMN declared input committed state and service-worker consumption/restart through its ordinary model protocol. Native wrappers are separate controls. Unsupported entries/profiles/workflow-response combinations refuse visibly.
10. Collect/delete with unresolved accepted work and retained dedup facts. Confirm exact material/source/capture survives until the documented safe handoff/retention boundary; deletion is not implicit cancellation.

Retain source/admission evidence, each recipient phase, linked ordinary execution status and transport outcomes. Counts and screenshots alone cannot prove restart, multi-node or absence of duplicate execution.

## Actual Studio experience

Extend the existing runner's finite journey/capture inventory before this run. It refuses a dirty checkout and owns its child hosts/ports/disposable stores.

```bash
pnpm --dir Studio typecheck
pnpm --dir Studio check:bundle:workflows
python3 tools/ux-acceptance/run.py --output /tmp/standalone-triggers-ux-UNIQUE-RUN
```

Choose a new output path for every run; do not reuse or overwrite failed evidence. The existing runner uses the normal development seed without requiring another identity/credential product. See [UX acceptance](ux-acceptance.md) for resource/association/input/mode/policy/delivery/permission states and named captures.

Open the actual screenshots, accessibility snapshots and traces. Independent inspection covers desktop/narrow/light/dark and the additional changed-surface checks named in that record. Retain keyboard/focus, empty/populated, validation/conflict/recovery, source-owned and denied states. Link current-commit downloadable CI artifacts; local paths alone are not shared acceptance evidence.

## Repository and delivery gates

```bash
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check
```

If ProjectReferences change, run the required locked solution restore. Review public API deltas, module catalogs and migration/upcast paths. Keep the PR draft through converged independent correctness/standards/acceptance review, with current-head receipts. Mark ready and request normal review so required checks run on the final head. Follow current org merge rules without bypassing protection or human holds.

After merge, record the actual resulting-main SHA/CI and repeat the applicable composed source/Studio qualification. Advance the spec lifecycle in the finishing implementation PR. Close #3582 only after the full accepted feature matrix is delivered; a design packet, partial milestone or unmerged branch is incomplete.
