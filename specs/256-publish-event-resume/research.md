# Research: Publish Event resumes a waiting Event without faulting

## R-1 Where the fault comes from

**Finding**: `PublishStimulusExecutor` routes `StimulusDispatchRequest(input: payload.Payload)`. The resume-bookmark
handler (`WorkflowResumeBookmarkSchedulerWorkHandler`, the trigger-delivery block around line 673) stores that input
as the trigger payload, typed with the registration's payload type (`EventReceived`). It stores an explicit null when
there is no input. `StatefulActivity.ResumeAsync` then runs `TriggerPayload.Deserialize<EventReceived>()` with the
default options:

- JSON `null` gives a null record, so the call throws "The validated activity trigger resolved to null."
- An object such as `{ "orderId": "1" }` deserializes fine, because unknown properties are ignored. A string,
  number or array payload, including a JSON-encoded string literal, throws `JsonException`, which surfaces as
  "does not match the declared stateful activity contract".

The start path has the same exposure. `ActivityStartContext.Create` deserializes a targeted start trigger's payload
as `EventReceived`, so a start via Publish Event with a non-object payload throws "The targeted start trigger
payload does not match". A start without a payload is fine, because a null start payload means "no trigger".

`POST runtime/workflows/stimuli` with `{ "eventName": ... }` works only because that object happens to deserialize
into the record.

## R-2 Fix in Event, by making `EventReceived` accept any JSON

**Decision**: `EventReceived` becomes the event's delivered payload: `sealed record EventReceived(JsonElement? Payload
= null)`. A private `JsonConverter<EventReceived>` reads any JSON token (null, object, array, string, number,
boolean) as `Payload`. It writes `Payload` back raw, or `null` when there is none. `HandleNull => true`, so a JSON
`null` produces `new EventReceived()` instead of a null reference. The converter is attached with
`[JsonConverter(...)]` on the type, so every `Deserialize<EventReceived>()` uses it, including the resume path, the
start path and `PersistableActivityValue.Materialize`, with no options to pass.

**Rationale**:
- No change to the runtime contracts (FR-006). The router, the resume handler, the outbox and Publish Event stay as
  they are.
- The trigger type's canonical alias is derived from the CLR type, and the type keeps its name and namespace, so
  the alias does not change. Registrations parked before the change still pass the alias check in
  `ValidatePortableType` and the `SameType` check in the resume handler (FR-005). Their stored payload type is the
  same alias, and any stored payload is valid JSON, which the new converter accepts.
- The snapshot round-trip (`SnapshotAndMaterialize`) is exact: the converter writes `Payload` raw and reads it back
  raw.
- `JsonElement` and `JsonElement?` are scalars for `PersistableActivityValue.ValidateType`, so the type stays
  persistable.

**Alternatives considered**:
- *Build the trigger from the routed stimulus and send the payload in its own channel.* This means splitting trigger
  from input in `StimulusDispatchRequest` and the resume-bookmark command, which are shared by every stimulus
  provider. The owner declined it on 2026-10-20 for its blast radius.
- *Change the trigger type to `JsonElement`.* This changes the canonical alias, so every parked Event wait would fail
  the `SameType` check after an upgrade. It also leaves BPMN and test callers with an anonymous trigger type.
  Rejected.
- *Keep `EventReceived(string? EventName)` and only add `HandleNull`.* This fixes the null case but still faults on
  scalar and array payloads, and drops the payload. Rejected.
- *Make `StatefulActivity` tolerate a null trigger in general.* This weakens a runtime contract shared by every
  stateful activity. Rejected under "fix in Event".

## R-3 The event name no longer travels in the trigger

**Decision**: Remove `EventReceived.EventName`. The waited name is already in `EventWaitState.EventName` and is what
`ResumeAsync` reports. A start completes with the activity's own `EventName` input.

**Rationale**: Nothing reads the trigger's `EventName` today (`ResumeAsync` uses the state). Keeping it would make
an input such as `{ "EventName": "x" }` ambiguous: the event's name, or a payload? The spec's edge case says it is
a payload.

**Consequence**: About 40 test call sites build `new EventReceived("x")` to resume a wait, in the BPMN tests,
`PrimitiveDrives` and `GraphBoundaryCompleteDurableCancellationTests`. Each becomes
`JsonSerializer.SerializeToElement(new { eventName = "x" })`, the same shape the stimulus API sends. Only the arrange
lines change, and no assertion changes. The three `EventTriggerStimulusProviderTests` that construct the record
directly switch to `new EventReceived()` or `new EventReceived(payload)`.

## R-4 The payload as an Event output

**Decision**: `EventResult` gains `[Output(Key = "Payload", Path = "payload", IsRequired = false)] JsonElement?
Payload`. The existing `Result` output (event name, path `eventName`) keeps its key, path and meaning (FR-003).
Resume completes with `new EventResult(state.EventName, trigger.Payload)`. A start completes with
`new EventResult(EventName, trigger?.Payload)`.

**Rationale**: This follows the `HttpEndpointResult.ParsedContent` precedent: an optional `JsonElement?` output
with `IsRequired = false`. The name `Payload` matches Publish Event's `Payload` input, so the pair reads
symmetrically in the designer.

**Alternative considered**: putting the payload on `Result`, as in Elsa 3. Rejected, because it would change the
meaning of an existing Elsa 4 output key that published workflows may already capture.

## R-5 The Elsa 3 map: Event `Result` is carried

**Decision**: In `Elsa3BuiltInActivityMap.Entries.cs`, replace `Lost("Result")` with
`CarryOutput("Result", "Payload")`. Amend spec 236's `member-map.md` row "Event.Result (payload)" to R1 → `Payload`,
citing this spec and the owner's 2026-10-20 decision.

**Rationale**: Elsa 3's Event `Result` is the event payload, both when the event starts the workflow and when it
resumes a wait. `Payload` now carries the same value, so the conversion is faithful (the spec 248 principle: faithful
or refuse). Fixtures that expect the Lost diagnostic, in the Elsa 3 mapping tests and the import integration
fixture `reusable-waits-event.json`, are updated to match.

## R-6 Paths that keep working unchanged

- **Stimulus API**: its `input` is now the payload. `{ "eventName": ... }` still resumes, and it shows up on the
  `Payload` output.
- **Test-run "Deliver event"** (`EventTestRunWaitDriver`): it already sends `payload` as the stimulus input. Today
  an empty or non-object payload faults, and after the fix every shape completes.
- **BPMN catch events**: they reuse Event with `CanStartWorkflow = false`. Only the test arrange lines change
  (R-3).
- **Start trigger extraction and hashing** (`EventTriggerStimulusProvider`, `EventStimulus`): they are keyed by
  event name and untouched.
- **Correlation and local publish**: they are applied by the router before delivery, so this change does not
  touch them.

## R-7 Tests

**Decision**:

- Unit tests next to the existing `EventTriggerStimulusProviderTests` (`Essentials/tests/Activities/Runtime`):
  - the converter reads null, object, array, string, number and boolean as a payload;
  - `EventReceived` round-trips exactly through `Snapshot`/`Materialize`;
  - resume through `IStatefulActivity.ResumeAsync(ActivityResumeRequest)`, the request-level path that faulted, with
    a JSON null and with a scalar payload;
  - a start completing with a payload.
- End-to-end tests: Publish Event → Event resume, with and without a payload, in a regular workflow and in a graph
  activity body. Each asserts the wait completes with no incident and that `Payload` holds the published value. The
  hosts and fixtures come from the existing Publish Event and spec 246 graph-body tests (see plan.md, Project
  Structure).
- The Elsa 3 conversion test for Event `Result` is carried.
