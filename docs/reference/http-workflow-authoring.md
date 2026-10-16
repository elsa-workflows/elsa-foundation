# HTTP workflow authoring

Source behavior for the HTTP usability change (#1177); this guide does not assert availability in previously published preview packages or images.

In Studio, create a workflow and choose **HTTP request and response** as its starting point. It contains a `POST /hello` endpoint connected to a JSON response. Change the path or response body in the inspector if needed, then publish. **Test HTTP request** calls the published endpoint and shows its actual status, content type and body. It executes a real workflow, so use appropriate test data. The initial request path includes the standard `/workflows/http` base path; adjust it if your host configures another path.

An endpoint's response choice is visible in its inspector and graph:

- **Wait for workflow response** (`Sync`) is the default for a newly published endpoint. A reachable Write HTTP Response supplies the response to that HTTP exchange.
- **Accept and run in background** (`Async`) returns 202 immediately after acceptance. Write HTTP Response records a response artifact for that run rather than returning it to the original caller. Studio offers a non-blocking hint when a background endpoint has a reachable response activity.

Output captures are optional. Bind a request output only when later activities need to read it.

## Understanding 202

The accepted response retains its `started` and `resumed` execution identifiers and adds `response.mode`, `response.reason` and `response.message`:

| Reason | Meaning |
|---|---|
| `background-accepted` | The endpoint accepts work in the background, including legacy bindings without response-mode metadata. |
| `workflow-response-not-produced` | Sync dispatch was accepted, but no workflow-authored response was produced during this exchange. The workflow may have suspended, reached no response activity, or dispatched elsewhere. Inspect the run to determine which occurred. |

A 202 confirms acceptance, not completion. Use the returned execution identifiers to inspect the run and its activities. Sync requests may wait for execution; a per-endpoint request timeout bounds that wait when configured.

## Existing workflows

Already-published trigger bindings and waiting bookmarks without response-mode metadata continue to mean Async. The enum's numeric values are unchanged. Every new publication/bookmark writes its selected mode explicitly.

HttpEndpoint declares activity version `1.1.0` for the Sync input-default contract. Catalog reconciliation adds this version rather than replacing the older version's hashed inputs. Existing compiled artifacts keep their pinned contracts.

Republishing an older draft that never specified a mode adopts the new Sync default. To retain background acceptance after republishing, explicitly select **Accept and run in background** first. Callers that consume the 202 execution identifiers should make that choice explicit.
