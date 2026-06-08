# Unfinished Work Re-Ranking

Status: point-in-time review for ranking unfinished work by the current program goal state without turning program goals into a global priority hierarchy.

## Purpose

Re-rank [unfinished work](unfinished-work.md) for the active Elsa Brain Operating Model review while preserving the rule that future sessions may use a different named bucket or explicitly run as `none/free-flow`.

## Inputs Reviewed

- [Program goals index](../program-goals/README.md)
- [Elsa Brain Operating Model](../program-goals/elsa-brain-operating-model.md)
- [Unfinished work](unfinished-work.md)
- [Architecture tour review](architecture-tour-review.md)
- [Glossary coverage audit](glossary-coverage-audit.md)
- [Skills stabilization audit](skills-stabilization-audit.md)
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md)
- [CShells composition evidence](cshells-composition-evidence.md)
- [Maps index](../maps/README.md)

## Program Goal State

Current re-ranking state: [Elsa Brain Operating Model](../program-goals/elsa-brain-operating-model.md).

This state is explicit for this review only. Program goals are coordination lenses, not a global priority hierarchy. Future work may use another named bucket or `none/free-flow`.

## Priority Recommendation

| Priority | Candidate | Why |
|---:|---|---|
| 1 | Unfinished work triage maintenance | The "what next" surface needs a program-goal-state-aware priority view so future agents do not rank only by recent local topic. |
| 2 | Map freshness / testing maturity map | Maps are useful navigation facts, but current map-generator work is already assigned elsewhere; future work should be map/report planning or verification unless the user redirects. |
| 3 | Configuration and feature dependency classification | Feature composition remains important as documentation/report classification work, but generator or source-code changes should wait for an explicit implementation bucket. |
| - | Codebase reality / test maturity follow-up | Code placeholders and weak implementations remain documented evidence, but they are not current Elsa Brain Operating Model work-unit candidates while the user wants no code changes. |
| 5 | Constitution ratification / provisional gate review | Targeted gate review is useful when needed, but broad ratification should wait for clearer code reality and runtime seam decisions. |

## Decision

Do not reorder or delete the detailed unfinished-work inventory. Add a priority view above it and a re-ranking rule below it. This keeps the report useful both as an inventory and as a next-step selector.

## Follow-Up

- Use the current priority view to choose the next Elsa-brain unit, excluding code-change candidates unless the user explicitly selects a code-change or implementation bucket.
- Before future "what next" reviews, identify the current program goal state.
- Do not treat Elsa Brain Operating Model as the default for ordinary domain or consumer work.
