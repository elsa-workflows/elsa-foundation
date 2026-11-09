# Decisions at workflow publication

Enable `DecisionsWorkflowsDesign` in a publishing host with Decisions persistence, the evaluator and workflow publishing. Its `IExecutableCompilationSource` contribution resolves latest once, validates an immutable version and embeds its executable bytes, hashes, profile and semantics identity in node metadata. Later draft edits and new decision versions do not change that executable.

The default `IDecisionPublicationAuthorizer` requires an authenticated canonical tenant and current Read/Source access. Non-HTTP publishing hosts replace this single scoped port with their own trusted policy. It never supplies runtime permission snapshots. Runtime authorization is checked separately by the [runtime adapter](../Runtime/README.md).

The activity's `decision-version` UI hint is supplied by the optional Decisions Studio module. Native activities and ordinary BPMN native tasks use the same activity; vendor-specific decisionRef bindings are outside the admitted DMN profile.
