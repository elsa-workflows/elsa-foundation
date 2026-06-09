# Agent Maturity Audit

Status: point-in-time audit for deciding whether the Elsa brain is mature enough to hand a large architecture unit to an AI agent.

## Purpose

This audit stress-tests the current repository operating model against realistic architecture work. It does not thin the constitution and does not change gates. It asks whether an agent can enter through the current source-of-truth layers, select the right workflow, avoid guessing, and produce useful architecture output without rereading or rewriting the whole repo.

## Inputs Reviewed

- [AGENTS.md](../../AGENTS.md)
- [Architecture tour](../architecture-tour.md)
- [Skills catalog](../skills/catalog.md)
- [Unfinished work](unfinished-work.md)
- [Knowledge inventory](knowledge-inventory.md)
- [Skills stabilization audit](skills-stabilization-audit.md)
- [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md)
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md)
- [CShells composition evidence](cshells-composition-evidence.md)
- [Map manifest](../maps/manifest.json)
- [Framework constitution](../../.specify/memory/constitution-framework.md)
- [Elsa constitution](../../.specify/memory/constitution.md)
- [CLAUDE.md](../../CLAUDE.md)

## Stress Scenarios

### Scenario A - Plan a workflow execution seam unit

Expected mature behavior:

- Start from `AGENTS.md`, then the architecture-development task path.
- Use the runtime handoff report as pre-spec input.
- Load only the relevant constitution gates: Design/Runtime split, artifact-only runtime, bridge/adapter, test discipline.
- Treat the runtime JavaScript design reference as named deferred debt, not accidental drift.
- Stop at a work-unit plan or Speckit-ready scope unless the user approves implementation.

Result: mostly pass.

Evidence:

- `runtime-execution-pre-spec-handoff.md` gives a strong handoff surface with gates, allowed crossing points, risk register, and candidate Speckit starting scope.
- `unfinished-work.md` points runtime work back to that report.
- `AGENTS.md` routes architecture development through Critical Constitution Review or Work Unit Planner.

Residual risk:

- A capable agent can follow the path, but the handoff is dense. A large architecture worker should receive a reviewed handoff prompt that names the exact files and sections to load, instead of being told only "plan runtime."

### Scenario B - Classify feature dependencies for a shell

Expected mature behavior:

- Use Feature Composition Explorer, not CShells Appsettings Generator first.
- Read the feature/dependency map as evidence, not policy.
- Use the reviewed classification boundary before marking required activations.
- Refuse to guess appsettings keys, required settings, secrets, or host-loading output.

Result: pass with current guardrails.

Evidence:

- `cshells-composition-evidence.md` now has Reviewed Classification v1.
- `docs/skills/catalog.md` routes Feature Composition Explorer and CShells Appsettings Generator through that boundary.
- `unfinished-work.md` no longer treats vocabulary review as pending; it points to classification passes and generator readiness.

Residual risk:

- The map manifest currently reports dirty relevant inputs, so agents must check map freshness before using maps as strong evidence.

### Scenario C - Review a provisional constitution gate

Expected mature behavior:

- Use Critical Constitution Review.
- Separate gate text from rationale, examples, draft history, and unresolved follow-up.
- Preserve draft/provisional status unless architects ratify.
- Record findings in reports before changing constitutional meaning.

Result: partial pass.

Evidence:

- Skill catalog gives the correct workflow.
- Knowledge inventory and unfinished work identify remaining constitution thinning and provisional-gate risks.
- Constitutions include knowledge-boundary notes and links to glossary/reports/reference docs.

Residual risk:

- The constitutions still contain enough examples, rationale links, and provisional decisions that a less careful agent may treat draft working text as ratified doctrine. Constitution thinning should continue only after targeted audit findings identify safe moves.

### Scenario D - Verify codebase against one constitution rule

Expected mature behavior:

- Use Verify Codebase Against Constitution.
- Pick one gate, inspect maps/source/tests/catalogs, and produce a report before proposing code changes.
- Classify findings as code drift, doc drift, missing test, or unclear gate.

Result: pass for report-first behavior, partial pass for map freshness.

Evidence:

- `test-maturity-and-weak-implementation-report.md` is a good example of the workflow.
- `unfinished-work.md` preserves code gaps without making every placeholder the next task.
- The skill catalog tells agents to produce a report before proposing code changes.

Residual risk:

- Generated maps are central to verification, but `docs/maps/manifest.json` says `relevant_inputs_dirty: true`. A worker must either refresh relevant maps or explicitly state that verification is based on stale snapshots.

## Findings

### F1 - The source-of-truth layering is mature enough to route agents

Classification: strength.

`AGENTS.md` is now a real AI-provider-neutral front door. It explains repo intent, source-of-truth layers, program-goal drift guard, task paths, map refresh rules, preference handling, and constitution boundaries without duplicating the whole architecture.

