# Contract: Event activity trigger and outputs

## Trigger delivery (any path: Publish Event, stimulus API, test-run "Deliver event")

The stimulus `input` is the event payload. It may be absent, JSON `null`, or any JSON value. Every one of these
resumes a matching mid-flow Event wait, or completes a targeted Event start, without a fault.

| Delivered input | Event completes | `Payload` output |
|---|---|---|
| none / `null` | yes | `null` |
| `{ "orderId": "1" }` | yes | `{ "orderId": "1" }` |
| `[1, 2]`, `"text"`, `42`, `true` | yes | the same value |
| `{ "eventName": "order.approved" }` (stimulus API habit) | yes | that object |

The routing identity is unchanged: stimulus type `Event`, with the hash from the event name only. Correlation and
local-event scoping are applied before delivery and do not change.

## Outputs

| Key | Path | Type | Required | Value |
|---|---|---|---|---|
| `Result` | `eventName` | string | yes | The name the Event waited on or was started by (unchanged) |
| `Payload` | `payload` | JSON value | no | The delivered payload, or null (new) |

## Elsa 3 conversion

Elsa 3 `Event.Result` (the payload) converts to the Elsa 4 `Payload` output (R1). It is no longer Lost.
