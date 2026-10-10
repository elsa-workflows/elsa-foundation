# Data model

- Workflow execution state: existing lifecycle status and pinned executable; no lifecycle transition changes.
- Scheduler poison: existing durable fault, retry disposition and metadata; causal execution/node identity is captured from the actual work payload when attributable. Unassociated infrastructure evidence remains nullable.
- Incident: existing severity/status/resolution history plus optional activity execution/node and structured input failure context. Active = status neither Resolved nor Suppressed; blocking = status Blocking, respecting pending retry semantics.
- Input inspection evidence: failed attempt linked to input key, exact activity execution and incident; successful persisted values unchanged; legacy absence is unavailable/unrecorded evidence rather than proof evaluation was never attempted.
- Run summary: historical incidentCount plus additive activeIncidentCount and blockingIncidentCount; permissions/unavailable state never fabricated as zero.
- Run query: optional incidentHealth (active/blocking/none); health applies before total count and stable cursor paging and intersects the existing authorized filters.
- Graph overlay: aggregates active incident counts per authored activity; exact occurrence lives in selection. Parent-contained counts describe descendants and do not label parent as failed.
