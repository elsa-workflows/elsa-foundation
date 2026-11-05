# Specification quality checklist: external authentication

Created 2026-11-04. Feature: [spec.md](../spec.md).

- [x] User value, bounded scope and owner decisions are explicit.
- [x] Mandatory template sections are complete; no unresolved clarification placeholders.
- [x] Requirements are observable and testable; implementation placement is deferred to the plan.
- [x] Each user journey has independent tests and concrete acceptance scenarios.
- [x] Identity isolation, admission, secret handling, preview non-mutation, ownership and recovery are covered.
- [x] Source transitions, stale/concurrent mutations, restart and multiple-node behavior are covered.
- [x] Success criteria measure user/security outcomes and resulting-main delivery.
- [x] Dependencies, assumptions, exclusions and optional-module/capability boundaries are explicit.

Review 2026-11-04: checked against all #3579 product bullets and success criteria, the existing #2848 acceptance, ADR 0085 D1–D11 and the 4 November delivery authorization. All are represented. This is specification completeness, not implementation or runtime acceptance. Design research may refine requirements before implementation; any material unresolved product choice returns to the owner.
