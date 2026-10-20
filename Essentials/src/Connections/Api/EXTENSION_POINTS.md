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

`connections:manage`, `connections:bind-secret` and `connections:test`, contributed by `ConnectionsPermissionContributor`,
guard administration: create, update, revoke, delete and the OAuth redirect URI need `connections:manage`, binding an
existing secret or a named credential to a field also needs `connections:bind-secret`, and testing needs
`connections:test`.

The list, descriptors, picker and single-connection reads require `workflows/design:read`, which Workflow Design owns
and contributes, or `connections:manage`, because whoever manages connections must see them. The Connections API does
not reference the Workflow Design API, so it names that permission itself; an architecture test compares the name with
the owner's declaration. Binding a connection in a workflow needs no
Connections permission: it is authorised by `workflows/design:manage` on the routes that write workflow state.
