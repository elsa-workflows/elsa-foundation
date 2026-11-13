# Delivery slices — 12 November 2026

#3604 remains active. [PR #3913](https://github.com/elsa-workflows/elsa-foundation/pull/3913) was closed without merging on 9 November. Its owner instruction requires stacked PRs of roughly 3,000 changed lines, one spec slice per PR. The original implementation is preserved at `424c09e279cd930203b25ce4781bce5c7e36940f`; nothing in that branch is presumed delivered.

The owner resumed delivery on 12 November and extended the review limit to as many rounds as needed within reason. This changes the numeric cap only. Review, CI, independent UX inspection, usable live provider access and resulting-main verification remain obligations.

## Integration order

Each PR is based on its predecessor until that predecessor lands. Split an oversized row further at the named boundary. The original task checkboxes record prepared implementation, not the state of main; this table records reintegration.

| Slice | Spec tasks / boundary | Current state |
| --- | --- | --- |
| 1 | T001–T005: spec and SDK-free query, resource, authoring contracts; tracked API and solution registration | Merged [#4217](https://github.com/elsa-workflows/elsa-foundation/pull/4217), merge `9cac434363`; contracts/spec now on main |
| 2 | T006–T009/T011: native validation, named event fan-in and read bounds; query tests (resource authorization moves to tested persistence/resource slice) | Prepared; 55 query/composition cases pass after causal regression controls; review pending |
| 3 | T008–T009/T024: resource service/authorization and EF definitions, CAS, revisions and SQLite schema; resource and normal consumer-context tests | Prepared SQLite/resource slice; tests/review in progress, HTTP and relational providers deferred |
| 4 | T024/T033: bounded collection/reuse reads, relational migrations and provider/security tests | Prepared; SQLite paging/migration tests pass, provider/review/Architecture pending |
| 5 | T010/T016/T023/T026/T030–T031: authenticated native APIs, audience resolution, resource HTTP tests and catalogs | Preserved; not reintegrated |
| 6 | T013–T015/T021: reusable runtime inspection and Workflow query source; numerical tests | Preserved; not reintegrated |
| 7 | T017/T025/T027/T035/T037: native Studio transport, editor, collections and renderers; split component and page integration as needed | Preserved; not reintegrated |
| 8 | T019/T026/T032/T039: Studio sharing/history/recovery, accessibility and Home preservation tests | Preserved; not reintegrated |
| 9 | T018/T020: Weaver authoring and existing Copilot adapter boundary; reconcile current Weaver names and test independently | Preserved; not reintegrated |
| 10 | T040–T042: independently packaged Acme business dataset and package-only consumer qualification | Preserved; not reintegrated |
| 11 | T022/T028–T029/T034/T038–T039/T043/T046: actual composed host and live provider qualification; split native and provider harnesses if needed | Preserved harness; qualification unavailable on new source |
| 12 | T012/T044–T049: host composition, remaining integrator documentation, final acceptance and resulting-main reconciliation | Pending all applicable predecessors |

## Before API exposure

Code Review approved slice1 without blockers and noted that the current store list materializes full documents and embedded history. Before enabling the native API, add bounded store paging and avoid accumulating history during collection/reuse scans; preserve current ACL filtering, stable continuation invalidation, source withdrawal and no privileged result caches. Requalify real-store collection/history/CAS controls. This is an additional resource sub-slice, not a completed capacity guarantee. The token-last ListAsync convention is corrected in the first resource implementation slice while its API is Unshipped.

## Drift and evidence

- Main advanced by 134 commits since the preserved head, including the Agent-to-Weaver package/type/wire rename. Reconcile implementations and regenerate locks from each slice's actual graph; do not restore the old whole-solution lock set.
- No replacement implementation PR or Dashboard backend contract was found on main during resumption. Main's existing Home widgets remain independent of this stack.
- Historical results under `evidence/` name their original sources. Former `/tmp` proof artifacts are no longer available; they cannot serve as fresh or downloadable acceptance evidence.
- Configured Copilot credentials previously reached discovery but returned 403; account restoration and live inference remain unqualified. Never expose tokens in reports or check in credentials.
- The open main-CI incident #4177 refers to earlier commits. Current-base checks are inspected separately; pending, skipped or absent checks are not called green.

This first slice adds contracts only. No source is registered, no API is mapped, no UI is enabled and no provider is invoked by it. Behavior tests belong with their implementations, rather than assertions that mirror data-record declarations.
