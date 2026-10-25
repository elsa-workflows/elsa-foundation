# Microsoft 365 connector

`Elsa.Integrations.Connectors.Microsoft365` is an Elsa 4 connector package of Microsoft Graph
v1.0 activities (#3330). It follows the Slack connector (#3322 / #3324) on
`Elsa.Integrations.Sdk`.

Activities bind to the foundation `http` connection and call `ApplyConnectionAsync`. Authenticate
with the generic `oauth2` scheme from `Elsa.Integrations.Connectors.OAuth2` (authorization-code +
PKCE, plus `offline_access`) using **your own Entra app registration**. This package has no
Microsoft-specific connection type, no second `DelegatingHandler`, and no Valence-owned app.

The host composes `AddMicrosoft365Connector()` and, for OAuth, `AddOAuth2Connector()`. This package
does not reference the OAuth2 assembly.

## Connection settings

Create an `http` connection with scheme `oauth2`:

| Setting | Value |
|---|---|
| `authorizationUrl` | `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` |
| `tokenUrl` | `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` |
| `scopes` | the delegated scopes for the activities you use, plus `offline_access` |
| `clientId` | your Entra app's client id |
| client secret | connection secret field or a named credential |
| `allowedHosts` | `graph.microsoft.com` (or the national-cloud Graph host) |

Redirect URL: `{PublicBaseUrl}/_elsa/connections/oauth/callback`.
Start the flow with `POST /connections/{name}/oauth/authorize`.

Set each activity's **Graph base URL** when the tenant is a national cloud. The default is
`https://graph.microsoft.com`.

v1 is delegated `/me` (optional `User id` for a shared mailbox or calendar). App-only
(client credentials) is out of scope even though the generic OAuth2 scheme can issue that grant.

## Activities

| Activity | Graph call | Delegated scope | Inputs | Output |
|---|---|---|---|---|
| Send Mail | `POST /me/sendMail` | `Mail.Send` | to, cc, bcc, subject, body, body type, save to sent items, user id | none |
| List Messages | `GET /me/mailFolders/{folder}/messages` | `Mail.Read` | folder, filter, top, max items, user id | list of messages |
| Create Event | `POST /me/events` | `Calendars.ReadWrite` | subject, start, end, time zone, body, location, attendees (JSON), user id | event |
| List Events | `GET /me/calendarView` | `Calendars.Read` | start, end, top, max items, user id | list of events |
| Get User | `GET /users/{id-or-upn}` | `User.ReadBasic.All` | user id | `{found, user}` — 404 is not a fault |
| Upload File | `PUT …:/content` or `createUploadSession` | `Files.ReadWrite` (`Sites.ReadWrite.All` for a SharePoint drive id) | drive id, item path or file name, content, conflict behavior, user id | drive item |
| Download File | `GET …/content` | `Files.Read` | drive id, item id or item path, user id | `{fileName, contentType, content}` |
| Post Teams Channel Message | `POST /teams/{team}/channels/{channel}/messages` | `ChannelMessage.Send` | team id, channel id, content, content type, attachments (JSON) | `{id, webUrl}` |

Subject, body, file bytes, Teams content and Teams attachments refuse secret bindings
(`EchoedToOutput`). JSON inputs (To/Cc/Bcc when they start with `[`, attendees, attachments)
fault with `invalid_input` when they are not a JSON array.

## Errors

- Graph errors become an activity fault whose code and message are only `error.code` and
  `error.message`, truncated. The raw body, `innerError`, tokens and client secrets never appear.
- `InvalidAuthenticationToken` and `invalid_grant` throw `ConnectionUnavailableException`
  (`ConnectionUnavailable`) so the connection can be re-authorized. A 401 retries once when the
  bound scheme has refreshable state (the same seam as `SendHttpRequest`).
- `Authorization_RequestDenied` and `ErrorAccessDenied` (HTTP 403) name the activity's required
  scope in the fault message.
- HTTP 429, 503 and 504 honour `Retry-After` for 3 attempts, capping each wait at 30s and the
  total wait at 60s, then fault with `rate_limited`. Other 5xx responses are retryable faults.

## Upload

Files up to 4 MB use `PUT …:/content` with the connection Authorization header. Larger files
create an upload session. Chunk PUTs go only to an `uploadUrl` that is https on a Microsoft
host, do **not** call `ApplyConnectionAsync`, and treat any 2xx as success. Do not put
`*.sharepoint.com` or `*.1drv.com` on the connection unless you want other authenticated calls
to those hosts.

Paging follows `@odata.nextLink` only when the link is https on the configured Graph host.

## Out of scope

Triggers (Graph change notifications / webhooks — #3325), app-only auth, Excel, Planner, and
Elsa 3 shims (ADR 0045).
