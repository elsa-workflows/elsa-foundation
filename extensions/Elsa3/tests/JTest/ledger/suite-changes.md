# Suite changes

One row per changed assertion or step after the engine-neutral refactor, any change to `templates-elsa3/` after it,
and any raised wait in either template set (spec 1235 FR-012, task T029). Each row needs coordinator approval.

| # | Suite and step | Change (before → after) | Reason and engine | Approved |
|---|---|---|---|---|
| W1 | The 15 fixed `wait` steps in 12 suites (`composition/*` ×11 in 9 suites, `events/basic-event`, `logging/logging`, `single-outcome` ×2) | `{"type": "wait", "ms": N}` → `{"type": "use", "template": "elsa-wait-for-persistence", "with": {"ms": N}}` (same name, same N, including `{{ $.env.dispatchWaitTimeMilliseconds }}`); new template in `templates-elsa3/` (waits N) and `templates-elsa4/` (does not wait) | The waits covered Elsa 3's persistence lag; whether an engine needs one is the engine's template's business, so Elsa 4 does not sleep. Both engines | Coordinator request in the implementing session, 2026-11-24 ("move those to the elsa 3 templates, and the elsa 4 templates dont do that"); verified on 3.6.3 in run 15 |
| W2 | `http/request-body-tests.json`, dataset `xmlContent` (both steps) | `"expectedStatusCode": 500` → `"expectedStatusCode": "{{ $.env.xmlRequestBodyStatusCode }}"`; new environment value `xmlRequestBodyStatusCode`: `500` in `environments/elsa3.json`, `202` in `environments/elsa4.json` | An XML request body: Elsa 3 cannot parse it and answers 500, Elsa 4 accepts it and answers 202. The suite keeps both engines' behaviour, so it stays engine-neutral and the 3.6.3 baseline is unchanged. Both engines | Coordinator, 2026-11-25 ("Accept Elsa 4's 202"); verified on 3.6.3 (run 30, 4/4) and Elsa 4 (run 31, 4/4) |