Impact:

An agent can start narrow, route to the right layer, and avoid treating `AGENTS.md` as a mutable roadmap.

Recommendation:

Keep `AGENTS.md` stable. Do not add new program objectives there; keep using `docs/program-goals/` and `docs/reports/`.

### F2 - The skill layer is usable, but some workflows remain report-bound

Classification: strength with guardrail.

Core workflows exist and are clear enough for architecture planning, codebase verification, source-of-truth audit, feature composition, and unfinished-work inventory review. The skill catalog also records when to plan first, how to route planned work through the selected work tracking model, and when tests/docs/maps are follow-through obligations.

Impact:

Agents are much less likely to jump straight into implementation.

Recommendation:

For any large architecture worker, provide the exact skill to use in the handoff prompt. Do not rely on the worker to infer between Work Unit Planner, Critical Constitution Review, and Speckit Flow Guide.

### F3 - Reports are now strong handoff surfaces

Classification: strength.

The runtime execution handoff, test maturity report, CShells composition evidence, knowledge inventory, and unfinished-work report collectively give agents a way to reason from current evidence rather than memory.

Impact:

The Elsa brain is starting to behave like a durable architecture workspace instead of a chat-history-dependent project.

Recommendation:

Before a large architecture handoff, pick one report as the primary handoff surface and name supporting reports explicitly.

### F4 - Constitution density remains the main maturity risk

Classification: risk.

The constitutions are thinner than before, but they still contain examples, rationale pointers, provisional/pending text, and candidate/pending pattern language. This is visible in the knowledge-boundary notes and in remaining references to worked examples, deferred configuration, and provisional review sections.

Impact:

An agent may over-read constitution text, treat provisional material as ratified, or copy explanatory material into new docs instead of linking to glossary/reference/report surfaces.

Recommendation:

Do targeted constitution thinning after this audit. Start with sections where the gate can remain short and existing reference docs already hold the explanation. Do not thin runtime/design gates until the runtime architecture work settles.

### F5 - Map freshness must be an explicit handoff gate

Classification: risk.

The map manifest reports `relevant_inputs_dirty: true`. Maps remain useful, but their freshness cannot be assumed for architecture verification or dependency claims.

Impact:

A large architecture worker may base decisions on stale generated facts.

Recommendation:

Every large handoff that relies on maps should include one of these instructions:

- refresh the relevant map first;
- use maps only as approximate navigation and verify facts from source;
- wait for the in-progress map-generator work to settle.

### F6 - Program-goal behavior is mature enough, but local-loop drift is still possible

Classification: manageable risk.

The drift guard is lightweight and the program-goal file is clear. The risk is not missing process; the risk is agents continuing local cleanup work because it is nearby.

Impact:

The workspace can keep polishing the Elsa brain instead of using it for architecture decisions.

Recommendation:

Before a third consecutive docs/meta unit, force a Program Goal Drift Review. For this session, that means the next step after this audit should be either targeted constitution thinning from audit findings or a prepared architecture-worker handoff, not more broad operating-model grooming.

### F7 - AI-provider neutrality is good, with Claude as a thin adapter

Classification: strength.

`CLAUDE.md` is a compatibility shim and points back to `AGENTS.md`. The skills audit confirms Claude wrappers point back to the neutral catalog.

Impact:

The repo is no longer Claude-specific in its architecture guidance.

Recommendation:

When another provider adapter is added, mirror only thin wrappers. Do not copy architecture explanations into provider-specific files.

## Maturity Verdict

The Elsa brain is mature enough to hand an AI agent a large architecture piece if the handoff is constrained.

It is not mature enough for a vague instruction like "go design runtime" without a reviewed prompt. The safe model is:

- choose one primary report;
- name the exact skill;
- name the constitution sections to treat as gates;
- name maps as fresh/stale and whether to refresh;
- require findings/work-unit plan before implementation;
- require unresolved decisions to return to reports/specs, not into ad-hoc chat memory.

## Before Big Architecture Handoff Checklist

Use this checklist before letting a worker handle a large architecture unit:

- Program goal state is explicit or intentionally `none/free-flow`.
- One primary skill is named.
- One primary report/spec is named.
- Relevant constitution sections are named as gates.
- Glossary/reference docs are named only for needed terms.
- Map freshness is checked or source verification is required.
- The worker is told whether to stop at report, work-unit plan, Speckit spec, or implementation.
- The worker is told where unresolved decisions must be recorded.
- The worker is told whether to work in the current session or as a fresh worker.

## Recommended Next Work

1. Do targeted constitution thinning from this audit, starting with safe explanatory/provisional material that already has a reference/report home.
2. Prepare a reviewed handoff prompt template for large architecture workers.
3. After runtime work lands, run this audit again against the runtime handoff path to see whether the Elsa brain still routes correctly.
