# ETL Studio integration

Owner project: `Elsa.Studio.Etl`; feature: `EtlStudio`; module: `etl`.

The module contributes navigation and routes for `/etl/definitions` and `/etl/runs` through the public Studio
feature-area API. It uses Studio's authenticated HTTP transport and the backend capability document; it does
not require a server runtime assembly in the browser host.

The property editor registered for `etl-published-selection` handles `RunEtl.Pipeline`. It loads bounded pages
of published definitions and versions and stores the exact canonical `EtlPublishedSelection`, including its
full content hash. It preserves an existing selection outside the currently loaded page. General Workflows
Studio contains no ETL-specific branch or alternate activity catalog.

The structured editor supports the finite topology in [spec 255](../../../../specs/255-finite-file-etl/spec.md).
Its local draft is separate from the saved revision. Validation and explicit publication operate on the saved
revision/version/hash tuple. Published artifacts are read-only. Binding choices are scoped aliases, never
host paths. Integer and decimal literals retain their JSON numeric representation through the lossless client.

Runtime inspection reads retained run/partition facts and safe diagnostics. It does not probe storage or claim
that a cancelled run has stopped every external operation. API failure/missing capability must remain visible;
an empty page is not a substitute for unavailable or forbidden access.

See [the ETL module guide](../../README.md) for host composition and recovery limits, and
[component evidence](../../../../specs/255-finite-file-etl/evidence/studio-components.md) for the current verification scope.
