# Connections API Extension Points

The Connections API maps the REST surface for service connections and the OAuth callback.

## Endpoints

- `GET/POST /connections` list and create.
- `GET /connections/descriptors` connection types and authentication schemes.
- `POST /connections/picker` Studio picker.
- `GET/PUT/DELETE /connections/{name}` metadata.
- `POST /connections/{name}/revoke` and `/test`.
- `GET /connections/{name}/oauth-redirect-uri` builds `{PublicBaseUrl}/_elsa/connections/oauth/callback`.
- `GET /_elsa/connections/oauth/callback` (D2, phase 2 groundwork). Anonymous. Unhandled until an OAuth scheme claims it.

## Permissions

`connections:read`, `connections:use`, `connections:manage`, `connections:test`, contributed by `ConnectionsPermissionContributor`.
