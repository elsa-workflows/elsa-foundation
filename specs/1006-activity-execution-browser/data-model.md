# Data model

Collection query: workflow execution ID; optional executable node, exact parent, ancestor; limit/cursor; optional selected execution anchor; authorization/tenant binding. Predicates combine conjunctively. Ancestor includes descendants, not the ancestor itself.

Entry: existing summary plus execution sequence, occurrence position and adjacent execution IDs. Page: matching count, bounded entries and opaque previous/next continuations. Positions use ascending scheduling time/sequence/ordinal identity within the entire matching query.

Inspection state: run/context identity, selected node, exact selected execution, ordered exact ancestor scope frames, retained selections per node/scope. Backend or authorization changes invalidate all retained data.

No stored entity changes. Generic ancestry derives from committed `Provenance.ParentActivityExecutionId`. Retry lineage is not inferred from iteration identity.
