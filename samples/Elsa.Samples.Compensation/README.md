# Elsa.Samples.Compensation

A sample package for `Elsa.Workbench` that shows compensation scopes undoing work in an external system safely. It
contributes:

- `SampleCompensation`, a CShells feature that depends on `ActivitiesCompensation`.
- **Reserve** and **Release**, two activities that call a small file-backed reservation participant.
- Three workflows under `workflows/`: a reservation saga, a nested scope with a Confirm, and a recovery that waits for an
  operator.

The [compensation guide](../../website/docs/essentials/compensation.md) explains the concepts. This README is the
walkthrough. `Essentials/tests/Workbench/Tests/WorkbenchCompensationSampleTests.cs` runs these same steps on every pull
request, so what follows is what the Workbench does.

## The participant

A compensation scope can repeat a call: after a crash, on an automatic retry, or when an operator retries. It is safe only
because the system it calls follows the participant contract in the guide's
[What a participant must do](../../website/docs/essentials/compensation.md#what-a-participant-must-do). The sample's
participant (`Ledger/ReservationLedger.cs`) is a reference implementation:

| Rule | What the sample does |
|---|---|
| Apply a step's effect at most once per operation key | **Reserve** records the reservation under `OperationKey`; the same key again returns the original reservation. |
| Undo the effect for the compensation's target key | **Release** finds the reservation that `TargetOperationKey` made and marks it released. |
| Refuse an effect that arrives after its undo | When no reservation has that target key yet, **Release** records a tombstone, and a later **Reserve** with the key fails `reservation.refused`. |
| Treat a repeated undo as done | **Release** records itself under its own `OperationKey`; the same key again changes nothing. |
| Report an uncertain outcome only when it cannot tell | **Reserve** with **Lose response** applies the reservation, then faults with the category `outcome-unknown`. |

Both activities keep the default `External` side-effect profile, so the runtime commits before each runs and publication
raises no replay-safe warning for them.

| Activity | Inputs |
|---|---|
| **Reserve** | **Resource** (required): the ledger file to write. **Reject**: refuse the request with no effect (`reservation.rejected`). **Lose response**: apply, then report an unknown outcome. Output **Reservation**: the reservation id, such as `inventory-1`. |
| **Release** | **Resource** (required). **Reservation**: the id the step returned; the workflows bind it to the step's result, and a target that made a different reservation fails `reservation.mismatch`. It is optional, because an uncertain step returns no result: the target operation key is enough. **Fail times**: how many calls with one operation key fail (`reservation.unavailable`) before one works. |

The ledger is one JSON file per resource in the feature's **Directory** (default `compensation-sample`, resolved against
the host's working directory), replaced atomically on every change. `inventory.json` after the saga below, with ids shortened and timestamps left out:

```json
{
  "resource": "inventory",
  "reservations": [
    { "reservationId": "inventory-1", "operationKey": "…", "workflowExecutionId": "…", "status": "Released", "releasedBy": "…" }
  ],
  "releases": [
    { "operationKey": "…", "targetOperationKey": "…", "reservationId": "inventory-1", "result": "Released" }
  ],
  "tombstones": [],
  "failedReleaseAttempts": {}
}
```

## Build and drop

```bash
dotnet pack samples/Elsa.Samples.Compensation/Elsa.Samples.Compensation.csproj \
  -c Release \
  -o Essentials/apps/Elsa.Workbench/packages
```

Enable the feature on the default shell by adding it to the `Features` section of the Workbench's `shells.json`, with a
directory for the ledger:

```json
"SampleCompensation": { "Directory": "/tmp/elsa-compensation-sample" }
```

Scheduled retries run at the runtime's outbox sweep, every 10 seconds by default; to see the third workflow's waits
closer to their 5 and 10 seconds, also set `"WorkflowsRuntimeResumption": { "SweepIntervalSeconds": 1 }`. Then start
the Workbench, or reload the shell (`POST /_admin/shells/reload/default` with the `X-Elsa-Module-Management-Key` header,
as for the [Nuplane activities sample](../Elsa.Samples.Nuplane.Activities/README.md)).

## Import the workflows

Each file in `workflows/` is a body for `POST design/workflows/definitions/submit`. Activity version ids differ per
catalog, so each node names its activity by type key in `activityVersionId`, and the import replaces it with the
catalog's version id. With `curl` and `jq`, against the Workbench's development admin account:

```bash
WB=http://localhost:5095
curl -s -c cookies.txt -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Password123!"}' "$WB/_elsa/identity/login" > /dev/null

versions='{}'
for type in Elsa.Activities.Compensation.Activities.CompensationScope Elsa.Activities.Compensation.Activities.Confirm \
            Elsa.Samples.Compensation.Activities.Reserve Elsa.Samples.Compensation.Activities.Release; do
  id=$(curl -s -b cookies.txt "$WB/design/activities/definitions?search=$type" |
    jq -r --arg type "$type" '.items[] | select(.definition.activityTypeKey == $type) | .definition.headVersionId')
  versions=$(jq --arg type "$type" --arg id "$id" '. + {($type): $id}' <<< "$versions")
done

for file in samples/Elsa.Samples.Compensation/workflows/*.json; do
  version=$(jq --argjson versions "$versions" \
      'walk(if type == "object" and has("activityVersionId") then .activityVersionId = $versions[.activityVersionId] else . end)' "$file" |
    curl -s -b cookies.txt -H 'Content-Type: application/json' -d @- "$WB/design/workflows/definitions/submit" | jq -r '.version.id')
  echo "$file: version $version"
  curl -s -b cookies.txt -H 'Content-Type: application/json' -d '{}' "$WB/publishing/workflows/$version/publish" | jq '{artifactId, sourceReferenceId}'
done
```

Run a published workflow with its two ids, and keep the run id it returns:

```bash
curl -s -b cookies.txt -H 'Content-Type: application/json' -d '{"sourceReferenceId":"<sourceReferenceId>"}' \
  "$WB/runtime/workflows/executables/<artifactId>/execute" | jq -r '.workflowExecutionId'
```

Each workflow appears in Studio under **Workflows** as a definition you can open and edit like any other. To inspect a
run, open `/workflows/instances/<run id>` in Studio and select the scope's **Steps**, as the guide's
[Inspect a run](../../website/docs/essentials/compensation.md#inspect-a-run) describes.

## What to expect

### S1: reservation saga (`reservation-saga.json`)

Scope **order**: *Reserve inventory* ↔ *Release inventory*, *Reserve delivery* ↔ *Release delivery*, *Confirm order*
(a **Reserve** with **Reject** on, and no compensation). Each **Release** binds **Reservation** to its step's result.

- **Run:** the order is rejected, so the scope undoes the two completed steps, newest first, and finishes `Compensated`.
  The run completes.
- **Ledger:** `delivery.json` and `inventory.json` each hold one reservation, now `Released`, and one release carrying
  that reservation's id; the delivery's release is the earlier of the two. `order.json` is never written: a rejected
  request has no effect.
- **Studio:** the scope reads `Compensated`, with "Confirm order" named as the failure that started recovery; *Release
  delivery* is the 1st compensation and *Release inventory* the 2nd.

### S2: nested scope with Confirm (`nested-confirm.json`)

Scope **order**: *Charge* ↔ *Refund*, *Confirm charge* (a **Confirm**), *Reserve stock* (an inner scope **stock** with
*Reserve A* ↔ *Release A* and *Reserve B* ↔ *Release B*), *Ship* (rejected). The Confirm comes before the inner scope
because it confirms everything the scope could undo so far, handed-over entries included.

- **Run:** the inner scope finishes `Done` and hands its two reservations to the outer scope. *Ship* is rejected, so the
  outer scope undoes what it can: the handed-over reservations, B then A. The charge is confirmed, so *Refund* never runs.
  The outer scope finishes `Compensated`.
- **Ledger:** `payment.json` keeps its reservation `Reserved` and has no release; `stock-a.json` and `stock-b.json` each
  hold one released reservation, B's release first; `shipping.json` is never written.
- **Studio:** *Charge* reads `Confirmed`, with a link to *Confirm charge*. **Handed over by inner scopes** lists B and
  then A as `Compensated`, each linking to the inner scope occurrence. The inner scope's own view shows its steps
  `Compensated` by the outer scope.

### S3: operator recovery (`operator-recovery.json`)

The saga again, with **Fail times** 99 on *Release delivery*, and the scope's **Handler max attempts** 3 and
**Handler retry delay** 5 seconds (the multiplier keeps its default, 2).

- **Run:** *Release delivery* fails, waits 5 seconds, fails again, waits 10 seconds and fails a third time. The scope then
  waits for an operator: the incident on *Release delivery* stays open with "wait for intervention", and the run keeps
  running. Each wait is a minimum: the retry runs at the first outbox sweep after it is due.
- **Ledger:** both reservations are still `Reserved`. `delivery.json` lists, under `failedReleaseAttempts`, the time of
  each of the 3 failed attempts for the release's one operation key: at least 5 and then 10 seconds apart.
- **Studio:** while it waits between attempts, the *Release delivery* row reads **Retry scheduled** with the attempt and
  the time after which it runs, and offers no action. After the third failure it reads **Needs action** with
  "Attempt 3 of 3", and offers **Retry** and **Resolve…**; the scope's header offers **Abandon recovery…**.

Then act on it, in Studio or over the API, as the guide's
[Act on a waiting recovery](../../website/docs/essentials/compensation.md#act-on-a-waiting-recovery) describes:

| Action | Then |
|---|---|
| **Resolve…**, **Effect absent or undone** (`compensation.resolve.effect-absent`) | *Release delivery* reads `Resolved: effect absent`; recovery carries on and releases the inventory; the scope finishes `Compensated`. The delivery reservation stays in the ledger: the operator said it was undone elsewhere. |
| **Retry** | *Release delivery* runs once more, with the same operation key, and fails again (Fail times is 99), so the scope waits again. |
| **Abandon recovery…** | No further compensation runs; both reservations stay; the scope faults with `compensation.scope.abandoned` and the run faults. |

The Workbench test resolves S3 as effect absent over REST and checks the outcome above.
