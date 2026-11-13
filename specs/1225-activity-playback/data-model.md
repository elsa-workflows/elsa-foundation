# Presentation data model

These are planned private Studio states and a proposed read dependency. No new persisted playback aggregate or public runtime model is implemented here.

## Context

Identity combines backend/tenant/auth-session revision, workflow execution, explicit activity scope/filter and available-history segment. Any change retires in-flight reads and timer generations. Run/backend/access changes clear protected data; same-run scope/range changes retain only an eligible exact selection.

## Sequence admission

An opaque server-owned session/bound identity identifies the authorized run/scope, authoritative order, frozen upper bound and known contiguous retained segment. Admission reports supported/unavailable/denied, recording/settlement/retention coverage, optional exact count and selected anchor. Ordering/bounds use opaque or string identities; C# long values must not be coerced into unsafe JavaScript numbers.

Ordinal positions refer to a declared segment. Known counts/positions must be representable safely by the UI; otherwise ordinal scrubbing is explicitly unavailable rather than rounded. Counts do not establish recording completeness.

## Occurrence entry

Exact execution/run/node identity; parent/scope, iteration and attempt context; authoritative recorded position; recorded timestamp and available duration facts; existing scheduling/causal evidence. One execution appears once regardless of the number of lifecycle events. Inspection/details remain independently authorized and keep capture provenance; no current value becomes a historical snapshot.

## Cursor and transport

- Confirmed cursor: exact occurrence, server position and resolved pinned scope.
- Pending target: requested position/identity, context/generation and loading/error; distinct from confirmed evidence.
- State: unavailable, paused, playing, buffering, ended or failed, with a separately identified gap/boundary reason.
- Speed:0.5/1/2/4; default1. Intervals2s/1s/0.5s/0.25s describe presentation, not backend elapsed time.

Transitions: initial admission→paused; Play→playing; timer→pending/confirmed next; next-page buffering→playing only for the same live generation; Pause/manual navigation/Activity/background→paused and timer retirement; failed read→failed+paused; retry success→paused; final/gap→ended or boundary-paused; context/access loss→clear/retire. No transition invokes workflow execution authority.

The controller exposes callbacks to the workbench's confirmed selection operation and manual-navigation pause. It never writes selection from a retired generation and never draws an edge merely because two cursor positions are adjacent.
