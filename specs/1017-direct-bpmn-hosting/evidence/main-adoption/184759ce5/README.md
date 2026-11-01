# Adopted connection validation main3480

Adopted main184759ce53bc251ccc81b07ec3980d804ad8fd76 normally at merge833e586e74dbe3a7c4dc362ef73bdfe3f7349273, preserving the existing #2898 branch/claim/PR. Main3480 validates connection inputs/credentials and improves its Studio dialog/errors. The shared SDK adds only a payload.error fallback in error-response parsing. All BPMN, Runtime, shared EF and Design production trees are byte-identical to qualifiede3ec219fc; no frozen authoring/state or successful request behavior changes.

At merge833e586e7 the whole Connections project passes157/157, zero skips, and the affected Studio SDK registry module passes34/34, zero skips. Raw commands/logs/TRX are retained. This adds integration checks; it does not relabel the6165-case runtime/four actual recovery journeys frome3 or the actual Studio browser source4f46 as this head. No new browser run is claimed. Final reviewed-head required CI remains due.

Full-PR round3 will review the adopted head. The existing magnitude owner card/needs:decision, deferred GitGuardian39189012/scan and protected/post-merge delivery remain open. #2898/#2907/#2908/program Partial and #3242/Orleans/task-runner deferrals are unchanged.
