# Studio settlement display and pending-scope isolation (FR-012, FR-014)

Part of [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650). Test-only Playwright journeys against an owned Studio/Workbench pair with BPMN hosting enabled in the disposable copy. Production sources are unchanged.

Harness: `tools/bpmn/run_owned_settlement_scope_studio.py` starts the pair, seeds a second administrator (`operator`), and runs `Studio/tests/live/bpmn-settlement-scope-journey.spec.ts`. Setup waits for the committed backend switcher and `?backend=` (not `networkidle`, `main` alone, or fixture-only `data-committed-backend-url`). Adding a personal backend waits for the Add backend heading and only touches Authentication when that select is actually rendered.

## FR-012 interruption and settlement truth

| Slice | Result | Test |
|---|---|---|
| Studio "awaiting settlement" while a native parent remains owed | Actual | `shows Cancelled · awaiting settlement while a native parent remains owed` |
| Which end event a timeout reached | **Product gap** | `timeout path reports Completed without labelling the timed-out end event` |

The awaiting-settlement journey publishes a human process, wraps it in a real `RunBpmn` parent, starts the parent, then cancels the child while the existing test-only `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` hold keeps parent resume owed. Studio shows `Cancelled · awaiting settlement` and `Native parent: Pending`. Releasing the hold lets settlement complete to `Cancelled` without that suffix. Production hosts never set the hold.

The timeout journey publishes the same format-6 duration-boundary fixture used by the claims driver (`PT8S`). After the timer wins, Studio shows `Completed` / no outstanding work. It does **not** name the `timed-out` / `Timed out` end event. That matches the earlier operations note; this PR does not add production labelling. Tracked by [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650); flip the journey assertions when a label ships.

## FR-014 scope changes

| Slice | Result | Test |
|---|---|---|
| Account switch while a start is in flight | Actual | `does not apply a pending start after an account switch` |
| Backend switch while a start is in flight | Actual | `does not apply a pending start after a backend switch` |

A real `POST /bpmn/executions` is paused in the browser until Studio shows the unconfirmed retained start.

- Account switch: sign out, then sign in as `operator`, then release the paused `POST /bpmn/executions` and wait until that request **finished or failed**. Sign-out aborts the in-flight start (the page unmounts the command); a late `Accepted` therefore cannot land on `operator`. The command receipt is also scoped to `[profile.id, baseUrl, subject, tenantId]`, so a surviving response would not apply to the new subject. The new session shows neither `Last start: accepted` nor the unconfirmed retained start.
- Backend switch while that POST is in flight: Studio refuses with `Wait for backend changes to finish before switching.` and stays on `Default backend` (`?backend=legacy`). The unconfirmed start remains on the original connection. After the start settles, connecting a personal profile that reuses the same Workbench URL (different `profile.id`) does not show `Last start: accepted` in the new scope.

`data-committed-backend-url` is fixture-only and is not present on the real host.

## Spec status

Spec 1184 stays **In progress**. SC-005 tenant/Studio halves remain deferred (#4107). T038 is not done.

Receipts from a local owned-host run land in the `--output` directory (`receipt.json`, host logs, Playwright log). They are not archived here as whole stores.
