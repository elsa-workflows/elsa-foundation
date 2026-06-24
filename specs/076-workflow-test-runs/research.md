# Research: Workflow Definition Test Runs

## Decision: Implement designer test runs as a publishing bridge capability, not a Runtime API capability

**Rationale**: The test-run request starts from Design-owned workflow definition state. The existing `Elsa.Workflows.Publishing.Api` project is the legal bridge that may read Design persistence seams and produce Runtime-owned executable artifacts. Keeping test-run orchestration there preserves the §E2.2 rule that Runtime does not reference Design.

**Alternatives considered**:

- Add a Runtime endpoint that accepts workflow definition ids. Rejected because Runtime would need to load or understand Design state.
- Add the behavior to Workflows.Design API. Rejected because Design would then drive runtime dispatch directly instead of using the established compile/publish bridge.

## Decision: Reuse a shared workflow executable compiler for publish and test-run paths

**Rationale**: Publishing and test runs both need to turn a `WorkflowDefinitionVersion.State.RootActivity` into a `WorkflowExecutable`. Reusing one compiler avoids drift between "what publishes" and "what tests" while allowing different scopes: durable publish versus transient test.

**Alternatives considered**:

- Duplicate compile logic in the test-run handler. Rejected because bug fixes and supported activity structure would drift.
- Treat test runs as a special flag on publish. Rejected because publish semantics imply durable artifact visibility and promotion/reuse.

## Decision: Store test-run artifacts in a transient store and let normal artifact lookup remain durable-only

**Rationale**: The user goal is avoiding pollution of durable executable artifacts. A separate transient lookup path lets the test-run dispatcher pin an executable identity for execution without making normal `ExecuteWorkflow` by artifact id accept test artifacts.

**Alternatives considered**:

- Save transient artifacts in the existing durable store with metadata. Rejected because normal execute-by-artifact-id currently resolves that store, which would make transient artifacts production-startable unless every caller remembers to filter.
- Do not store the transient artifact at all. Rejected because current runtime dispatch pins identity and downstream runtime scheduling expects artifact lookup by identity.

## Decision: Add a dispatcher method that accepts an explicit executable lookup source

**Rationale**: Existing `WorkflowExecutionStartDispatcher.DispatchAsync(request)` should continue to use the durable store. A second overload or method for a supplied executable keeps production behavior unchanged while allowing the bridge to dispatch a transient artifact that Runtime still treats as an artifact.

**Alternatives considered**:

- Add a boolean `AllowTransient` to `WorkflowExecutionStartDispatchRequest`. Rejected because a public flag on normal runtime execution invites accidental production use.
- Register the transient store as the main `IWorkflowExecutableStore`. Rejected because it changes durable runtime behavior globally.

## Decision: Test-run history is in-memory for this vertical slice

**Rationale**: Existing workflow runtime and publishing vertical slices use in-memory stores for development behavior. This feature's first goal is contract shape and designer loop, not durable audit/history.

**Alternatives considered**:

- Add EF Core storage and migrations. Rejected as premature for the current runtime seam slice and outside the requested developer-testing flow.
- No test-run store. Rejected because the designer needs correlation between test-run id, execution id, artifact id, status, and rejection reason.
