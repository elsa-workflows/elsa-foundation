# Business decisions qualification plan

These are required future acceptance scenarios, not commands/results for an implemented product. Exact project/host/API commands will be added with implementation. Keep expected outcomes here and actual evidence on #3602/its single PR.

1. Build the affected projects and run their complete suites, comparing discovered/executed counts. Validate profile parser/evaluator truth tables, hit policies, bounds, cancellation, actual trace and deterministic identity. Causal controls remove a guard or alter matching/selection and must turn its meaningful test red, then green on restoration.
2. Pack actual computed-version Core/evaluator artifacts into an isolated feed/cache. An ordinary PackageReference application evaluates the approval table with no EF/ASP.NET/workflow/Studio dependencies. Verify the transitive envelope and direct-result/explanation matrix.
3. Start owned temporary Workbench/Studio hosts with fresh disposable Decisions storage and real identity. Preserve unrelated hosts/stores. Exercise two tenants and read/manage/publish/evaluate/test/history/source/sensitive grants across every API, including export and validation failures.
4. In normal Studio, create the approval decision with amount/category and four rules; save/reload, validate, save cases, deliberately regress a threshold, inspect failed expectation, repair, publish/reload and clone-to-draft. Exercise actual keyboard navigation, 320px table scrolling, light/dark, dirty-navigation, concurrent-save conflict and stale-response protection.
5. On real provider connections, race draft/case saves and publication against edits. Prove CAS, immutable version/pointer atomicity, duplicate completed-receipt replay, divergent-key conflict and tenant isolation. Interrupt a synchronous test before receipt commit, mutate/delete its case, and verify retry conflicts rather than selecting newer content. Lose a response after receipt commit, change the case, and verify the existing exact receipt replays before mutable reads. Verify default summary-only persistence and explicit unavailable detailed payload; repeat with opt-in protected history retention. Record providers actually run; do not extrapolate SQLite to four engines.
6. Pin external editor and evaluator references. Test supported XML in Elsa→external→Elsa and external→Elsa→external directions. Compare exact supported configuration and results against standard-derived cases; preserve covered diagrams/extensions. Unsupported constructs must be retained safely or refused, never made runnable by silent omission. Tool disagreements remain visible.
7. Author normal native Sequence/Flowchart and BPMN bound-task callers for the same decision. Publish with specific/latest-at-publication selection and compare the direct/native/BPMN matrix. Suspend, publish a newer decision, restart/retry with Design storage unavailable, and prove the original artifact/semantics remain selected.
8. Verify explanations report actual matched versus selected rules and safe policy violations, without protected cell/input/output content leaking through normal trace, comparisons, errors, logs or history. Invocation permission alone must not grant protected output exposure. Include exact tagged numbers above 2^53 and 34-digit values across Studio/case/history/direct/native/BPMN boundaries.
9. Run architecture guard (`bash tools/architecture/restore-ci-project-graph.sh`, then the complete architecture test project), generated maps (`dotnet run --project tools/maps/Elsa.Maps.Generator -- check`), tier check and locked solution restore after reference changes. Use build-slot queue and check machine load for timing failures.
10. Independently review correctness, standards and acceptance on exact final diff. Post PR evidence with counts/causal controls. Mark ready, require exact-head CI, merge under current org authorization, then verify resulting-main CI/Studio/security as applicable. Mark spec Implemented in the completing PR and close #3602 only after every acceptance row is evidenced.

## Acceptance map

| Issue success criterion | Required witness | State |
| --- | --- | --- |
| Studio create/edit/validate/test/publish/reuse | 3–4, 7 | Pending |
| DMN round-trip/result equivalence and explicit unsupported cases | 1, 6 | Pending |
| Same direct/native/BPMN decision and explanations | 2, 7 | Pending |
| Data/explanation only, effects with caller | 1–2, 7–8 | Pending |
| Saved-case deliberate regression | 4 | Pending |
| Version/concurrency/input/policy/error/dependency/bounds | 1, 5, 7 | Pending |
| Tenant/permissions/sensitivity | 3, 8 | Pending |
| Packaged/normal-host/conformance qualification | 2–10 | Pending |
