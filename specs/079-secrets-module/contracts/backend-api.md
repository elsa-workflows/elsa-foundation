# Backend API Contract: Secrets Module

All routes are under the Elsa API route prefix used by the host. Responses that expose metadata are safe by default and never include raw secret values, encrypted payloads, configuration values, or provider-private payload metadata.

## Permissions

Permission names:

- `secrets:read`
- `secrets:write`
- `secrets:update-value`
- `secrets:delete`
- `secrets:test`
- `secrets:use`
- `secrets:import`
- `secrets:export`

Local unsecured hosts may allow anonymous development access, but endpoints must be annotated or structured so these permissions can be enforced by secured hosts.

## List Secrets

`GET /secrets?search=&typeName=&storeName=&scope=&status=&page=&pageSize=`

Response:

```json
{
  "items": [
    {
      "id": "01J...",
      "name": "smtp-password",
      "displayName": "SMTP Password",
      "description": "Password used by SMTP settings",
      "typeName": "text",
      "storeName": "encrypted",
      "scope": "email",
      "tags": ["smtp"],
      "status": "active",
      "currentVersion": 2,
      "createdAt": "2026-06-24T08:00:00Z",
      "updatedAt": "2026-06-24T09:00:00Z",
      "expiresAt": null
    }
  ],
  "totalCount": 1
}
```

## Get Secret

`GET /secrets/{name}`

Returns a single metadata model or `404`.

## Create Secret

`POST /secrets`

Request:

```json
{
  "name": "smtp-password",
  "displayName": "SMTP Password",
  "description": "Password used by SMTP settings",
  "typeName": "text",
  "storeName": "encrypted",
  "scope": "email",
  "tags": ["smtp"],
  "value": "submitted-only-on-create-or-rotate",
  "configurationKey": null,
  "expiresAt": null,
  "metadata": {}
}
```

Response is metadata-only.

Rules:

- `name` is normalized and immutable.
- `value` is accepted only for stores/types that support direct value submission.
- `configurationKey` is accepted for configuration-backed references.

## Update Metadata

`POST /secrets/{name}`

Request:

```json
{
  "displayName": "SMTP Password",
  "description": "Updated safe description"
}
```

Response is metadata-only.

## Rotate Secret

`POST /secrets/{name}/rotate`

Request:

```json
{
  "value": "new-submitted-value",
  "configurationKey": null,
  "expiresAt": null,
  "metadata": {}
}
```

Response is metadata-only.

## Revoke Secret

`POST /secrets/{name}/revoke`

Response is metadata-only or `404`.

## Delete Secret

`DELETE /secrets/{name}`

Returns success without value material. Deleted secrets are excluded from ordinary lists and picker queries.

## Test Secret

`POST /secrets/{name}/test`

Response:

```json
{
  "succeeded": true,
  "code": "ok",
  "message": "Secret resolved successfully."
}
```

Rules:

- Never returns the resolved value.
- Failure messages use safe codes.

## Descriptors

`GET /secrets/descriptors`

Response:

```json
{
  "types": [
    {
      "name": "text",
      "displayName": "Text",
      "description": "Plain text secret value.",
      "editorHint": "secret-text",
      "supportedStoreNames": ["encrypted", "configuration"]
    }
  ],
  "stores": [
    {
      "name": "encrypted",
      "displayName": "Elsa encrypted store",
      "description": "Stores values in Elsa-managed protected payloads.",
      "capabilities": ["read", "write", "delete", "test", "versioned"],
      "isReadOnly": false
    }
  ]
}
```

## Picker

`POST /secrets/picker`

Request:

```json
{
  "search": "smtp",
  "typeNames": ["text"],
  "storeNames": ["encrypted"],
  "scope": "email",
  "activeOnly": true
}
```

Response:

```json
{
  "items": [],
  "canCreateInline": true
}
```

Rules:

- Defaults to active-only.
- Returned items are metadata-only.
- `canCreateInline` reflects user permissions and compatible store/type capabilities.

## Import/Export Boundary

This slice must reserve contracts for safe reference export and conflict-aware import. Full encrypted value movement may be implemented later, but raw values must never be included by default.
