# Studio Preferences HTTP Contract

## Routes

### Read

- Method/path: `GET /_elsa/studio/preferences/{namespace}`.
- Required header: `X-Elsa-Studio-Host-Id`.
- Authorization: canonical Foundation `Any(*, studio.preferences.read)` policy.
- Success: `200`, existing `StudioPreferenceDocument` JSON, and quoted `ETag`.
- Existing failures: authentication `401`, authorization `403`, unknown namespace/document `404`, malformed host or read validation `400`.

### Write

- Method/path: `PUT /_elsa/studio/preferences/{namespace}`.
- Required header: `X-Elsa-Studio-Host-Id`.
- Body: existing web-JSON names for `schemaVersion` and `value`; route namespace is authoritative.
- Preconditions: exactly `If-None-Match: *` for create or one quoted `If-Match` revision for update.
- Authorization: canonical Foundation `Any(*, studio.preferences.write)` policy.
- Success: `200`, updated `StudioPreferenceDocument` JSON, and quoted `ETag`.
- Existing failures: authentication `401`, authorization `403`, unknown namespace `404`, malformed host `400`, stale/conflicting revision `412`, quota `413`, validation or malformed preconditions `422`.

## Compatibility rule

The migrated implementation must match the committed FastEndpoints-before observations for status, media type, headers, JSON/body, ProblemDetails, binding, and consumed OpenAPI operation. No difference is accepted implicitly.
