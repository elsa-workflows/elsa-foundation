# Decision disclosure in workflows

Enable `DecisionsWorkflowsApi` alongside the native workflow or BPMN APIs to apply Decisions permissions at the exact retained-material boundary. The pure engine and runtime activity do not depend on HTTP or authoring stores.

The deny-only permission resource contribution checks the immutable native/model dependency closure for decision pins. Native value inspection and payload resolution, and direct BPMN execution reads, require current `decisions:read` for each pinned decision. Existing workflow permissions remain required. Protected value policies additionally require `decisions:sensitive-values`; protected decision bindings currently refuse publication in the runtime adapter.

The Publishing API authorizes the exact produced `WorkflowArtifactClosure` through the canonical resource authorization service immediately before delivery. Its decision-dependent closure requires `decisions:read` and `decisions:source`, including transitive dependencies. This gate applies before the selected HTTP export target sees the closure. Missing, corrupt, extra or hash-mismatched material fails closed. Trusted hosts invoking export targets directly retain their existing behavior; those hosts own their disclosure boundary.

The executable inspection DTO omits node metadata, which holds the embedded decision pin. Definition inventory is not filtered by this contribution. Its default library visibility policy preserves tenant/action visibility only while this is the sole resource contribution; hosts with other resource handlers must supply their own query visibility policy. Explicit host policies are preserved.
