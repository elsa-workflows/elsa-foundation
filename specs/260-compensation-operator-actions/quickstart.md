# Quickstart: proving M4

All backend journeys use the shared reservation participant from spec 251 (operation-key deduplication, tombstones, `FailCancel(key, times)`) and assert its persisted file, never workflow state alone. Unless a row says otherwise, each runs in Durable, Balanced and Ephemeral.

## Shape

The order workflow from spec 251, with `handlerMaxAttempts` 3:

```
Scope "Order": [ Reserve inventory ↔ Release inventory,
                 Reserve delivery  ↔ Cancel delivery,
                 Confirm order (fails) ]
```

`W` below means: drive the run until *Cancel delivery* fails three times; the scope waits with entry `e2` `Failed` and incident `inc-w` offering the three decisions.

## Journeys

| ID | Setup | Action | Expected participant | Expected scope and evidence |
|---|---|---|---|---|
| O1 | `W`, then make the cancel succeed | `RetryFaultedActivity/1` on the last attempt | no reservations; one cancel effect | `Compensated`; `e2` 4 attempts; incident `OperatorRetry` with submitter, reason, job and plan |
| O2 | An uncertain *Reserve delivery* with no handler (spec 251 J6) | `DecideIntervention/1` `effect-absent` | inventory released; delivery as the participant left it | entry `ResolvedManually/EffectAbsent`; earlier entries undone; `Compensated`; incident `OperatorDecision` |
| O2h | `W` | `effect-absent` | inventory released; delivery still reserved (the operator said it was undone elsewhere) | `e2` `ResolvedManually/EffectAbsent`; recovery continued; `Compensated` |
| O3 | Same as O2 | `effect-accepted` | delivery kept; inventory kept | entry `ResolvedManually/EffectAccepted`; scope ran *Confirm order* next (here it fails: recovery undoes inventory only, never delivery) |
| O3h | `W` | `effect-accepted` | inventory released; delivery kept | `e2` `ResolvedManually/EffectAccepted`; `Compensated` |
| O4 | `W` | `compensation.abandon` | inventory and delivery still reserved | phase `Abandoned`; scope faulted `compensation.scope.abandoned` (`outcome-unknown`); run `Faulted` under the default strategy; journal readable |
| O4n | Outer [P ↔ undo P, inner scope (= `W` shape), X] | abandon the inner scope | P undone after the outer decision | outer journals the inner scope step `Uncertain`, waits (no handler) offering the decisions; resolving it `effect-absent` undoes P |
| O4d | `W` with *Cancel delivery* inside a Sequence (descendant fault) | resolve `effect-absent` | as O2h | the suspended Sequence is cancelled; `Compensated` |
| O5 | `W`; submit O2h's resolve, then a second resolve and a retry with new keys | — | as O2h, once | second and third jobs `Failed` with `InterventionNotPending` and `NoBlockingIncident`; activity, incident and scope state unchanged by them |
| O5r | `W`; submit the same resolve twice with the same key | — | as O2h, once | second submission `Replayed`, same plan id |
| O5c | `W` inside an outer scope; a Compensate cancels the inner scope | retry, then resolve | nothing new happens | `SourceParentNotRunning`, `RequesterNotRunning`; no handler started after the cancellation |
| O5t | `W`; cancel the run | resolve | — | `WorkflowTerminal` |
| O5x | `W` | resolve with a blank reason; with 501 characters; with a code not offered | — | `InvalidDecideInterventionPayload` ×2, `DecisionNotOffered`; nothing changed |
| O6 | `W`; caller has read and inspect but not manage | read run details; submit a resolve | — | `permissions.canManage: false`; 403; no plan created |
| O7 | `W` in tenant A; caller in tenant B with manage | read run, journal, incidents; submit a resolve by id | — | 404 on reads; plan job `TargetNotFound`; nothing changed |
| O7a | `W` in a run started by an HTTP endpoint, and in a Studio test run | retry and resolve as an operator of the tenant | as O1, O2h | accepted (FR-406) |
| O8 | `W` | resolve; kill at each injection point below | as O2h | applied exactly once after restart; history lists it once |
| J1 | Each phase: running, compensating, waiting, cancelled, abandoned, completed | read the journal | — | fields per [contract](contracts/journal-and-permissions.md); completed returns `StateCleared`; without the values grant the cause message is withheld |

## Restart injection points (O8)

1. After the plan is accepted, before its job runs.
2. After the alteration checkpoint commits, before the decision is delivered.
3. After the decision evaluation commits, before the next handler runs.

## Workbench owned process

O1 + O2h + O4 over REST against the real Workbench process, with a process kill at injection point 2 for O2h. Each run is started as a Studio test run (execution authority `workflow-designer-test-run`), so FR-406 is proven on a run that before this PR could not be altered. Runs started by HTTP endpoints are covered by the FR-406 capture tests (T407), not by this journey. Extends `Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs`.

## Live browser journey (real backend)

`Studio/tests/live/operator-actions-journey.spec.ts`, beside `compensation-journey.spec.ts`, against a running Studio and Workbench (`ELSA_STUDIO_URL`):

