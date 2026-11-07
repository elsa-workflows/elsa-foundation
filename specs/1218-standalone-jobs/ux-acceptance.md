# Jobs operations UX acceptance before implementation

Status: acceptance record defined before implementation. API/Studio/audit/secrets source now exists,
but SDK, actual composed journeys and independent visual acceptance remain open. Apply the repository's
[Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md) and
[module controls](../../Studio/docs/module-ui.md).

## User goal and reference

An operator starts in Studio Jobs, finds why a report is waiting or failed, inspects its attempt/result,
and takes a permitted recovery action. They can pause a queue/type/associated workflow, verify which
holds remain, and resume without losing accepted work or rerunning a completed report.

Use Workflow Definitions/Connections collections for filters, paging, empty states and shared action
controls. Use established collection → resource detail navigation and workspace tabs. Reuse shared
status, dialog, toast, loading and failure controls; do not introduce a scheduler-vendor dashboard or
implementation details into the operator flow.

## Proposed surface and observable states

| Surface | Normal journey | States that must be inspected |
|---|---|---|
| Jobs | Filter queue/type/status, open a run, return preserving collection context | Empty, populated, no matches, paging, long labels, loading, unavailable, forbidden |
| Job detail | See handler/version, queue, association, attempt timeline, result/progress and owed continuation | Delayed, backoff, held with all reasons, Starting, Running, cancelled/timeout requested, terminal, late receipt, result awaiting model consumption |
| Queue/type controls | Pause with reason, see durable operation outcome, resume only addressed hold | Concurrent change, another scope/actor hold, permission denied, acknowledgement lost, refresh/restart |
| Workflow association | Navigate to exact parent, inspect instance/definition holds | Result already complete while continuation held, parent cancelled/completed, independent job, missing/unavailable parent without false acknowledgement |
| Schedules | Create/edit UTC interval/cron and policy, inspect next/owed occurrences | Invalid schedule, held cursor, one catch-up/count, replay backlog, accepted historical jobs, edit/delete impact review |
| Recovery actions | Cancel or Retry as new run with reason/review, follow linked new JobId | Stale revision, duplicate submit, refused retry, failure then retry, preserved old result/history; no parent resurrection |
| Result/diagnostics | Inspect permitted bounded input/result and safe failure/progress | Redacted/no permission, unavailable old schema, expired payload with retained metadata, oversized values, safe export |

The copy must explain that Pause blocks future start permissions and allows already granted work to
finish. Show all current hold reasons; resume feedback must not claim eligibility when another hold
applies. Distinguish retained result from model consumption and cooperative stop request from physical exit.
The reviewed continuation grant cutoff is recorded in the [lifecycle contract](contracts/job-lifecycle.md).
Actual model and operator journeys must still qualify that behavior before final copy is accepted.

## Smallest disproof journeys

1. Seed real standalone jobs in an owned composed host: empty → submit → persisted collection → inspect
   Running → complete useful result → refresh and reopen. Verify queue/type filters and paging.
2. Pause a report type; submit jobs while paused; prove unrelated same-queue work completes. Add queue,
   instance and definition holds, resume one, inspect remaining reasons. Restart worker/host and repeat.
3. Run real native and genuine BPMN invocations of the same handler. Complete during workflow pause,
   inspect retained result/held continuation, resume and follow the exact parent/result without rerunning.
4. Trigger real failure/backoff, cancellation/timeout, stale owner and authorized Retry as new run. Inspect
   operation receipts, old history and the linked run. Recover from a failed/lost response by authoritative reload.
5. Create/edit a recurring schedule, prove default catch-up and explicit replay through actual storage,
   reopen persisted values and review deletion/owed occurrence impact. Preserve all accepted history.
6. Use two actual tenants and principals with different permissions. Exercise direct routes, filters,
   details, results and every mutation; denied views disclose no other tenant's identity or payload.

For changed create/edit/actions: keyboard-only activation, focus through dialog cancellation/acceptance,
validation and return navigation; light/dark themes; wide and 390px narrow layouts; large/long content.
Inspect loading, unavailable/failure and denied states with identified real server outcomes or clearly
labelled state injection. A mocked failure is component evidence, not real recovery qualification.

## Evidence and review receipt

Extend the actual composed harness under `tools/ux-acceptance/` with feature-specific jobs journeys.
Use owned disposable stores/ports/process groups; do not attach to existing listeners or shared data.
Final evidence must name a committed clean source and PR head, actual handler/model/provider/tenant setup,
screenshots, accessibility snapshots, Playwright traces, assertions and console/network failures.

Upload the current-commit evidence through the existing Actions artifact path, link the exact run and
downloadable artifact on the PR and retain its expiry. Local absolute paths alone are insufficient.
Every intended journey must execute successfully; failed, skipped, retried, unavailable and not-run
states remain visible and cannot become a passing acceptance receipt.

An independent acceptance reviewer opens the screenshots and relevant traces, compares shared references,
and inspects the composed experience. Record exact head, goal/reference, observed states, artifact,
reviewer/lens, specific findings, fixes/retest and unverified states using the normal PR UX receipt.
Do not self-merge an incomplete UX acceptance or bless new baselines to conceal a defect.
