# Exclude layout from the Artifact Hash

A workflow Executable must be inspectable (rendered read-only) without resolving its source Workflow Definition, so the publish step copies the graph layout into a Layout Sidecar embedded on the Source Reference (placement decided in elsa-foundation ADR 0039). The Artifact Hash never covers the Layout Sidecar: visual arrangement is not behavior, and hashing it would let cosmetic edits change artifact identity. The current hasher (`WorkflowExecutableHasher`) already excludes layout — this ADR locks that in so the sidecar is never added to the hash payload. Note that the hash currently covers source identity in addition to Execution Material; whether it should (publish identity vs behavioral identity) is a separate decision.

## Considered Options

- **Hash everything in the artifact, including layout** — rejected: cosmetic edits would mint new hashes for behaviorally identical workflows, destroying hash-based identity.
- **Store no layout in the artifact; resolve the source Definition Version to render** — rejected: breaks when the Executable outlives or travels without its definition (retention, deletion, cross-environment promotion), and misrepresents "inspecting the executable" as viewing its source.
- **Embed activity descriptors too, for fully offline rendering** — rejected: descriptors (names, icons, ports) resolve from the live activity catalog at render time. A catalog miss is shown honestly, since it also means the Executable cannot run in that environment.

## Consequences

- The Executable is self-describing for structure and geometry, but rendering fidelity (icons, display names, ports) still depends on the environment's activity catalog — the same dependency execution has.
- Executables published before the Layout Sidecar existed have no stored layout; renderers must fall back to automatic layout.