1. Sign in as an operator with manage. Author and publish the order workflow, with *Cancel delivery* as a `Fault` activity (always fails), and start it with the designer's **Test Run**.
2. Open the run: the scope reads "Needs action", the row "Needs action (1st) · Attempt 3 of 3", the banner names *Confirm order*. The journal panel says "From the scope's journal".
3. **Retry** with a reason: the dialog shows target and effect; after polling the row reads "Needs action (1st) · Attempt 4 of 3" (the handler still fails). History lists the retry.
4. **Resolve** "Effect absent or undone" with a reason, and reload while Studio follows the plan (step 7): the row reads "Resolved: effect absent"; *Release inventory* runs; the scope reads "Compensated". History lists both actions.
5. Run it again; open it in two tabs; **Retry** in the first. Then **Resolve** in the second: it shows "Someone else already acted on this" and the view refreshes. (A resolve in the first tab would finish the recovery and end the run, and the second tab would be told the run has ended.)
6. On that run, waiting again, **Abandon** with a reason: the scope reads "Abandoned", the rows "Not undone", the run "Faulted".
7. Reload mid-poll: the same run reopens and the action completes without a second submission.
8. Sign in as a read-only user: no action buttons; the journal and history are visible; a direct submission to the alteration endpoint returns 403. This step runs when `ELSA_STUDIO_READONLY_USER` and `ELSA_STUDIO_READONLY_PASSWORD` name such a user (the Workbench seeds only an administrator).
9. Run axe on each view and dialog at 1200 and 390 px.

## Mocked browser suite

`Studio/tests/browser/compensation-scope.spec.ts` gains fixtures for every new state, each dialog, the error messages, the read-only view and the derived fallback, with axe at both widths and both themes.

## Negative controls (SC-404, run by hand, reverted)

| Remove | Must fail |
|---|---|
| The `InterventionNotPending` check | O5 (the second resolve applies) |
| The offered-decision check | O5x (`DecisionNotOffered`) |
| The requester-running check | O5c (a decision reaches a cancelled scope's state) |
| `SourceParentNotRunning` | O5c (a handler starts after cancellation) |
| The validator allowance for `OperatorDecision` | O2h (the checkpoint is refused) |
| The abandon fault (complete instead) | O4 (the run completes) |
| Studio's `canManage` gate | the read-only Vitest and live step 8 |
| The cause-message gate | J1 without the values grant |

### Results for PR 1 (20 October 2026)

Each safeguard was removed by hand, `Elsa.Activities.Compensation.Tests` rebuilt and the named journeys run in Durable, Balanced and Ephemeral; every change was reverted afterwards (`git checkout`), and the full suites pass again.

| Safeguard removed | Where | Result |
|---|---|---|
| `InterventionNotPending` check | `DecideInterventionAlterationHandler` | O5 failed in all 3 modes (the stale decision was no longer refused as `InterventionNotPending`) |
| Offered-decision check | `DecideInterventionAlterationHandler` | O5x failed in all 3 modes |
| Requester-running check | `DecideInterventionAlterationHandler` | O5c failed in all 3 modes |
| `SourceParentNotRunning` | `RetryFaultedActivityAlterationHandler` | O5c failed in all 3 modes |
| Validator allowance for `OperatorDecision` | `IncidentStateTransitionValidator` | O2h failed in all 3 modes |
| Abandon fault (complete instead) | `CompensationScope.OnAbandoned` | O4 failed in all 3 modes |
| Cause-message gate | `CompensationJournalProjector` | J1's projector test `The_cause_message_is_shown_only_with_the_values_grant(false)` failed; the J1 journey checks only the `messageWithheld` flag |
| Studio's `canManage` gate | Studio (PR 2) | See below |

### Results for PR 2 (20 October 2026)

| Safeguard removed | Where | Result |
|---|---|---|
| Studio's `canManage` gate (`canAct` from the alteration links only) | `workflow-editor/useOperatorActions.tsx` | The Vitest `offers Retry and each offered decision in the incident dialog, only when the caller may manage the run` failed, and the mocked browser tests `a reader who may not manage the run sees the journal and history but no action` and `shows a refusal and a missing permission as messages, and changes nothing` failed. Live step 8 was not run: no user without manage permission exists on the Workbench. Reverted with `git checkout`; the suites pass again. |

## Run locally

```bash
dotnet test Essentials/tests/Workflows/Runtime/Tests --filter "DecideIntervention|RetryFaultedActivity|IncidentStateTransition|AlterationTargetCapture"
dotnet test Essentials/tests/Workflows/Runtime/Api/Tests --filter "ActivityStateView|OperatorAccess|Alteration"
dotnet test Essentials/tests/Activities/Runtime/Tests --filter InterventionDecision
dotnet test Essentials/tests/Activities/Compensation/Tests
dotnet test Essentials/tests/Workbench/Tests --filter Compensation
cd Studio/src/essentials/Elsa.Studio.Workflows/Client && npx vitest run compensation operatorAction alterations runSelection
cd Studio && pnpm test:browser -- compensation-scope.spec.ts
cd Studio && ELSA_STUDIO_URL=http://localhost:5089 npx playwright test --config tests/live/playwright.live.config.ts operator-actions-journey.spec.ts
```
