# Glossary Coverage Audit

Status: point-in-time audit for keeping stable architecture terms centralized in the glossary without promoting provisional planning labels too early.

## Purpose

Check whether key Elsa Brain Operating Model terms are centralized in [root glossary](../glossary/root.md) and [Elsa glossary](../glossary/elsa.md), and identify terms that should remain in reports, program-goal files, or entrypoint guidance instead of becoming glossary entries.

## Inputs Reviewed

- [AGENTS.md](../../AGENTS.md)
- [Root glossary](../glossary/root.md)
- [Elsa glossary](../glossary/elsa.md)
- [Knowledge inventory](knowledge-inventory.md)
- [Skills stabilization audit](skills-stabilization-audit.md)
- [CShells composition evidence](cshells-composition-evidence.md)
- [Feature dependency map](../maps/feature-dependency-map.md)
- [Program goals index](../program-goals/README.md)

## Findings

| Term or term family | Classification | Resolution |
|---|---|---|
| `feature identity` | Stable architecture vocabulary used by constitutions, skills, maps, and composition evidence | Add a root glossary entry. |
| `extension point` | Stable architecture vocabulary used by catalogs, maps, and implementation skills | Add a root glossary entry. |
| `program goal`, `drift guard`, `source-of-truth layer` | Operating-model vocabulary already owned by `AGENTS.md`, `docs/reference/agent-preferences.md`, and `docs/program-goals/` | Do not add glossary entries unless these terms start appearing outside operating-model docs. |
| `provider-neutral` | Ambiguous beside the existing runtime/backing-technology `Provider` term | Replace AI-agent/workflow uses with `AI-provider-neutral`; keep runtime-provider uses such as umbrella module wording unchanged. Do not add a generic glossary entry. |
| CShells dependency/settings labels such as `required activation`, `optional companion`, `host-loading`, `feature-bound`, and `compile-time-only reference` | Provisional classification language | Keep in [CShells composition evidence](cshells-composition-evidence.md) until the classification work unit approves stable terminology. |

## Outcome

The glossary now anchors the two stable missing terms. Provisional composition labels remain in reports so the future configuration and feature dependency classification unit can revise, merge, split, or reject them without creating glossary drift.

## Follow-Up

- During future source-of-truth audits, check whether operating-model terms have escaped into general architecture docs.
- After configuration and feature dependency classification is approved, revisit whether any CShells labels have become durable glossary terms.
