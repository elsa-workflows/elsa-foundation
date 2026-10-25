# Slack connector

`Elsa.Integrations.Connectors.Slack` is an Elsa 4 connector package of Slack Web API
activities. It is the first product connector on `Elsa.Integrations.Sdk` (#2825 / #3322).

Activities bind to the foundation `http` connection and call `ApplyConnectionAsync`, the same
seam as the SDK `Ping` template and `SendHttpRequest`. Authenticate with the generic `oauth2`
scheme from `Elsa.Integrations.Connectors.OAuth2` (authorization-code + PKCE) using **your own
Slack app**. This package has no Slack-specific connection type, no second `DelegatingHandler`,
and no Valence client ids or app registration.

The host composes `AddSlackConnector()` and, for OAuth, `AddOAuth2Connector()`. This package
does not reference the OAuth2 assembly.

## Connection settings

Create an `http` connection with scheme `oauth2`:

| Setting | Value |
|---|---|
| `authorizationUrl` | `https://slack.com/oauth/v2/authorize` |
| `tokenUrl` | `https://slack.com/api/oauth.v2.access` |
| `scopes` | the bot scopes for the activities you use (table below) |
| `clientId` | your Slack app's client id |
| client secret | connection secret field or a named credential |
| `allowedHosts` | `slack.com` |

Redirect URL: `{PublicBaseUrl}/_elsa/connections/oauth/callback`.
Start the flow with `POST /connections/{name}/oauth/authorize`.

A static bot token can instead use the foundation `http` `bearer` scheme. Slack's
`oauth.v2.access` returns `token_type: "bot"`; the generic OAuth2 scheme sends
`Authorization: Bearer` unless you set `tokenHeaderScheme`.

## Activities

| Activity | Slack method(s) | Bot scope(s) | Inputs | Output |
|---|---|---|---|---|
| Send Message | `chat.postMessage` | `chat:write` (`chat:write.public` to post to channels the bot has not joined) | channel, text, blocks (JSON, optional), unfurl flags (optional) | `channel`, `ts` |
| Reply in Thread | `chat.postMessage` with `thread_ts` | `chat:write` | channel, thread ts, text, blocks (optional), reply broadcast (optional) | `channel`, `ts` |
| Update Message | `chat.update` | `chat:write` | channel, ts, text, blocks (optional) | `channel`, `ts` |
| Add Reaction | `reactions.add` | `reactions:write` | channel, ts, emoji name | none |
| List Channels | `conversations.list` | `channels:read` (+ `groups:read` / `im:read` / `mpim:read` per type) | types, exclude archived, limit, max pages | list of `{id, name, is_private, is_archived}` |
| Lookup Channel | `conversations.info` | `channels:read` / `groups:read` | channel id | `{id, name, is_private, is_archived}` |
| Lookup User by Email | `users.lookupByEmail` | `users:read`, `users:read.email` | email | `{found, user}` — `users_not_found` is not a fault |
| Upload File | `files.getUploadURLExternal`, POST bytes to `upload_url`, `files.completeUploadExternal` | `files:write` | channel (optional), filename, content (`byte[]`), title, initial comment, thread ts (optional) | file id(s) |

Text, blocks, file bytes and the upload initial comment refuse secret bindings (`EchoedToOutput`).

## Errors

- Slack `ok:false` becomes an activity fault whose code is the Slack `error` string.
- `missing_scope` includes the `needed` scope in the message.
- `invalid_auth`, `token_revoked` and `account_inactive` throw `ConnectionUnavailableException`
  (`ConnectionUnavailable`) so the connection can be re-authorized.
- HTTP 429 honours `Retry-After` for 3 attempts, capping each wait at 30s and the total wait
  at 60s, then faults with `rate_limited`.
- Slack `warning` values are logged only.

## Upload

The POST to Slack's returned `upload_url` does **not** call `ApplyConnectionAsync` and does
not send `Authorization`. Do not put `files.slack.com` on the connection unless you want other
authenticated calls to that host.

## Out of scope

Triggers (Events API, Socket Mode, slash commands, interactivity), Elsa 3 shims, a Slack-specific
auth handler, and Valence-hosted Slack app defaults.
