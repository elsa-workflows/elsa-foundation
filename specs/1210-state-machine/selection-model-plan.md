# Bounded selection reference model

**Status:** implementation plan for source review, 12 November 2026. Part of M0/#3820; this is neither
production StateMachine code nor a replacement for the selected runtime's integration witness.

## Goal and scope

Turn the [selection proposal](contracts/selection.md) into a dependency-free executable reference for candidate
discovery, exit boundaries, regional conflict arbitration and phase dependencies. Keep it beneath this spec,
outside product projects and public packages. Use Node's built-in test runner; no installation, .NET command,
host, provider, Azure grant or infrastructure expansion is required.

Inputs are explicit immutable chart definitions, active visit identities and one already-grouped semantic input.
Guards are synchronous pure callbacks supplied by tests, standing in for evaluated expressions. The model must
not infer a causal cohort, receive external messages, persist receipts, emulate ordinary Elsa activities or
claim that a process restart is durable recovery. Completion/automatic/business queue arbitration, loop recovery,
the production definition schema, value scope and physical arm/cancellation boundaries remain outside this slice.

## Representation and algorithm

- A virtual machine root and region nodes make containment explicit. Compound states and regions each select
  an initial child; a parallel state owns region children. Leaf/final states have no children. States and
  transitions have stable IDs, and transitions have unique explicit order. Reject invalid structure, impossible
  active configurations and direct cross-region edges before selection.
- An active configuration contains all live state visits; region and virtual root containers have no authored
  entry/exit work. Exactly one child is active below a compound/region/root; all regions are active below a
  parallel state. Every state visit has a distinct occurrence identity.
- Discover matching rules deepest first. Skip a source's guards when an eligible descendant suppresses its
  ancestor fallback. Evaluate source-local order lazily, propagate considered guard errors, and consider a
  shared ancestor once. Guard order between unrelated sources is deterministic by stable state ID.
- Compute exit and entry sets from the retained common containment boundary. Targetless reactions preserve
  visits; explicit self re-entry and parent re-entry replace them. Parent-preserving descendant edges retain
  the compound visit. A parallel parent targeting its descendant requires whole-parent re-entry. Re-entry
  starts initial descendants in other regions. A target that is already active inside an unrelated parallel
  region is invalid rather than an implied reset.
- Reject a transition from a descendant directly to its ancestor in this bounded model. The selection proposal
  does not yet settle whether that ancestor visit is retained or replaced. Record and test this explicit model
  limitation instead of silently choosing deadline/local-value semantics; the full product contract still needs
  this shape resolved before implementation.
- Select compatible candidates in total transition order. Conflict means overlapping exited visits or one
  candidate exiting another candidate's source visit. Do not revive suppressed ancestors or later source-local
  rules after a candidate loses. Return the selected transition identities, exact leaving visits, entering state
  identities and phase work dependencies. Preserve source contexts for action work.
- Exit dependencies are child-before-parent; entry dependencies are parent-before-child. Action waits for
  every exit; entry waits for every action. Independent work in a phase has no completion-order promise. This
  returns a plan only; it does not schedule, cancel or execute authored work.

## Tests and negative controls

Cover lazy child fallback (including a throwing suppressed parent), source-local order, shared ancestors,
compatible regional reactions, explicit conflict order, action-without-exit conflicts, losing-candidate behavior,
compound preserve/re-enter boundaries, parallel whole-parent reset, nested common ancestors, initial expansion,
structural rejection, duplicate order/identity and malformed active configurations.
Include a negative ancestor-target case so this unresolved shape cannot be interpreted as preserving or
re-entering the ancestor accidentally.

For phase plans, assert both required dependency edges and the absence of dependency edges between independent
regional work. Assert that exact source visit IDs survive planning and that re-entry is represented as old-visit
exit plus new entry rather than reuse. A model plan cannot prove allocation or persistence of the future visit.

Run isolated source mutations as negative controls: suppress hierarchy precedence, disable source-visit conflict
checking and remove a phase barrier. Each must make its named semantic test fail. Never mutate the reviewed source
in place; retain the test counts and actual control outcomes with the PR evidence.

## Review and handoff

Review this plan before implementing it. Then review the exact code/test diff independently for correctness,
repository standards and accepted scope. Fix findings in new commits and repeat the source loop. Record the
exact source, command, test results, negative controls and limits. The model can disprove algorithm mistakes;
it cannot close Q1/M0, choose native/model placement, approve the kernel design or replace real ordinary-activity,
provider, restart, Studio, compensation and protected resulting-main qualification.
