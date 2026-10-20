# Quickstart: proving M2

## Participant (test fixture)

The reservation participant from M1 gains FR-220 behaviour and controllable faults:

| Control | Effect |
|---|---|
| `LoseResponseAfterEffect(key)` | Applies the reservation for `key`, then fails the call as `outcome-unknown`. |
| `ApplyLate(key)` | Holds the reservation request for `key` and applies it on demand, after a cancel has run. |
| `FailCancel(key, times)` | The cancel for `key` faults `times` times, then succeeds. |
| Tombstones | A cancel for an absent `targetOperationKey` records a tombstone. A later reserve for that key is refused. |

Final-state assertions read the participant's persisted file. They never read workflow state.

## Journeys

| # | Journey | Expected participant state | Expected scope |
|---|---|---|---|
| J1 | Reserve inventory ✓, reserve delivery loses its response with the effect applied, confirm never runs | no reservations | `Compensated`; delivery entry `Compensated`, forward outcome `Uncertain` |
| J2 | As J1, but the delivery effect arrives late, after its cancel | no reservations; the late request was refused | as J1 |
| J3 | Confirm faults; cancel delivery fails twice, then succeeds (`handlerMaxAttempts` 3) | no reservations; one cancel effect | `Compensated`; 3 attempts recorded |
| J4 | As J3, but the cancel fails every time | delivery still reserved; inventory released | `AwaitingIntervention`; delivery entry `Failed` with 3 attempts; blocking incident with `WaitForIntervention`; run `Running`; cause names *Confirm order* |
| J5 | J4, then `RetryFaultedActivity/1` on the last cancel attempt, submitted twice with the same key and once with a new key | no reservations; one cancel effect from the retry | `Compensated`; 4 attempts recorded |
| J6 | An uncertain step without a handler | the effect is as the participant left it | `AwaitingIntervention`; entry `Uncertain`; never compensated |

## Restart injection points (FR-216)

Each runs in Durable, Balanced and Ephemeral:

1. After the delivery effect, before the lost response's fault commits (J1).
2. After a failed handler attempt commits, before the next attempt runs (J3).
3. While `AwaitingIntervention` (J4).
4. After the retry alteration commits, before the successor completes (J5).

## Negative controls (SC-204)

| Remove | Expected failure |
|---|---|
| The scope-assigned `runtime.operationKey` on handler retries | J3 records two or three cancel effects |
| The `outcome-unknown` branch, so the step is treated as a plain fault | J1 leaks the delivery reservation |
| The intervention request, so the default strategy acts | J4's run faults |
| Incident resolution in the retry alteration | J5's second submission schedules a second successor, or the retry is rejected |

## Run locally

```bash
dotnet test Essentials/tests/Activities/Compensation/Tests
dotnet test Essentials/tests/Workflows/Runtime/Tests --filter RetryFaultedActivity
dotnet test Essentials/tests/Workbench/Tests --filter Compensation
cd Studio/src/essentials/Elsa.Studio.Workflows/Client && npx vitest run compensation
```
