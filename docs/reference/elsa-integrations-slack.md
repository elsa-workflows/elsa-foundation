# Slack connector (#3322)

`Elsa.Integrations.Connectors.Slack` ships Slack Web API activities on
[`Elsa.Integrations.Sdk`](../../Extensions/Integrations/README.md). Auth is the foundation
`http` connection plus the generic [`oauth2` scheme](elsa-integrations-oauth2.md). Bring your
own Slack app. There are no Valence client ids, redirect defaults, or app manifests in this
repository.

## Create a Slack app

1. Open [https://api.slack.com/apps](https://api.slack.com/apps) and create an app **From scratch**.
2. Under **OAuth & Permissions**, add the bot token scopes your workflows need
   (see the activity table in the [package README](../../Extensions/Integrations/src/Connectors/Slack/README.md)).
3. Set the redirect URL to:

   `{PublicBaseUrl}/_elsa/connections/oauth/callback`

   `PublicBaseUrl` is `ConnectionsOptions.PublicBaseUrl` (no trailing slash). Example:
   `https://workflows.example.com/_elsa/connections/oauth/callback`.
4. Install the app to the workspace when you are ready to test. Copy the **Client ID** and
   **Client Secret**.

## Elsa connection

Create an `http` connection with scheme `oauth2`:

- `authorizationUrl` = `https://slack.com/oauth/v2/authorize`
- `tokenUrl` = `https://slack.com/api/oauth.v2.access`
- `scopes` = space-separated bot scopes, for example `chat:write channels:read users:read users:read.email files:write reactions:write`
- `clientId` = the Slack app client id
- client secret = the connection secret field or a named credential
- `allowedHosts` = `slack.com`

Start authorization with `POST /connections/{name}/oauth/authorize`. Slack's token response
uses `token_type: "bot"`; the generic OAuth2 scheme still sends `Authorization: Bearer`.

A bot token you already have can use the `http` `bearer` scheme instead of OAuth.

## Suggested scopes

| Scope | Activities |
|---|---|
| `chat:write` | Send Message, Reply in Thread, Update Message |
| `chat:write.public` | Send Message / Reply to a public channel the bot has not joined |
| `reactions:write` | Add Reaction |
| `channels:read` | List Channels, Lookup Channel (public) |
| `groups:read` / `im:read` / `mpim:read` | List Channels when those types are requested |
| `users:read`, `users:read.email` | Lookup User by Email |
| `files:write` | Upload File |

User scopes (`user_scope`) are not configured in v1.

## Upload File

Slack returns a pre-signed `upload_url`. The connector POSTs file bytes to that URL **without**
the connection `Authorization` header. Tokens never appear in activity outputs, logs or faults.

## Not in v1

Events API, Socket Mode, slash commands and interactivity need host endpoints, which the
SDK activities-only rule does not allow. File a follow-up for triggers.
