# Data Model: Runtime Value Binding Contract

## RuntimeInputBinding

Durable compiled binding declaration on an executable node input. Sources are literal, expression, activity output, durable value, or reference. Evaluated input values are not workflow continuation state.

## ActiveActivityOutput

Execution-local output value keyed by workflow execution, activity execution, and output name. It is valid only while the active execution scope retains it.

## RuntimeOutputCapture

Declaration that an activity output should be captured into a declared durable value. Capture targets the existing durable value state model instead of creating a separate durable activity-output store.

## RuntimeInputBindingDiagnostic

Artifact/build-time validation finding for binding declarations. Used here for cross-suspension and ambiguous output-reference rules without introducing the full compile/publish pipeline.
