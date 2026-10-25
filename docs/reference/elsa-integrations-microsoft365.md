# Microsoft 365 connector (#3330)

`Elsa.Integrations.Connectors.Microsoft365` ships Microsoft Graph v1.0 activities on
[`Elsa.Integrations.Sdk`](../../Extensions/Integrations/README.md). Auth is the foundation
`http` connection plus the generic [`oauth2` scheme](elsa-integrations-oauth2.md). Bring your
own Entra app registration. There are no Valence client ids, redirect defaults, or app
manifests in this repository.

## Create an Entra app

1. Open the [Entra admin center](https://entra.microsoft.com/) and register an application.
2. Under **API permissions**, add the delegated Microsoft Graph permissions your workflows
   need (see the activity table in the
   [package README](../../Extensions/Integrations/src/Connectors/Microsoft365/README.md)).
   Grant admin consent when the tenant requires it.
3. Create a client secret (or use a certificate later; v1 uses a client secret).
4. Set the redirect URL to:

   `{PublicBaseUrl}/_elsa/connections/oauth/callback`

   `PublicBaseUrl` is `ConnectionsOptions.PublicBaseUrl` (no trailing slash). Example:
   `https://workflows.example.com/_elsa/connections/oauth/callback`.

## Elsa connection

Create an `http` connection with scheme `oauth2`:

- `authorizationUrl` = `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize`
- `tokenUrl` = `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`
- `scopes` = space-separated delegated scopes plus `offline_access`, for example
  `Mail.Send Mail.Read Calendars.ReadWrite Calendars.Read User.ReadBasic.All Files.ReadWrite Files.Read ChannelMessage.Send offline_access`
- `clientId` = the Entra app client id
- client secret = the connection secret field or a named credential
- `allowedHosts` = `graph.microsoft.com` (or the national-cloud Graph host)

Start authorization with `POST /connections/{name}/oauth/authorize`. The generic OAuth2
scheme sends `Authorization: Bearer`.

National clouds: set each activity's **Graph base URL** (default
`https://graph.microsoft.com`) and the Entra authorize/token URLs for that cloud.

## Suggested scopes

| Scope | Activities |
|---|---|
| `Mail.Send` | Send Mail |
| `Mail.Read` | List Messages |
| `Calendars.ReadWrite` | Create Event |
| `Calendars.Read` | List Events |
| `User.ReadBasic.All` | Get User |
| `Files.ReadWrite` | Upload File (OneDrive) |
| `Sites.ReadWrite.All` | Upload File to a SharePoint drive |
| `Files.Read` | Download File |
| `ChannelMessage.Send` | Post Teams Channel Message |
| `offline_access` | refresh the delegated grant |

v1 is delegated `/me`, with an optional User id for a shared mailbox or calendar. App-only
(client credentials) is out of scope.

## Upload File

Files up to 4 MB use `PUT …:/content`. Larger files create an upload session. The connector
PUTs chunks to Graph's returned `uploadUrl` **without** the connection `Authorization`
header, and only when that URL is https on a Microsoft host. Tokens never appear in activity
outputs, logs or faults.

## Not in v1

Graph change notifications and webhooks need host endpoints, which the SDK activities-only
rule does not allow. They come with the trigger work in #3325. Excel and Planner are also
out of scope.
