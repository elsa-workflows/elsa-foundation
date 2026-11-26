# Studio Preferences API Contract

## Scope

Every authenticated operation derives subject and tenant from the existing auth
session and requires a non-secret Studio host ID supplied by Studio. Canonical
key: `(subjectId, tenantId, studioHostId, namespace)`.

## Read

```http
GET /_elsa/studio/preferences/{namespace}
X-Elsa-Studio-Host-Id: studio-primary
```

`200`:

```json
{
  "namespace": "dashboard",
  "schemaVersion": 1,
  "revision": "rev-7",
  "value": {},
  "updatedAt": "2026-07-13T10:00:00Z"
}
```

`404` means no document exists and the namespace owner supplies defaults.

## Conditional Write

```http
PUT /_elsa/studio/preferences/{namespace}
X-Elsa-Studio-Host-Id: studio-primary
If-Match: "rev-7"
Content-Type: application/json
```

```json
{
  "schemaVersion": 1,
  "value": {}
}
```

Returns the new document/revision. Creation uses `If-None-Match: *`.

## Errors

- `400`: malformed host ID, schema version, or body.
- `401`: authenticated subject/tenant unavailable.
- `403`: namespace/permission denied.
- `404`: namespace is not registered or document absent on read.
- `409` or `412`: stale revision/precondition failure.
- `413`: namespace/document quota exceeded.
- `422`: namespace validation or migration rejected the value.

## Governance

- Registered namespaces only.
- Owner defines current schema version, defaults, validator, migration, and size
  quota within system cap.
- Store owns revision CAS and update timestamp.
- Preference values contain metadata only.
- Dashboard namespace owns layout/visibility/sizes/refresh/widget settings.
- Attention namespace owns snooze records.
- Anonymous/unavailable fallback is device-local and uses the same scope shape
  with `anonymous` subject; it is never presented as synchronized.
