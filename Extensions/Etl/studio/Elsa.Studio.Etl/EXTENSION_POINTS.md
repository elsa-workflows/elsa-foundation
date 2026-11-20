# ETL Studio integration

Owner project: `Elsa.Studio.Etl`; feature: `EtlStudio`; module: `etl`.

The module contributes navigation and routes for `/etl/definitions` and `/etl/runs` through the public Studio
feature-area API. It uses Studio's authenticated HTTP transport and the backend capability document; it does
not require a server runtime assembly in the browser host.

The property editor registered for `etl-published-selection` handles `RunEtl.Pipeline`. It loads bounded pages
of published definitions and versions and stores the exact canonical `EtlPublishedSelection`, including its
full content hash. It preserves an existing selection outside the currently loaded page. General Workflows
Studio contains no ETL-specific branch or alternate activity catalog.

It also contributes `etl-native-run` (cross-domain) to the optional `workflow.run.related-links` slot
(`api.workflowRunRelatedLinks`) for `RunEtl` occurrences whose authorized `etl.run-reference/1` state view names
a run. The link opens the exact ETL run on the same backend, with a return route limited to the originating
native run; hosts without the slot or the runtime capability show no link.

The structured editor supports the v1 file topology in [spec 255](../../../../specs/255-finite-file-etl/spec.md)
and the v2 connector topology in [spec 996](../../../../specs/996-http-etl/spec.md) and
[spec 1190](../../../../specs/1190-etl-jsonl-input/spec.md). Connector drafts select HTTP, CSV, JSON Lines or SQL table sources ([spec 1198](../../../../specs/1198-etl-sql-source/spec.md): table, columns and
key; [spec 1199](../../../../specs/1199-etl-sql-watermark/spec.md): an optional watermark column) or RabbitMQ queue
sources ([spec 1201](../../../../specs/1201-etl-rabbitmq-source/spec.md): queue, JSON fields and a message maximum), optional CSV lookup, and HTTP, JSONL or CSV routes ([spec 1191](../../../../specs/1191-etl-csv-delivery/spec.md)), or a keyed SQL table for accepted
records ([spec 1197](../../../../specs/1197-etl-sql-destination/spec.md)) or a RabbitMQ exchange and routing key
([spec 1200](../../../../specs/1200-etl-rabbitmq-delivery/spec.md)), through scoped alias/role/kind choices.
Its local draft is separate from the saved revision. Validation and explicit publication operate on the saved
revision/version/hash tuple. Published artifacts are read-only. Binding choices are scoped aliases, never
host paths. Integer and decimal literals retain their JSON numeric representation through the lossless client.

Runtime inspection reads retained run/partition facts and safe diagnostics. V2 inspection uses the separately
advertised connector endpoint, distinguishes HTTP page/item/exhaustion from file ranges (a SQL source is a retained snapshot read by file range, with no admission digest), and presents durable
retry state, owed receipts and accepted route-body counts without inventing an HTTP total or percentage. It does not probe storage or claim
that a cancelled run has stopped every external operation. API failure/missing capability must remain visible;
an empty page is not a substitute for unavailable or forbidden access.

Continuous runs ([spec 1192](../../../../specs/1192-etl-continuous-http/spec.md)) are authored with "Run continuously"
and a poll interval, offered only for an HTTP source with token or next-link pagination; switching to an unsupported
source drops the policy. Inspection shows the run mode, a completion request, and a caught-up shard's next poll apart
from a retry wait. "Complete run" appears only while the host reports a preparing or running continuous run whose
completion has not been requested, so Studio keeps working against hosts that predate the `etl-run-complete` relation,
which is resolved only when the action is used.

See [the ETL module guide](../../README.md) for host composition and recovery limits, and
[file component evidence](../../../../specs/255-finite-file-etl/evidence/studio-components.md),
[connector component evidence](../../../../specs/996-http-etl/evidence/connector-studio-checkpoint.md), and
[real Workbench/Studio acceptance](../../../../specs/996-http-etl/evidence/http-live-acceptance-checkpoint.md)
for their respective verified source checkpoints.

## Cross-domain contributions

`Elsa.Studio.Etl` contributes to Studio-owned surfaces only through the public SDK: the `etl-published-selection`
property editor for `RunEtl.Pipeline`, and `etl-native-run` in the
[workflow run related-links slot](../../../../Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md#workflow-run-related-links-sdk--elsastudiohosting).
Neither adds an ETL branch to Workflows Studio. Both are offered only when the selected backend advertises the
required ETL capabilities.
