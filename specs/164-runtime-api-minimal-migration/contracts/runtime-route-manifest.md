# Runtime route manifest

The owner publishes exactly 24 routes under `/runtime/workflows`:

- instance, instance page, executable, source-reference, provenance, execution, stimulus;
- dispatch list/get/redrive;
- activity execution, descendants, layout, value payload, incidents;
- diagnostics get/save;
- alteration submit/get/jobs page/job/cancel.

Every endpoint has an explicit HTTP method, stable operation name, `Elsa.Workflows.Runtime.Api` owner and tag, Minimal authoring metadata, security disposition, one catalog-owned permission action, and typed response/request metadata. The immutable route set is compared against `runtime-openapi-fastendpoints.json`; the HTTP case manifest is in `runtime-http-fastendpoints.json`.
