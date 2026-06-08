# Architecture Tour Review

Status: point-in-time review for keeping the architecture tour aligned with the Elsa Brain Operating Model goal.

## Purpose

Check whether [architecture-tour.md](../architecture-tour.md) gives a concise orientation to the repository, core systems, and next lookup surfaces without duplicating glossary, constitution, map, report, or program-goal material.

## Inputs Reviewed

- [Architecture tour](../architecture-tour.md)
- [Program goals index](../program-goals/README.md)
- [Elsa Brain Operating Model](../program-goals/elsa-brain-operating-model.md)
- [Docs index](../README.md)
- [Maps index](../maps/README.md)
- [Architecture reference map](../maps/architecture-reference-map.md)
- [Unfinished work](unfinished-work.md)
- [Knowledge inventory](knowledge-inventory.md)
- [Skills catalog](../skills/catalog.md)
- [Root glossary](../glossary/root.md)
- [Elsa glossary](../glossary/elsa.md)

## Findings

| Finding | Classification | Resolution |
|---|---|---|
| The tour named the Elsa brain role but did not route readers to active program goals. | Under-routed source-of-truth layer | Add `docs/program-goals/` to the repo shape and "How to go deeper" sections. |
| The tour compressed glossary, skills, maps, reports, reference docs, and goals into a broad docs bucket. | Minor source-of-truth ambiguity | Clarify the `docs/` bullet without explaining those layers in detail. |
| The Workflows Design/Runtime paragraph stated the ideal rule but did not point to current review signals or deferred exceptions. | Rediscovery risk | Add a short note that maps track signals and reports hold deferred exceptions. |
| The tour did not route readers to generated facts. | Navigation gap | Add a maps route in "How to go deeper." |

## Outcome

The architecture tour remains appropriately short. The accepted fix is navigational rather than explanatory: point readers to the right source-of-truth layer and avoid duplicating glossary meanings, constitution gates, or report findings.

## Follow-Up

- Keep future architecture-tour updates concise.
- Do not turn the tour into a second docs index or constitution summary.
- If future reviews find duplicated concepts, use the Source-of-Truth Audit workflow before editing.
