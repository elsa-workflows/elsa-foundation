# Quickstart and proof plan: M1 slice

## The reservation participant

Two participants stand in for an external system. Both apply each effect once per invocation id, as an
idempotent participant does, and journal every call, so a test can tell a repeated call from a repeated effect.

- **In-process** (`Essentials/tests/Activities/Compensation/Tests/Support/Participant.cs`): one shared instance
  outlives both host generations of a restart test. `Rejecting(node)` faults a step with `order.rejected`;
  `KillingHostAfterEffectOf(node)` and `KillingHostBeforeEffectOf(node)` stop the host at an injection point.
- **Owned process** (`Essentials/tests/Workbench/Fixtures/Reservations`, the `ReservationParticipant` activity in a
  fixture feed package): inputs `DirectoryPath`, `Key`, `Reject`, `HoldAfterEffect` and `Undoes`. It appends each
  call to `{Key}.calls` (process id, invocation id, attempt id), applies its effect once per invocation id
  (`{Key}.{invocationId}.effect`, `effects.log`), records the reservation it was asked to undo in `{Key}.undoes`, and
  returns the reservation id `{Key}:{invocationId}`. With `HoldAfterEffect` it waits after the effect until
  `{Key}.release` exists; the test kills the Workbench process during that hold.

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
3. **Backend e2e** (`Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs`): publishes and runs
   the journey over REST on its own Workbench process, kills it at P2 and restarts it. Asserts the participant's
   files (each effect once, the killed handler re-called with the same invocation id, each handler undoing its own
   step's reservation through a step-result reference and a captured variable) and the run over REST.
4. **Browser** (`Studio/tests/live/compensation-journey.spec.ts`, opt-in with `ELSA_STUDIO_URL`, real backend):
   authors the workflow in Studio from scratch, publishes, runs it and inspects the result, with axe on the designer
   and the run view. Restart is layer 3's job. The mocked suite (`Studio/tests/browser/compensation-scope.spec.ts`)
   covers keyboard authoring, inline problems and both views at phone and desktop widths in both themes.
5. **Studio component tests** (Vitest): paired list authoring, keyboard paths and every inspection state.

## Negative controls (SC-003)

Each control is a test that must fail when the named safeguard is removed:

- absorption not requested → the workflow faults instead of compensating;
- entry marked `Compensating` after scheduling instead of in the same commit → P3 restart runs the handler twice
  (detected by the participant's attempt counter);
- repeat-completion guard removed → a duplicated completion advances the cursor twice
  (`CompensationScopeRepeatDeliveryTests`; removing either the step or the handler guard fails its test);
- handler resolved from the current artifact instead of the pinned one → the republish-mid-recovery test runs
  the new handler (`A_republish_mid_recovery_keeps_the_handlers_of_the_pinned_executable`). The scope reads its
  handlers from the executable node the runtime hands it, so there is no current-artifact lookup to mutate; the test
  guards the runtime's pinning instead.
