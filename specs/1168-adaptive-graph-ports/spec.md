# Adaptive graph ports

**Status**: Implemented
**Issue**: #3471
**Program-goal state**: none/free-flow

Owner request: connected nodes should move their ports to shorten edges when their relative positions change, including vertically stacked activities.

## Acceptance

1. Actual ports choose top, bottom, left or right from measured node geometry and update while dragging. A small improvement threshold prevents chatter near a side boundary.
2. One inbound handle and exact outcome handle names remain stable. Fan-in/fan-out choose a combined direction; independent outcomes may use different sides. Disconnected ports retain left-input/right-output defaults. Self-loops do not steer placement.
3. Ports sharing a side have distinct positions. Outcome labels follow their ports, remain readable and expose their full names. Light/dark and curved/squared wiring remain supported.
4. Workflow and Activity Definition authoring, BPMN and read-only/runtime graphs share the behavior. Drawing/reconnecting edges, undo/redo and reloading saved layouts preserve logical connections.
5. Placement is derived presentation state. No backend, wire, connection identity, layout schema or workflow execution change.

## Verification

Pure geometry cases, actual React Flow browser dragging/connection journeys, full affected frontend suite, red/green behavior controls, inspected screenshots, production build and existing bundle budgets, typechecks/lint, architecture/maps and exact-head CI/review. Existing glossary and Studio ADRs 0015/0018/0019 remain canonical.
