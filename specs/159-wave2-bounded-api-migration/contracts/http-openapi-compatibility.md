# HTTP and OpenAPI Compatibility Contract

The immutable FastEndpoints-before cases cover every target route with anonymous 401, authenticated success, representative malformed/validation/conflict errors, BPMN XML in JSON, multipart upload binding, paging, polling, location headers, idempotency, and execution-evidence delete/read behavior. `HttpEvidenceCapture` consumes the response body and canonicalizes JSON. `OpenApiEvidenceCapture` projects the actual document and schema references.

After mapping, the same cases run against the Minimal API host. `CompatibilityComparer` is the gate: route/method, binding, status, content type, relevant headers, body, errors, paging, and terminal state must match unless the reviewed contract explicitly allows the change. No fixture is generated from the after host.
