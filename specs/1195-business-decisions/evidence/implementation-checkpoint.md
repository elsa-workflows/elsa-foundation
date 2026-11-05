# Implementation checkpoint — 5 November 2026

This is an intermediate delivery receipt for #3602, not final acceptance. The current draft PR remains [#3689](https://github.com/elsa-workflows/elsa-foundation/pull/3689).

## Completed focused evidence

| Slice | Observed result | Limit |
| --- | --- | --- |
| Pure evaluator | 91 discovered/executed, 91 passed, zero skipped | Includes leading XML signature, unnamed single-output and tagged input transport corrections |
| Cold computed-version package consumer | 46 assertions passed at `d9238f05e206dc0cda76afe2dbda0cdb7cad833b` | Actual calculator and cold PackageReference restore; no Workflow/EF/ASP.NET/Studio closure |
| Workflow adapter | 32 discovered/executed/passed | Focused contracts; actual-host restart is separately required |
| Authenticated start subject capture | Runtime 3355 passed; Publishing API 940 passed; BPMN 581 passed; zero skipped | Discovery placeholders Runtime3344/Publishing919 expand MemberData at execution; BPMN581 discovered |
| Decisions Design | 28 passed | Actual SQLite, including future-schema refusal; more provider/final integration checks pending |
| Decisions API | 15 passed | Includes signature-preserving lifecycle and exact exported bytes |
| Studio module | 29 passed; TypeScript, build, ESLint and stylelint passed | Final normal-host UX captures remain required |
| Normal Workbench startup | Fresh owned SQLite migration application and readiness passed | Preliminary dirty source, not exact committed final acceptance |
| Normal Studio journey | Author, five cases, regress/repair, publish, evaluate, reload, clone, concurrent409, exact import/export passed | Preliminary dirty source; final four-project screenshots/traces and independent visual review pending |

Private raw receipts, logs, TRX, package computation/consumer, browser traces, screenshots and ARIA snapshots are retained under the task's `decision-qualification` output directory. Final PR evidence will link downloadable CI artifacts. Behavioral negative controls prove numeric normalization, null wildcard semantics, canonical bytes, ANY conflicts, CAS, source privacy, identity ambiguity, history retention/disclosure, source hash integrity and source-preserving XML edits by a genuine red followed by restoration to green.

## Integration findings corrected

The ordinary host exposed missing EF module registration and schema stamps; these were corrected and all four initial provider migration sets regenerated before shipping. A real native workflow exposed default Object-expression deserialization losing lowercase tagged value fields; explicit JSON property names now preserve the portable input contract and an execution-materializer serializer regression test passes. Neither preliminary run was reported as final acceptance.

## Still required

- Exact final committed browser/runtime/restart evidence and current-grant/disclosure matrix.
- Pinned external-editor/reference-engine both-direction round trips with explicit disagreements.
- Final current package consumer, locked restores, architecture/maps/tier gates and affected whole suites.
- Independent correctness, standards and acceptance reviews; exact-head required CI and resulting-main verification.
