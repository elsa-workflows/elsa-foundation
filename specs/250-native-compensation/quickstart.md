# Quickstart and proof plan: M1 slice

## The reservation participant

The sample module `Elsa.Samples.Reservations` (under `Extensions/Samples.Reservations`) provides a participant
that persists independently of the workflow database: a JSON file per host, written atomically.

| Activity | Effect | Idempotency |
|---|---|---|
| `ReserveInventory(orderId, sku, quantity)` | Creates reservation `inv-…`; output `reservationId` | Keyed by invocation id: a repeat returns the existing reservation |
| `ReleaseInventory(reservationId)` | Marks it released | A repeat of an already released reservation is a no-op |
| `ReserveDelivery(orderId, slot)` / `CancelDelivery(reservationId)` | Same pattern | Same |
| `ConfirmOrder(orderId, reject)` | Faults with `order.rejected` when `reject` is true | n/a |

Failure injection (test hosts only, gated by the `Reservations:FailureInjection` setting): a "crash plan" file
names an activity and a point (`afterEffect`). When that point is reached, the host process terminates with
`Environment.FailFast` before the activity returns, so the effect is written and its completion is not
committed.

## Injection points (SC-002)

| # | Point | Expected after restart |
|---|---|---|
| P1 | `ReserveDelivery` after effect | Delivery reservation exists once; recovery after `ConfirmOrder` fails releases it once |
| P2 | `CancelDelivery` after effect | Cancelled once; `ReleaseInventory` then runs once |
| P3 | Between `CancelDelivery` completion and `ReleaseInventory` scheduling (kill when `ReleaseInventory` is claimed) | `ReleaseInventory` runs once |
| P4 | `ReleaseInventory` after effect | Released once; scope completes `Compensated` |

Each point runs in Durable, Balanced and Ephemeral modes.

## Proof layers

1. **Unit** (`Essentials/tests/Activities/Compensation/…`): structure handler round trips, state transitions,
   repeat-delivery guard, ordering, validator messages.
2. **Runtime integration** (same test project): an in-process host over a file-backed SQLite database. Each
   injection point is simulated by disposing the host at the point and building a new host over the same
   files. Runs in all three modes.
3. **Backend e2e** (`e2e-tests/compensation/Test-CompensationRestart.ps1`): starts its own Workbench and kills it
   with the crash plan at P2. Restarts it and asserts the participant file and the inspection projection over
   REST.
4. **Browser** (`Studio/tests/live/compensation-journey.spec.ts`, opt-in, real backend): authors the workflow
   in Studio from scratch, publishes, runs it, stops and restarts the backend at P2, and inspects the result. It
   includes the axe check on the designer and the run view.
5. **Studio component tests** (Vitest): paired list authoring, keyboard paths and every inspection state.

## Negative controls (SC-003)

Each control is a test that must fail when the named safeguard is removed:

- absorption not requested → the workflow faults instead of compensating;
- entry marked `Compensating` after scheduling instead of in the same commit → P3 restart runs the handler twice
  (detected by the participant's attempt counter);
- repeat-completion guard removed → a duplicated completion advances the cursor twice;
- handler resolved from the current artifact instead of the pinned one → the republish-mid-recovery test runs
  the new handler.
