# Durable publication and run discovery

Executing source `1e608cb8a71eb6d9fb02ac5052ed912c9c5a55fb` contains main `69d90f4f0ec21b965de6df72645eacf008b9fbe5`. [18-file raw archive](https://github.com/elsa-workflows/elsa-foundation/releases/download/contributor-preview-2026-10-07.1/elsa-bpmn-catalog-host-1e608cb8a.zip), [verified receipt](receipt.json). The uploaded archive and every member were downloaded and verified on the existing archival release; no new release/tag.

The authored Workbench control now discovers its immutable publication through `GET bpmn/publications` and its retained Running execution through `GET bpmn/executions` after an actual process restart. It completes the exact work, restarts again and replays completion without another revision. The physical verifier passes for all four whole Design/Runtime stores: revision1→2, exact authored version/source/full pin/authority/incarnation, receipts and zero native wrapper rows. All16 products were captured; every Elsa product matches the executing source. The earlier [publication checkpoint](../publication/README.md) retains its independent source-bound evidence.

| Check | Actual outcome |
|---|---|
| Initial unsupported catalog operation |23failed,0passed,0skipped; raw before-change TRX retained. |
| Implemented read controls |23passed,0failed,0skipped. |
| Expanded registration/budget controls plus existing command authorization |First32passed/1test assertion failure: capability declarations normalize relation order. Corrected assertion;33passed,0failed,0skipped. |
| Source and execution paging |One provider page per request, default50/max500; empty filtered pages retain protected continuation without a refill loop. |
| Cursor and disclosure controls |Purpose/subject/tenant/scope/filter/budget changes refused before provider access. Initial source live clock retained; each item reauthorized, denied source never loads material. Foreign scope/tenant/protocol and inconsistent/retired/unavailable source filtered. Provider failure/cancellation propagate. |
| Actual composed host |1passed,0failed,0skipped, including catalog discovery after restart and final replay. |
| Full server locked restore |Passed after refreshing68 affected lock files. External package entries, versions and hashes unchanged. Earlier pushed-head PR shard/platform restore failures were genuine NU1004 stale transitive capability-Core edges; retained CI log is not reported as green. |
| Generated solution-filter check |Passed; no filter changes needed for the new API contract edge. |

T010 is complete. T009/T015 remain pending their actual runtime-only portion. Inspection, real Studio browser acceptance, both saved-Studio families, ETL/claims, final package/API gates and independent full PR review remain open. This same-source API/host witness is not changed-binary upgrade, Studio or wider feature acceptance. Full review rounds0; PR stays draft/Partial.
