# S-9 local validation checkpoint, 24 November 2026

This is an incomplete implementation checkpoint, not final acceptance or review convergence. CI waits are deferred by
the coordinator. No CI pass is claimed. S-2 owns workflow null/missing/undefined policies and ValueEnvelope admission
and writes; these repairs preserve JSON tokens at the serializer boundary only.

Clean published source `87ae0a2dd58e0c4ca4b898e6671d2e88bbb0d187` passed the forced canonical locked restore and shipping
Architecture rebuild. Whole results: Serialization 128 passed/1 skipped; Runtime 5007; Publishing API 2089;
BPMN Interchange 292; Dashboard Resource 88. Architecture had 862 passed, 2 failed and 1 Windows-only skip. Its failures
were a typed golden-fixture construction error and a reviewed wire-graph difference. Both failed states are retained.

Independent source review of that graph found two real defects: nullable defaults bypassed raw writes, and form-field
configuration erased present null before the owner's existing object-only validation. The prepared repairs use raw
writes and the shared nullable-element converter, retaining the existing read/presence rules.

Recorded candidate patches based on that head produced:

| Control or affected whole suite | Result |
|---|---|
| Activity and executable default raw spelling/indentation, original writer | 4 expected assertion failures |
| Same default controls, repaired writer | 4 passed |
| Actual form-draft route, original nullable member | 1 expected assertion failure |
| Same route, repaired member; raw object and missing configuration | 1 passed |
| Runtime whole | 5009 passed |
| Activities Design whole | 815 passed |
| Runtime API whole | 739 passed |
| BPMN publication owner controls | 87 passed |
| BPMN Interchange whole | 293 passed |

The first combined default test did not compile because Runtime tests do not reference Activities Design. That failed
attempt is retained. Tests were split into their owning projects before the assertion-level negative and repaired runs.

The five authored BPMN publication vectors retain independent Python preimages and SHA-256 results for operation
identity and supplied request intent. The actor-fixture construction repair changes none of the 37 existing expected
owner digests. Its full shipping-owner rerun remains pending at this checkpoint.

Private local logs, TRX files, source patches and failed attempts are retained under `.agent-prefs/test-results/` and
the owned Linux validation helper. They do not substitute for final exact-head downloadable PR evidence. The exhaustive
hash register/guard, graph pin convergence, retained provider/fixture proof, both composed Studio hosts and independent
visual acceptance remain outstanding, followed by final conflict resolution and fresh independent local review.
