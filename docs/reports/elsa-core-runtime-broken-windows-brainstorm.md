# Elsa Core Runtime Broken Windows Brainstorm

Status: brainstorm queue and analysis plan. This is not a design decision, Speckit spec, or implementation plan.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

## Purpose

Elsa 4 in `elsa-foundation` needs to import, rethink, and likely redesign major workflow runtime and execution behavior from the Elsa 3 `elsa-core` repository. This report preserves the initial maintainer concerns and defines how future sessions should analyze the Elsa 3 runtime without losing decisions or mixing brainstorm notes with ratified architecture.

The unit represented by this report is done when:

- Each maintainer-listed concern has been analyzed against Elsa 3 source evidence.
- Additional valuable cleanup and redesign opportunities have been identified from a full code, architecture, and design review of `elsa-core`.
- The resulting candidates are grouped into topic-specific brainstorm notes or Speckit-ready work units.
- Approved architecture decisions are moved to the normal source-of-truth layer: spec for work-unit behavior, glossary for meanings, constitution for gates, or program goal for active durable work.

## Working Rules

- Preserve user-stated concerns separately from source-derived findings.
- Do not treat this report as canonical architecture doctrine.
- Do not import Elsa 3 runtime code before the Runtime execution seam has an approved target shape.
- Keep Runtime execution-time dependencies Design-free unless an approved constitution amendment says otherwise.
- When a topic produces an approved decision, link the new spec/report/work unit here instead of expanding this report indefinitely.

## Maintainer-Listed Broken Windows

These are the initial concerns as understood from the maintainer's June 9, 2026 note. They require source confirmation before becoming final work units.

### 1. Serialization Is Overgrown And Fragile

Working understanding: Elsa 3 has too many serialization modes, custom converters, and value-shape exceptions. Workflow definitions, workflow instances, workflow variables, activity inputs, and activity outputs all pressure the serializer differently. The hardest case is runtime values that can be arbitrary CLR objects, `ExpandoObject`, JSON DOMs, Newtonsoft models, or other values produced by user activities. Existing compatibility with persisted workflow definitions and instances makes change risky.

Brainstorm direction: Elsa 4 should aim for a unified serialization story with explicit boundaries around what the runtime promises to persist. Some responsibility may need to move to integrators through typed value contracts, serializers, storage drivers, or explicit persistence policies.

Source-backed topic note: [Elsa Core runtime serialization and value persistence analysis](elsa-core-runtime-serialization-value-persistence-analysis.md).

Elsa 4 brainstorm decisions: [Elsa 4 runtime serialization brainstorm decisions](elsa-4-runtime-serialization-brainstorm-decisions.md).

Questions to validate:

- Which Elsa 3 serialized shapes must Elsa 4 read for migration, and which can be intentionally incompatible?
- Should arbitrary activity/runtime values ever be persisted by default?
- Should runtime state store typed values, JSON values, envelopes, or integrator-provided external references?
- How much Newtonsoft compatibility is required versus allowing a clean System.Text.Json-centered model?

### 2. Workflow Variables, Inputs, And Outputs Duplicate Capabilities

Working understanding: workflow-level variables, inputs, and outputs are technically similar but currently modeled as separate concepts. Their main difference may be semantic intent. This creates duplicated behavior and prevents workflow input/output from naturally inheriting variable capabilities.

Brainstorm direction: consider one workflow-level value declaration model with role metadata such as variable, input, output, or combined roles. The model must still preserve clear user-facing semantics and validation.

Questions to validate:

- Are input/output capabilities truly identical to variables once defaults, requiredness, external API contract, and persistence are included?
- Can one declaration serve internal variables and public workflow contracts without confusing tooling?
- How should role-specific validation, visibility, and external invocation metadata be represented?

### 3. API JSON Differs From Stored Workflow Definition JSON

Working understanding: Elsa Studio sends a workflow definition JSON shape that differs from the JSON persisted in the database, and API export JSON differs as well. This complicates import/export, startup imports from blob storage, and workflow-as-activity scenarios. Descriptor resolution during deserialization can break when a workflow references another workflow-backed activity that has not yet been imported, forcing a two-pass import with `NotFound` placeholders.

Brainstorm direction: separate authored document storage, API/read models, and runtime executable artifacts deliberately. Avoid requiring activity descriptor resolution when merely storing or importing authored workflow data. Resolve to runtime-owned executable artifacts only at compile/publish time.

Questions to validate:

- What JSON shapes exist in Elsa 3 for Studio API, DB persistence, export/import, and runtime execution?
- Which shape should Elsa 4 treat as canonical authored state?
- Can imports persist opaque activity descriptors first and defer construction/resolution until publish?
- How should workflow-as-activity cycles, missing dependencies, and import ordering be reported?

### 4. Workflow And Activity Execution Middleware Feels Too Complex

Working understanding: Elsa 3 has separate workflow execution and activity execution middleware infrastructure. The materialized pipeline is a linked list of delegates, which may be fast but makes troubleshooting and debugging harder. The design may also be less reusable than necessary.

Brainstorm direction: investigate a shared middleware abstraction that can serve workflow execution and activity execution without hiding execution state. Decide whether linked-list materialization is actually buying enough performance to justify the debugging cost.

Source-backed topic note: [Elsa Core runtime execution layer analysis](elsa-core-runtime-execution-layer-analysis.md).

Elsa 4 brainstorm decisions: [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md).

