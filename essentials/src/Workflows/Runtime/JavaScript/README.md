# Elsa.Workflows.Runtime.JavaScript

Hosts the runtime endpoint for explicit JavaScript activity execution. The endpoint uses the typed
`IJavaScriptScriptEvaluator` contract from `Elsa.Expressions.JavaScript.Core`: callers provide script
source plus one JSON argument document and receive a JSON result.

This package does not participate in canonical expression binding evaluation. It injects no workflow
variables, inputs, outputs, services, configuration, or mutable execution context into scripts, and
it performs no variable write-back. Workflow-visible mutation remains a separate runtime intrinsic.
