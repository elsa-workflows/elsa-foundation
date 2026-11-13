# Complete source capture

This optional package coordinates exact owner source acquisition with the shared bundle builder and a durable archive.
It references lightweight release/archive contracts, with no Design implementation, workflow compiler or Runtime engine.
Owner strategies and archive persistence are selected by host composition. See the [extension catalog](EXTENSION_POINTS.md).

Sources hold their callback boundaries through actual owner verification, bounded ZIP creation and archive commit.
Downloads then use the archive reader, independently of the original source catalogs and runtime GC. A failed or
ambiguous operation is not reported as created; the caller can reconcile a completed archive by capture ID.
Owner callbacks are invoked once and are never retried by this service.

Member contributors expand exact retained containers in bounded batches. Each contributor/container key and payload digest
is processed once; identities and supported container codecs are validated at composition. Contributors return only members.
They cannot select additional roots or declare graph facts, and their retention callbacks remain open through archive commit.
The final owner inspection must establish the complete closure, original identities and root authority independently.

This package alone implements no native/BPMN/ETL source acquisition. Controlled framework cases exercise orchestration
with the actual shared verifier and SQLite archive; actual owner retention/import and composed release journeys require
their separate qualification. Target resource mappings, execution captures, staging, activation and rollback are separate.
