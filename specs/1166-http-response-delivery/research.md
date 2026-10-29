# Research decisions
- Existing incidents have Warning severity but are execution-affecting continuation state. Use observational projection facts instead, preserving successful completion and avoiding incident strategies.
- Extend existing JSON activity-inspection persistence, not declared durable values or process-local ExecutionEvidence. The latter explicitly has no durability guarantee.
- Retain opaque request identity in the typed request model; no HttpContext/request sink enters the activity attempt.
- Reconcile at transport closure and after scheduler drains. Persist closure before scanning completions; a later completion is reconciled by its post-commit drain. Missing/ambiguous origins stay Unknown.
