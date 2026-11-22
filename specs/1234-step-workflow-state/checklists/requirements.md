# Specification Quality Checklist: Step bodies change workflow state

**Purpose**: Check the proposal before planning and coordinator review.
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

- [x] User value, boundaries and five prioritized independently testable stories are explicit.
- [x] Functional requirements and measurable success criteria cover successful and negative behavior.
- [x] No unresolved clarification markers or template placeholders remain.
- [x] Accepted ADRs and S-7 transaction/placement limits are identified without a new transaction model.
- [x] Read-your-writes, capture precedence, faults/cancellation, suspension/recovery and source scope are explicit.
- [x] Correlation never rebinds existing bookmarks; later waits keep their own input behavior.
- [x] Native/script/import behavior and stable refusal requirements are linked to concrete contracts.
- [x] Input writes, setInput/setResult, graph/reusable expansion and arbitrary interop remain excluded.
- [x] Implementation details live in plan/contracts/research; the specification states observable behavior.
- [x] Runtime/store/inspection and Studio delivery/acceptance are part of the same approved implementation scope.
- [x] Dependencies, assumptions and approval boundary are explicit; research review is not spec approval.

Author checklist complete. Independent proposal review and coordinator approval remain separate and must be recorded on the proposal PR. No runtime, recovery, Studio or CI execution is claimed by these checks.
