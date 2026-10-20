# Connections Design Extension Points

## Extend

- `IDraftValidator`: `ConnectionDraftValidator` blocks promotion when a bound connection is missing or the wrong type. A connection in `NeedsAuthorization` does not block (D7). It never checks the caller's permissions, so the validations panel, the dashboard and the promotion gate show every viewer the same findings; the right to bind a connection in a workflow draft is `workflows/design:manage`, which the Workflow Design routes that write or promote drafts require.
