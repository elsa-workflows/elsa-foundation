# Research

- Decision: use committed inspection projections for generic history. Rationale: Do has one execution and distinct body child executions, whereas any node may also repeat via back-edges. Alternatives: activity allowlist or inferring iterations from timestamps would misidentify occurrences.
- Decision: keep existing descendants API semantics. `ActivityExecutionHierarchyPager` and EF hierarchy reader filter execution scope (reusable boundary), not arbitrary ancestor. It cannot safely implement generic loop scopes.
- Decision: parameterized provider-aware recursive JSON read over existing EF inspection rows. Rationale: current relational projections lack executable node/parent columns; supported providers all support JSON extraction, recursive CTEs and window functions. No schema or historical backfill needed. Alternative: new ancestry projection would need migration/backfill and writer changes; client run-wide loading violates bounded browsing.
- Decision: scheduling time/sequence/ordinal identity ordering and live-view paging follow existing runtime store semantics. Positions/counts are authoritative per response; no cross-request snapshot claim.
- Decision: retain exact activity inspection/redaction and existing boundary navigation. No new diagnostic capture or condition history.

- Live ordering correction: `WorkflowScheduleActivitySchedulerWorkHandler.ResolveExecutionSequence` falls back to a stable identity hash. Existing Looping data confirms hash-valued sequences, so occurrence order uses scheduled time first, sequence and ordinal identity only for ties. No runtime scheduling change.
