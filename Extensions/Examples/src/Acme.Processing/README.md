# Acme.Processing reference contribution

This reference module demonstrates an explicitly exposed business dimension through the same public Dashboard contracts as Elsa's Workflow contribution. Its pure domain contracts are in `Core/`; `Module/` owns ordinary processing records, authorization, native create/read endpoints and question contributions. It references no Workflow or Dashboard implementation package. Neither shared query orchestration nor Weaver needs an Acme-specific branch. Both example projects are excluded from normal package publishing (`IsPackable=false`); the owned local package witness explicitly opts in to packing its frozen inputs.

Enable the `AcmeProcessing` feature alongside `StudioDashboardApi`. The default actor adapter uses the host's canonical Dashboard identity mapping; another trusted identity integration can replace `IProcessingActorContext`. This fixture is not a customer integration or evidence that native workflow variables became searchable.

The [extension-points catalog](Module/EXTENSION_POINTS.md) lists replaceable contracts, default implementations, registration lifetimes and cross-domain contributions.

## Deliberate data exposure

`customerCategory` is a required typed field accepted by the ordinary record capability. The module deliberately advertises it as a business dimension only with current category-read permission. `processingTimeMs` derives from completed/failed records with valid start/completion pairs. Processing records, uncaptured completion and inverted pairs do not contribute a duration; a valid zero duration is zero.

Failure rate belongs to this module: failed records divided by **finished** records in the started cohort. In-flight work is excluded from its denominator, unlike Workflow's started-run rate. No finished denominator yields null. Period comparisons and HAVING use exact fractions before numeric transport rounds to twelve decimal places. Native controls may compose new filters, grouping, field aggregates, comparison predicates and sorting rather than selecting a developer-authored widget.

The dataset is `acme.processing.records`, schema `processing-v1`. Metrics are `processing.record-count/v1`, `processing.finished-count/v1`, `processing.average-time-ms/finished-valid-pairs-v1` and `processing.failure-rate/finished-denominator-v1`. The discovery response is authoritative for currently available fields and operations.

## Ordinary native records and current access

POST `/_acme/processing/records` accepts `customerCategory`, `startedAt`, nullable `completedAt`, status `processing`/`completed`/`failed`, and optional canonical `readerIds`. Tenant, owner and ID are server-issued. Unsupported fields, inconsistent timestamps and oversized input are rejected. GET that collection returns authorized finite pages; GET `/{id}` returns only a currently readable record. Views omit category values when field authority is absent.

Required permissions are independent:

- `acme/processing:create` creates an owned record in the current tenant.
- `acme/processing:read` is combined with the record's current owner/readers ACL.
- `acme/processing/category:read` governs category disclosure and use in filters/grouping/sorting.
- `acme/processing-questions:read` permits question discovery/aggregation; it grants no record or field rights.

Each source operation uses Foundation's canonical permission service. Every contributing record passes current tenant, record and requested-field decisions **before** counts, groups, comparisons or pagination. Sharing a question conveys no access to its author's records. A current category denial removes that field from discovery; per-record category restrictions exclude that record from category questions. Ordinary reads and questions use the same module authority.

## Storage and evidence limits

The default reference store is ephemeral and loses processing records on restart. `IProcessingRecordReader` and `IProcessingRecordWriter` are separate replacement ports for an application's storage integration; each replacement must preserve tenant identity, immutable record ownership, finite paging and domain failure semantics. This module makes no durable-business-storage or complete-history claim. Results explicitly report unknown collection/retention coverage.

Drill recomputes current group/HAVING membership, then returns bounded authorized records from the selected current/baseline period. Integrity proofs and cursors are process-local; after restart or an instance change, refresh generates a new proof. There is no result cache, export, generated widget code, external database querying or model integration in this reference module.

The whole suite in `Extensions/Examples/tests/Acme.Processing.Tests` seeds through the ordinary create capability and independently checks retail/enterprise means, counts, module-owned denominator semantics, tenants, record/category grants, native endpoints and drill. Package-only archive qualification remains pending in the delivery stack; a project-reference test alone is not package-install evidence. These native cases do not prove live-provider behavior or full #3604 delivery.

## Cross-domain contributions

The module implements `IDashboardDatasetExtension`, `IDashboardQueryValidationExtension`, `IDashboardQueryExecutionExtension` and `IDashboardQueryDrillExtension`. Generic Dashboard named events dispatch those contracts. It also contributes the four Foundation permission declarations. Domain data and semantics remain with Acme; the shared assistant remains resource and dataset agnostic.

Drill separately authorizes its actual record projection and selected absolute current/baseline interval before reading, while retaining original query, per-record and category decisions. Shell activation and plain-host startup both validate replacement cardinality without resolving request readers.
