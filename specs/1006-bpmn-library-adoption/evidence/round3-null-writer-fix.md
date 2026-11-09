# Complete-PR round 3: preserve required nullable control writes

[Full-PR round3](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6459014162) reviewed exact `35ed1215b9a4d91385015a6d2e467ffa2da0ad83` against main `7cd167e00a80c8c01d83c198200cde36058c3300`. Independent correctness and standards reviewers found the same null-omission writer regression; acceptance was clean. It is one deduplicated finding. Earlier findings remain fixed.

The preceding strict reader correctly requires explicit nullable authoredFailureCode and notification payload in a present control batch. The writer still inherited null omission from caller options. Elsa's shipped Serialization feature registers an IPayloadSerializer whose actual default is WhenWritingNull, so otherwise valid control could serialize and then fail same-serializer deserialization.

## Minimal correction and actual serializer regressions

Two existing nullable members now use the established JsonIgnore(Condition=Never) convention: notification Payload and private WireBatch.AuthoredFailureCode. Their explicit nulls survive WhenWritingNull and WhenWritingDefault. The strict persisted reader remains intact. In-memory constructors and CLR signatures remain unchanged; legacy absence of the whole optional control property remains valid. Opaque payload meaning, naming/case rules, package/schema and frozen authoring are unchanged. The normal non-omitting runtime persistence/transport wire already emitted these fields; the two attributes preserve that existing wire rather than migrating it.

Five cases configure the real SerializationFeature in a service collection and resolve its actual registered IPayloadSerializer. They assert its actual WhenWritingNull policy and exercise both string and JsonElement serialization/deserialization for empty terminal, nullable notice, running notice, nullable classification and fully populated control. This uses the shipped serializer/options implementation rather than a test serializer; it is a DI/wire test, not a host startup-contribution test. Twelve additional cases cover default/web/snake naming under WhenWritingNull/WhenWritingDefault.

On unchanged production at the reviewed head, **16 fail and 1 passes**, zero skips. Four actual registered-serializer cases and all twelve omission/naming cases fail with the strict reader's expected missing-member exception. The fully populated actual-serializer case passes. No compilation failure counts as behavioral red. After the two attributes all **17** pass, with required null members present and byte-stable same-serializer round trips. Original TRX/logs are portable.

| Current whole suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| BPMN | 372 | 0 | 0 |
| Runtime EF | 1,724 | 0 | 0 |
| Runtime | 3,043 | 0 | 0 |
| DispatchWorkflow | 380 | 0 | 0 |
| Activities Runtime | 720 | 0 | 0 |
| Architecture | 590 | 0 | 0 |
| Total | **6,829** | **0** | **0** |

Every discovered/executed name is reconciled; any deferred theory selector and its explicit case expansion are recorded in the machine reconciliation. Maps pass. The 17 new cases belong to DispatchWorkflow; focused results are subsets of the whole total. Earlier malformed-wire refusal, real committed-root split and EF refusal/replacement cases remain in these passing whole suites. No project reference, API declaration or lock file changed, so earlier locked solution restore and Interchange/Studio suite evidence retain their own source identities rather than being restamped.

## Delivery boundary

This receipt qualifies the writer fix and affected whole suites. A fresh exact committed standard build and relevant real-host/Studio refresh follow the fix commit, then complete-PR round4. Full rounds completed3/6, no clean final verdict. T036–T038, actual required Full tier/other exact-head checks, protection/delivery/main and separate third ADR0063 remain gates. #2898/#2907/program production acceptance remains open; Orleans/task-runner follow-up deferred. Existing host/legacy/saved-start/acknowledgment and Studio receipts remain unchanged and source-specific.
