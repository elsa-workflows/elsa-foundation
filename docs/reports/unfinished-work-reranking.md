# Unfinished Work Re-Ranking

Status: superseded point-in-time review. Planned work now routes through the selected work tracking model; `unfinished-work.md` remains an inventory, not a queue.

## Purpose

Record the earlier re-ranking of [unfinished work](unfinished-work.md) for the active Elsa Brain Operating Model review. Do not use this report as live work-tracking guidance.

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

## Historical Priority Recommendation

| Priority | Candidate | Why |
|---:|---|---|
| 1 | Unfinished work triage maintenance | The earlier "what next" surface needed program-goal-state-aware routing so future agents would not rank only by recent local topic. |
| 2 | Map freshness / testing maturity map | Maps are useful navigation facts, but current map-generator work is already assigned elsewhere; future work should be map/report planning or verification unless the user redirects. |
| 3 | Configuration and feature dependency classification | Feature composition remains important as documentation/report classification work, but generator or source-code changes should wait for an explicit implementation bucket. |
| - | Codebase reality / test maturity follow-up | Code placeholders and weak implementations remain documented evidence, but they are not current Elsa Brain Operating Model work-unit candidates while the user wants no code changes. |
| 5 | Constitution ratification / provisional gate review | Targeted gate review is useful when needed, but broad ratification should wait for clearer code reality and runtime seam decisions. |

## Decision

Superseded. Do not add a live priority queue to `unfinished-work.md`. Keep that report useful as inventory and route planned work through the selected work tracking model.

## Follow-Up

- Before future "what next" reviews, identify the current program goal state and selected work tracking model.
- Use `unfinished-work.md` only as inventory/evidence.
- Do not treat Elsa Brain Operating Model as the default for ordinary domain or consumer work.
