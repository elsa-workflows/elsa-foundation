# Connections Design Extension Points

## Extend

- `IDraftValidator`: `ConnectionDraftValidator` blocks publish when a bound connection is missing or the wrong type. A connection in `NeedsAuthorization` does not block (D7). It never checks the caller's permissions, so the validations panel, the dashboard and the promotion gate show every viewer the same findings; the right to bind a connection is `workflows/design:manage`, which the routes that write and promote workflow state require.
