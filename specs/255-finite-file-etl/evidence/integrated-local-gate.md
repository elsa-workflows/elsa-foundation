# Finite ETL integrated local gate

2026-10-21 local checkpoint after `b47e59b332ea4823e7cf86e5b52029ddc43fa226`. These results qualify the integrated working tree containing the Studio module, native HTTP/preflight tenant corrections and final recovery tests. They do not substitute for fresh-head CI or verification after integration with newer main.

| Validation | Executed result |
|---|---|
| Full Release ETL suite | 367 passed, zero failed/skipped |
| Full native Workflow Runtime suite after HTTP tenant fix | 2,443 passed, zero failed/skipped |
| Runtime API suite | 180 passed, zero failed/skipped |
| Publishing API after preflight tenant correction | 812 passed, zero failed/skipped |
| Architecture, with new source paths visible to tracked-file guards | 583 passed, zero failed/skipped |
| ETL frontend | 40 passed in seven files; production build passed |
| Studio host feature discovery | 3 passed, zero failed/skipped |
| Workbench build | Succeeded, 11 warnings and zero errors |
| Studio host and full frontend workspace build | Succeeded |
| Real Workbench HTTP | Sequence and Flowchart continuation, typed physical output, rejected and filtered rows passed |
| Real Studio Chromium | One expected pass, zero unexpected/skipped/flaky; author/publish/select/execute/inspect and Sequence continuation |
| SQLite initial migrations and reopen | Executed within ETL suite; other providers unsupported, not passing/skipped evidence |
| Dependency map and solution filters | Regenerated changes reviewed; freshness checks passed |
| Catalog links | Zero broken links across 140 files |
| Locks | Frozen filtered frontend install and Workbench restore passed; reviewed lock changes add ETL project closure/lossless-json only |

See [browser receipt/screenshots](studio-browser.md), [HTTP and precise tenant mutation evidence](workbench-http.md), [recovery/identity matrix](recovery-identity-formats.md), [provider boundaries](provider-reopen.md) and [frontend qualification](studio-components.md).

## Final review pass

Root reviewed the incremental production, composition, recovery-test and evidence diff. Two bounded read-only worker reviews independently covered (1) native HTTP/preflight tenant propagation plus binding approval and (2) Studio lifecycle, paging, exact selection and live harness. Neither found a remaining actionable in-scope defect. Root checked the three screenshots, successful report totals, exact selection/native timeline and host teardown independently. Workers ran no tests; the executed results above belong to root verification.

Earlier actionable findings were corrected and verified: native start/preflight tenant propagation, the test fixture's same-name authentication handler, stale picker loading, normal publication/run navigation, and uncontained long-token overflow. Removing the two final preflight propagation arguments caused all four route/tenant regressions to fail with null tenant; exact restoration passed the full Publishing API suite. The initial architecture catalog failure was due to untracked new source targets; adding intent-to-add entries and rerunning all architecture tests passed. Failed intermediate attempts remain failed evidence.

## Remaining delivery and limitations

Fresh-head CI, integration with newer main where necessary, normal merge and resulting-main checks remain required. No current-main health or completed delivery is inferred from these local results. Parent #2908 and Program #2892 remain incomplete. No live browser cancellation, transient Test Run, non-SQLite ETL provider, generic worker pause, or exactly-once physical effect qualification is claimed. Historical timeout causes remain unproved. Further Orleans work stays deferred. The future task runner is recorded in the program's follow-up section, outside this leaf.
