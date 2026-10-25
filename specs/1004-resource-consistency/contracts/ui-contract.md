# UI Contract: Studio resource-list consistency and Home access

This is an observable UI contract for existing clients. It adds no external API or backend contract.

## Shared page hierarchy

- Secrets and Connections use the accepted Studio resource-list heading, search/filter, create, and action hierarchy.
- Each page has one visually clear primary header action for creating its resource.
- Existing domain columns, filter semantics, table structure, accessible names, operation permissions, and confirmation rules remain intact.
- Secrets exposes Edit as its primary row action. Rotate, Test, Revoke, and Delete remain reachable through a keyboard-accessible secondary action menu; destructive actions remain clearly separated.
- Connections retains Details as the row entry. Test, Revoke, and Delete remain in the existing details surface. Details retains existing focus restoration.
- Secret copy remains technical-name-only. Secret material must not be exposed in list UI, tooltips, fixture metadata, logs, screenshots, or proof artifacts.

## Secrets query and inventory behavior

- A single internal page-size value of 100 governs both the loaded page request and coverage copy.
- The server search and total count remain authoritative. Type, Store, Scope, and Status filters describe only loaded records.
- Coverage communicates the loaded subset and the continued ability to search server-side. Shown, loaded, and total counts must not conflict.
- A selected scope absent from loaded rows remains identified as absent from loaded results.
- Draft input, accepted query, and failed attempted query remain distinct. Applying a query changes the accepted query. Refresh and successful actions use the accepted query; retry uses the failed attempted query.
- Existing permission, busy, error, confirmation, and focus-return behavior remains for Edit, Rotate, Test, Revoke, and Delete.

## Connection creation and actions

- The accepted query before create remains the accepted query after create.
- Successful create shows the existing exact notice: `Connection '${created.name}' created.`
- Reload uses that same accepted query. The preferred created name may select a row only if the returned `items` contain that exact name. Do not clear the query to force a selection.
- Later successful and failed actions return to the accepted list context.
- Details remains the row entry, with Test, Revoke, and Delete in the existing detail surface and current action semantics.
- No new paging, total-count, outbound-request, remote-test, or remote-revocation claim is added. The built-in HTTP Test is a local readiness check; Revoke is Elsa-side state.

## Home access behavior

| Session | Needs attention destination | Destination |
|---|---|---|
| Authenticated with effective `workflows/runtime:read` | Available | `/workflows/instances?incidentHealth=active` |
| Authenticated without effective `workflows/runtime:read` | Unavailable | None |
| Authentication disabled | Preserve current behavior | Existing active-incident URL |

The gate affects only Needs attention. Continue retains the full authorized definition catalog and truthful counts in every session covered by its existing behavior.

## Browser fixture evidence

- Count each attempted POST, PUT, and DELETE, including attempts rejected by URL validation or a simulated handler error.
- The rendered counter must update in response to each attempt.
- Tests may assert the counter and status. They must not inspect or log request bodies, credentials, or secret values.
- Keep fixture responses and metadata outputs value-free.

## Compatibility

Existing endpoints, response shapes, permission authorities, and service operations remain unchanged. The UI reuses current Studio primitives and effective-permission matching behavior.
