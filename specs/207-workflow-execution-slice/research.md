# Research: Workflow Execution Vertical Slice

## Decision: Use Publishing.Api As Compile Bridge

**Decision**: Implement `POST /publishing/workflows/{versionId}/publish` in `Elsa.Workflows.Publishing.Api`.

**Rationale**: Publishing is already documented as the bridge that can read Design and produce runtime artifacts. Runtime must not read `WorkflowDefinitionVersion` or `WorkflowDefinitionState`.

**Alternatives considered**:
- Runtime endpoint reads Design directly: rejected, violates §E2.2/§E2.6.
- Put publishing in Design.Api: rejected, Design should not own runtime artifact construction.

## Decision: Add Runtime.Api Feature For Execute Endpoint

**Decision**: Add `Elsa.Workflows.Runtime.Api` with `POST /runtime/workflows/{artifactId}/execute`.

**Rationale**: The endpoint is a runtime concern but should remain separate from Runtime.Core contracts/services. This follows existing API feature patterns.

**Alternatives considered**:
- Put execute endpoint in Publishing.Api: rejected, would conflate bridge and runtime.
- Put endpoint in Runtime.Core: rejected, Core should not depend on FastEndpoints/API infrastructure.

## Decision: In-Memory Artifact Store For First Slice

**Decision**: Define `IWorkflowExecutableStore` in Runtime.Core and provide `InMemoryWorkflowExecutableStore` for the demo.

**Rationale**: The feature goal is proving the seam and execution path. Durable publication needs a separate persistence design and should not block the Monday demo.

**Alternatives considered**:
- Persist artifacts in Design EF tables: rejected, wrong ownership.
- Add runtime EF persistence now: rejected, too broad for the vertical slice.

## Decision: Literal-Only Typed Input Materialization

**Decision**: Compile only literal `ArgumentState` values into `RuntimeInputBinding` and include type metadata so execution can create `InputArgument<T>`.

**Rationale**: `WriteLine.Text` needs a real `InputArgument<string>`. Literal support is sufficient for a useful demo and avoids pretending expression/variable binding is complete.

**Alternatives considered**:
- Pass null inputs as the existing construct endpoint does: rejected, activity executes without authored values.
- Implement expressions/variables now: rejected, belongs to broader runtime value-binding work.

## Decision: Sequential Synchronous Executor

**Decision**: Implement a strict `SequentialWorkflowExecutor` that supports one start node and at most one outgoing edge from each node.

**Rationale**: It demonstrates real execution while making unsupported runtime behaviors explicit. Branching, bookmarks, scheduling, and recovery can build on the same artifact boundary later.

**Alternatives considered**:
- Build a scheduler now: rejected, too broad.
- Execute only one activity: rejected, does not demonstrate workflow control flow.

## Decision: Clear Domain Diagnostics

**Decision**: Return deterministic publishing/execution diagnostics for unsupported graph shapes, missing activity rows, missing artifacts, and failed activity execution.

**Rationale**: Explicit failure is acceptable for a bounded slice; silent partial execution is not.

**Alternatives considered**:
- Best-effort traversal: rejected, misleading for a runtime demo.
