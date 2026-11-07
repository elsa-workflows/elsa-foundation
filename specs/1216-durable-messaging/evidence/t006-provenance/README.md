# T006 authored BPMN provenance component

6 November 2026. Canonical authored DefinitionId now travels through the trusted direct BPMN publication source using `BpmnModel.Authored.v1`. Material identity/pins/hashes stay separate. Runtime start/recovery, inspection, catalog and embedding share its owned matcher. Legacy `BpmnModel` keeps strict material matching and absent logical provenance. This implements the provenance portion of T006; Message declarations, delivery and model participation remain open.

| Check | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---|
| Added strict-constructor regression | 1 | 1 expected | 0 | [red](strict-json-red.trx.gz) |
| External generated-reader regression | 0 | 2 expected | 0 | [red](generated-reader-red.trx.gz) |
| Synthetic-ID mutation control | 1 | 2 expected | 0 | [red](canonical-mutation-red.trx.gz) |
| Exact source restored | 3 | 0 | 0 | [green](canonical-green.trx.gz) |
| Complete BPMN project | 591 | 0 | 0 | [whole](whole-bpmn.trx.gz) |
| Complete interchange project | 229 | 0 | 0 | [whole](whole-interchange.trx.gz) |

Strict JSON first rejected the newly required `definitionId`. A private optional JSON constructor fixed reflection reads but external source-generated readers silently lost authored identity. The final form preserves the original public constructor, uses validated init-only provenance and an authored factory. Both reflection modes and actual generated JsonTypeInfo are covered by the complete project. A separate wrong-source mutation replaced canonical authored identity with the synthetic material ID; cross-material identity and actual EF-backed provider-rebuild observations failed, then passed after byte-identical restoration. The whole project ran on those same restored production bytes before the mutation. Hashes and prototype/control source distinctions are in [manifest.json](manifest.json).

Tests use verified original archived fixtures restored by the repository's documented `tools/bpmn/restore_evidence.py` prerequisite. The new recovery observation rebuilds an in-process EF-backed provider after source retirement; it is not process-death, real host or Message-provider qualification. One earlier source-generator filter matched no cases and is excluded. Existing warnings remain visible; the preserved public constructors avoid new optional-overload/source-reader regressions.

No Message producer, API, authored activity, key-admission lane, physical source capability or UI is enabled. Native/BPMN Message behavior, historical interest, provider/topology/upgrade races and composed UX remain NOT RUN in the [acceptance matrix](../../acceptance-matrix.md).
