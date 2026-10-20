# Data Model: Publish Event resumes a waiting Event without faulting

All types live in `Essentials/src/Activities/Primitives/Activities/Event.cs`.

## EventReceived (trigger, changed)

The value delivered with a matching event: the event's payload. It used to describe the event (`EventName`).

| Member | Type | Notes |
|---|---|---|
| `Payload` | `JsonElement?` | The delivered payload, unchanged. `null` when the delivery carried no payload or JSON `null`. |

- **JSON shape**: the payload itself. Reading accepts any JSON token, and `null` gives `new EventReceived()`.
  Writing emits `Payload` raw, or `null`. The round-trip is exact.
- **Canonical alias**: unchanged, because it is the same CLR type name and namespace. Registrations parked
  before the change stay valid.
- **Removed**: `EventName`. The waited name lives in `EventWaitState` (unchanged).

## EventResult (result, extended)

| Output key | Path | Type | Required | Meaning |
|---|---|---|---|---|
| `Result` | `eventName` | `string` | yes | Unchanged: the name the Event waited on or was started by. |
| `Payload` | `payload` | `JsonElement?` | no | New: the delivered payload, or null. |

- Resume: `new EventResult(state.EventName, trigger.Payload)`.
- Start-trigger delivery: `new EventResult(EventName, trigger?.Payload)`. A start without a payload or a direct
  invocation gives `Payload = null`.

## EventWaitState (private state, unchanged)

`EventWaitState(string EventName)`.

## Elsa 3 member mapping (changed)

| Elsa 3 member | Before | After |
|---|---|---|
| `Event.Result` (payload) | R4 Lost: captured → unconvertible | R1 `CarryOutput("Result", "Payload")` |
