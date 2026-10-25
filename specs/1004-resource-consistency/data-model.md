# Data and UI State Model: Studio resource-list consistency

This feature changes presentation and local UI state only. It introduces no persisted entity, endpoint, schema, or backend transition.

## Secret inventory item

| Field | Meaning | Presentation rule |
|---|---|---|
| `name` | Stable technical identifier | May be shown and copied as the technical name |
| `displayName` | Optional operator-friendly label | May be shown; never substitutes for the copied technical name |
| `typeName` | Secret type identifier | Shown as metadata |
| `storeName` | Storage descriptor identifier | Shown as metadata |
| `scope` | Optional selection/filter value | Facets describe loaded records; a selected scope absent from loaded results remains explicitly qualified |
| `status` | Existing lifecycle state | Shown using the existing status presentation |
| `createdAt`, `updatedAt` | Existing metadata timestamps | Shown where available |
| secret material | Credential-bearing value | Never displayed, copied, placed in fixture metadata, or written to evidence by this work |

The endpoint returns a loaded page and a total count. One page is limited to 100 items. Server search can locate results outside the initially loaded page; local facets operate only over the loaded records. Coverage wording must agree with that limit and the displayed counts.

## Connection inventory item

| Field | Meaning | Presentation rule |
|---|---|---|
| `name` | Exact technical identifier used for returned-item selection | Select the preferred created item only when an exact name appears in returned `items` |
| descriptor/type | Describes the connection configuration kind | Preserve the existing descriptor-specific details |
| local status | Elsa-side connection status | Preserve the existing list/details presentation; do not imply remote status guarantees |

The current list response supplies `items`; this feature does not add pagination or a total-count claim. The built-in HTTP Test provides local readiness only, and Revoke changes Elsa-side status only.

## Search state

| State | Meaning | Transitions |
|---|---|---|
| Draft query | Text currently edited by the operator | Changes on input; does not redefine results until accepted |
| Accepted query | Query represented by the latest accepted list context | Set on Search/Enter; used by refresh, successful operations, and Connection creation reload |
| Failed attempted query | Query whose most recent load failed | Retained for retry; retry uses this attempted value |

Secrets must preserve its existing distinctions. Connections must preserve its accepted query after create and after subsequent successful or failed operations.

## Home task destination

The Needs attention destination is the existing active Runs URL `/workflows/instances?incidentHealth=active`. When authenticated, effective `workflows/runtime:read` controls whether the destination is offered. When authentication is disabled, current behavior remains. Continue is separate: it represents the complete authorized definition catalog and its counts and is not restricted by this new link gate.

## Fixture attempt counter

The fixture counter measures attempted POST, PUT, and DELETE operations. A failed validation/handler still counts because the request was attempted. It is a visible test signal only; it does not retain or display payload, header, secret, or credential values.
