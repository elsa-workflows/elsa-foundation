# Specification quality checklist

Feature: [spec.md](../spec.md), reviewed 2026-11-02.

- [x] User outcomes, scope and non-goals are explicit.
- [x] Requirements and success criteria are observable and testable.
- [x] Primary journeys, failure/recovery, cancellation and compatibility are defined.
- [x] Source constraints from #2908, its research checkpoint and the claim resolve material ambiguity.
- [x] No NEEDS CLARIFICATION markers remain.
- [x] No class layout or implementation recipe is embedded in the requirements; persisted and wire shapes are named because compatibility depends on them.
- [x] Full parent scope remains visible; this slice cannot check the file criterion off.

An independent review against the code at `3adffdfe1` raised one blocker and ten other findings. All were adopted:

- The blocker: header-only v2 CSV runs never settle. This became FR-011 and R9.
- Rejected-line runs settle as a business error.
- Gates keyed on the CSV kind are listed in the plan.
- Ordinals are file ordinals with a 32-bit bound.
- `rawInputBytes` and the content definition are fixed.
- The snapshot publisher is distinct (R11).
- Kill-point, cancellation and native-journey proof are now in scope (US3, US5, SC-003, SC-006).
- Framing is re-verified (R10).
- The CR edge case is corrected.
- The untestable old-worker claim is now a stated precondition.
- "Exactly once" wording is replaced.

This passes specification quality only, not implementation acceptance.
