# Complete-PR round 2: retained child-control wire correction

Full-PR round 2 reviewed `e54ea7f57f79602182c22fcea89f4ccb3f3fc673` against main `7cd167e00a80c8c01d83c198200cde36058c3300`. [The changes-required receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6458619720) records one correctness finding; independent standards/acceptance lenses are clean. Both previous-round findings remain fixed.

A real terminal root control can commit while the workflow remains Running, before its later terminal checkpoint creates a delivery source. The previous converter let missing notifications become an empty list, missing authored failure code become null, and missing notification payload become null. The producer could then freeze that reduced batch into its first immutable source. Source equality after production cannot restore content already lost during checkpoint loading.

## Correction and compatibility

The existing version-1 batch converter validates the complete persisted authored-content members before constructing a batch. Notifications must be a present nonnull array; authoredFailureCode and each notification payload must be present, including explicit nulls. Record-member duplicates follow the configured serializer case rules and refuse shadowing. Validation honors the configured naming policy. Opaque payload objects are not traversed, so case-distinct payload keys and explicit JSON null remain intact. Ordinary constructors still accept their existing optional defaults; every emitted canonical batch records those values explicitly. Legacy absence of the entire optional control property remains valid. No public constructor, API declaration, dependency, package lock, storage schema or frozen authoring projection changes.

A malformed EF activity row refuses both read and replacement, retaining its exact original malformed JSON and revision. These negative copies are never used as positive upgraded-host evidence. The native producer regression obtains its root through actual runtime scheduling/committed checkpoints and proves the workflow has not reached a terminal checkpoint before testing incomplete wire; it is an in-process runtime boundary test, not a process-crash claim.

## Executed red and green

Both regression runs execute against unchanged production at the reviewed head: DispatchWorkflow **21 fail / 3 pass**, EF **4 fail / 0 pass**, no skips. All 25 intended refusal assertions fail because no exception is thrown. The three valid controls pass. No compilation failure is counted as a behavioral red. After the reader correction all **28** focused cases pass. The original red/green TRX and logs are portable.

| Current whole suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| BPMN | 372 | 0 | 0 |
| Runtime EF | 1,724 | 0 | 0 |
| Runtime | 3,043 | 0 | 0 |
| DispatchWorkflow | 363 | 0 | 0 |
| Activities Runtime | 720 | 0 | 0 |
| Architecture | 590 | 0 | 0 |
| Total | **6,812** | **0** | **0** |

Discovery and execution are reconciled by complete names. BPMN/EF/DispatchWorkflow match exactly. Runtime discovery has 3 deferred selectors expanding to 14 cases (3,032 selectors → 3,043 cases); Activities Runtime has 2 expanding to 4 (718 → 720); Architecture has 2 expanding to 11 (581 → 590). Every unmatched name is accounted for by its specific deferred selector in the reconciliation. Maps pass. The 28 newly added cases are DispatchWorkflow24 plus EF4. Focused cases are subsets, not additional total passes. No references changed, so prior locked solution-restore evidence remains source-specific rather than being presented as rerun. Prior Interchange130 and Studio suites likewise retain their prior source identities.

## Open delivery gates

This receipt qualifies the reader correction and affected whole suites. Relevant exact committed-code actual-host recovery must be refreshed after the fix commit, followed by complete-PR round 3. Full rounds completed2/6, no clean final round. T036 remains open until live qualification; T037/T038, actual required Full tier/other exact-head checks, protection/delivery/main and the third ADR0063 PR remain separate. #2898/#2907 and overall program production acceptance stay open; Orleans/task-runner follow-up remains deferred. Historical receipts are unchanged, not restamped.
