# BPMN Engine

## Status

Active. Reactivated on 14 October 2026 as a named owner exception to
[ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus).

## Area

Executable BPMN 2.0 support: the `BpmnProcess` composite activity, BPMN XML and BPMNDI interchange, the runtime
seams the engine consumes, and the shared `valence-works/bpmn` library that holds the BPMN semantics for both
Elsa 3 and Elsa 4.

## Steward(s)

Sipke plus the active BPMN control-room session and its worker agents.

## Purpose

Make Elsa's BPMN support competitive with a Camunda 7-class engine. BPMN stays an Elsa composite activity next
to Flowchart, not a replacement for it. Models authored elsewhere import and run with the same meaning, or are
refused. The platform capabilities BPMN needs, such as job workers, user tasks, message correlation and FEEL,
are built as general Elsa modules that Flowchart can use too.

## Decisions (13 October 2026)

1. **One semantics core.** Elsa 4 moves onto `Bpmn.Semantics`, the package Elsa 3 already ships on, and its
   own token engine is deleted (ADR 0063).
2. **FEEL plus Zeebe mapping.** FEEL becomes an Elsa expression provider. `zeebe:` task definitions, IO
   mappings and timers map into Elsa bindings. Simple Camunda 7 JUEL is translated; the rest is refused.
3. **Camunda 7-class throughput on a single node.** An explicit target is set together with Runtime
   Efficiency, and no partitioned execution is designed for now.
4. **Refuse rather than degrade.** An imported model whose meaning would change does not publish.

## In scope

- The BPMN runtime module (`Essentials/src/Activities/Bpmn/`) and its interchange adapter.
- Upstream changes to `valence-works/bpmn` that the engine needs, and the package bumps that consume them.
- Studio BPMN authoring and run views (`Studio/`).
- Designs for the platform modules BPMN depends on: job workers and external tasks, user tasks and forms,
  message correlation with buffering, and connectors.

## Out of scope

- An operator console. That belongs to Valence Control.
- Zeebe wire-protocol compatibility, CMMN and analytics.

## Active objectives

- [#2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735): switch to `Bpmn.Semantics`, in three
  PRs. First, a parity diff that upstreams anything only Elsa 4 has. Then the switch, with the work binder and
  a guard test. Then ADR 0063 marked Accepted.
- [#2736](https://github.com/elsa-workflows/elsa-foundation/issues/2736): refuse to import or publish BPMN whose
  meaning changed on import.
- Library follow-ups: [valence-works/bpmn#27](https://github.com/valence-works/bpmn/issues/27) (cycles that
  never wait). [valence-works/bpmn#26](https://github.com/valence-works/bpmn/issues/26) shipped in 0.3.0.

## Linked surfaces

- Gap analysis and roadmap: https://claude.ai/artifact/7mh6yzenWfMvt8smFCnHL6
- Earlier program record: [docs/plans/bpmn-phase2-events-tier.md](../plans/bpmn-phase2-events-tier.md)
  (Phases 1 to 3).
- [ADR 0063](../adr/0063-bpmn-moves-to-a-host-agnostic-library.md) and the `valence-works/bpmn` repository.

## Removal or completion conditions

Complete when Elsa 4 runs on the shared semantics core, imported models are refused instead of silently
degraded, and the platform-module designs are either built or explicitly dropped.
