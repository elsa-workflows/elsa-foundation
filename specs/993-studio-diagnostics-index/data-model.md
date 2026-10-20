# UI State Model: Studio Diagnostics Index

This feature introduces no persisted domain entities. The model below describes only existing navigation contributions and transient page state as presented to the user. The Diagnostics index is a frontend navigation projection; backend shell composition is a separate input to destination behavior and does not by itself determine whether a Studio module is registered.

## Diagnostics index projection

| Item | Source | User-visible meaning | Boundary |
|---|---|---|---|
| Diagnostic navigation entry | Existing registered navigation entries in the Diagnostics grouping, after the host's feature, role, and permission filtering | A destination the current app and user can navigate to | Its label, route, ordering, and access gates remain those of the contribution. Known built-ins receive local task descriptions. A custom contribution without task semantics receives neutral navigation wording. |
| Empty link state | The filtered Diagnostics entry set has no entries | No diagnostic tool links are available for this app or access context | It does not mean backend outage or an empty telemetry result. It coexists with any diagnostic widgets. |
| OpenTelemetry task description | Known built-in entry presentation | Trace inspection is supported; Metrics and OpenTelemetry Logs are unavailable in this experience | Does not claim backend readiness or data availability. |
| Application-log handoff | Existing Structured Logs entry, when the surface can confirm it is registered and accessible | A truthful link to Structured Logs | If the surface cannot confirm both conditions, any handoff uses neutral Diagnostics navigation and makes no availability claim. |
| Runtime evidence settings entry | Existing workflow settings navigation | Opens the existing Runtime evidence settings page | Keeps its exact current route, view permission, save permission, policy and save behavior. |

The index projection is derived from the same host-filtered navigation used by the existing navigation surfaces and receives that filtered list from the existing `App.tsx` render path. It does not perform its own authorization check, call destination APIs, or add a backend-health status.

## Destination state

Destination pages retain their existing transient request and result states. Tests must check what is rendered, not only helper classifications.

| Destination | Existing user-observable states to preserve | State interpretation |
|---|---|---|
| OpenTelemetry traces | Loading; successful empty result; successful trace result; permission response (401/403); unavailable/missing destination backend (including 404 where currently represented); other failure | A failed request remains distinguishable from a successful empty result. Unsupported Metrics and OpenTelemetry Logs are explained on this page before entry into placeholder content. |
| Structured Logs | Loading; empty results; permission response; unavailable/missing backend; other failure | The destination owns and explains its state. Registration alone does not imply a working log backend. |
| Runtime evidence settings | Readable values with view access; existing save-enabled state for authorized writers; view-only state with write controls disabled | Read access continues to permit discovery and viewing. The existing write authorization remains authoritative; attempted edits in view-only mode do not issue a save request. |

Exact status labels and error presentation should follow the current destination behavior except where the approved spec requires unsupported OpenTelemetry views to be explained before entry. No state is added to a shared capability or health model.

## Contributed diagnostic widgets

Widgets remain independent contributors, not tool registrations or health signals. Existing widget content and loading, ready, streaming, and error states continue unchanged. A failure in one widget does not hide another widget or the index's destination links. Widget count is not used to infer backend readiness or an empty tool list.

## Persistence and contracts

There is no new persistence, domain record, API, SDK shape, or contribution metadata. Capture-policy persistence and payload capture remain as they are today. Navigation and widget contracts are consumed as-is.