Action plan: [Elsa 4 runtime execution action plan](elsa-4-runtime-execution-action-plan.md).

Questions to validate:

- What middleware stages exist for workflow execution versus activity execution in Elsa 3?
- Which stages are truly shared, and which need distinct context types?
- Is pipeline materialization measurable as a hot-path optimization?
- What diagnostics, traceability, or step inspection should the Elsa 4 pipeline expose?

### 5. Input Evaluation Memory Register May Be Overcomplicated

Working understanding: before activity execution, activity inputs are evaluated and stored in a memory register, inspired by Windows Workflow Foundation. This may be more machinery than Elsa 4 needs.

Brainstorm direction: determine whether pre-evaluated registers are necessary for correctness, expression consistency, retries, bookmarks, and observability, or whether direct evaluated bindings / scoped value access would be simpler.

Questions to validate:

- What correctness problem does the register solve in Elsa 3?
- Are evaluated input values persisted, replayed, or only held in memory?
- How does the register interact with retries, incidents, bookmarks, and resumed execution?
- Can Elsa 4 make input evaluation explicit without a separate register abstraction?

### 6. Activity Output Is Ephemeral Unless Captured

Working understanding: activity output disappears once a workflow instance leaves memory unless the value is copied into a persisted workflow variable. Variable storage drivers exist, but equivalent activity-output storage could be confusing and still faces arbitrary-value serialization risk.

Brainstorm direction: compare how other workflow engines model activity outputs, persistence, result references, and data passing. Elsa 4 likely needs a clearer distinction between ephemeral execution results, persisted workflow state, and externally stored payloads.

Questions to validate:

- Should activity outputs be addressable after execution, and if so for how long?
- Should output persistence be opt-in per output, per activity, per workflow, or per storage policy?
- Can output values be represented as references to durable payload storage instead of serialized inline state?
- What should happen when an output value is not persistable?

### 7. Direct Activity Output-To-Input Links

Working understanding: users should ideally be able to connect an activity output directly to a later activity input. The challenge is that activities can have multiple inputs, outputs, and outcomes. Outcomes are control-flow decisions, not data outputs, so visual output ports and outcome ports can become confusing if represented the same way.

Brainstorm direction: explore a visual and runtime model that separates data-flow ports from control-flow outcomes while still letting users wire outputs to inputs naturally. The model must support multiple inputs/outputs and preserve Elsa's outcome semantics.

Questions to validate:

- Should data-flow links be first-class graph edges or compiled variable/binding expressions?
- How should Studio visually distinguish outcome ports from output ports?
- Can an activity output link target an input on a non-immediate downstream activity?
- How do output-to-input links interact with parallel branches, loops, retries, and activity completion order?

## Brainstorm Session Plan

Use separate sessions for each major topic. Each session should end by updating this report or replacing its topic notes with a more specific linked report/spec.

1. Serialization and runtime value persistence.
2. Workflow value declarations: variables, inputs, outputs, roles, and public contracts.
3. Workflow definition JSON: authored state, API contracts, import/export, and workflow-as-activity resolution.
4. Execution pipeline shape: workflow middleware, activity middleware, diagnostics, and performance.
5. Input evaluation model: expression evaluation, registers, replay, and resumed execution.
6. Activity output lifecycle: ephemeral values, persisted outputs, storage drivers, and external references.
7. Data-flow links: output-to-input wiring, control outcomes, graph semantics, and Studio visualization.
8. Full-source review additions: candidates discovered from `elsa-core` that are not covered above.

## Full Elsa Core Analysis Plan

Before confirming or challenging the concerns above, inspect the Elsa 3 `elsa-core` repository with this sequence:

1. Repository orientation: project layout, runtime packages, persistence packages, API modules, Studio-facing contracts, and test projects.
2. Runtime execution path trace: workflow start, activity scheduling, middleware, input evaluation, output capture, incidents, bookmarks, suspension, resumption, and completion.
3. Serialization inventory: serializer registrations, custom converters, persisted entity shapes, workflow definition JSON, workflow instance JSON, variable values, activity values, and compatibility assumptions.
4. Data model comparison: workflow variables, inputs, outputs, activity inputs, activity outputs, outcomes, ports, descriptors, and expressions.
5. Import/export and workflow-as-activity trace: API payloads, stored payloads, exported payloads, startup import behavior, descriptor resolution, and two-pass import behavior.
6. Diagnostics and debuggability review: pipeline visibility, exception surfaces, logging, tracing, replayability, and failure classification.
7. Test coverage map: direct tests for serialization, execution middleware, input evaluation, output behavior, import/export, and workflow-as-activity.
8. Improvement synthesis: classify opportunities as simplify, redesign, preserve for compatibility, migrate with adapter, or intentionally drop.

## Candidate Output Format For Each Topic

Each topic-specific note should use this shape:

- Maintainer concern.
- Elsa 3 source evidence.
- Current Elsa 4 architecture constraints.
- Compatibility constraints.
- Design options considered.
- Preferred direction, if one emerges.
- Open questions for the maintainer.
- Follow-up surface: report, spec, glossary, constitution amendment, code spike, or test map.

## Initial Program-Goal Alignment

This work belongs to `Runtime Execution Seam` because it directly affects the runnable artifact boundary, runtime context/value model, workflow-as-activity behavior, execution middleware, and Runtime-owned import/compile/publish decisions. It should not broaden into a general Elsa Foundation operating-model cleanup unless the analysis uncovers source-of-truth or governance issues outside runtime execution.
