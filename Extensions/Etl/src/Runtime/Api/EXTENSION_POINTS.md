# ETL Runtime API extension points

Owner project: `Elsa.Etl.Runtime.Api`
Domain: `Elsa.Etl`

`IEtlRuntimeOperations` is the scoped operation seam dispatched by the four NativeEndpoints endpoints. The default
implementation derives scope from a validated normalized principal and the host's ordinary persistence scope. Route
and query values cannot set tenant or partition authority. The API requires `etl/runs:read` for inspection and
`etl/runs:cancel` for cancellation; it returns bounded summaries and never exposes host file paths, binding handles,
artifact payloads, native result addresses, output object keys or worker owner identities. Run detail includes
retained source/lookup revisions and expected digests; prepared snapshot digests/lengths describe the last
verified preparation, not a fresh integrity check. They remain absent before the relevant admission/preparation.

The feature depends on `ApiCapabilities` and shared ETL API transport helpers only. The host supplies Foundation
Identity, the ordinary persistence scope and an `IEtlRuntimeStore` provider. No ETL design database, compiler,
worker feature, Tasks feature or runtime activation service is required. Missing runtime persistence is reported as
unavailable rather than as an empty run list.

Run and partition pages are limited to 1–200 entries and use signed cursors bound to their kind, exact scope,
resource and page size. Configure `CursorSigningKey` consistently across replicas for stable pagination; its
process-local default deliberately does not promise continuity across restarts. The key is a secret host setting,
not an authored value or response field.
