# Connections Design Extension Points

## Extend

- `IDraftValidator`: `ConnectionDraftValidator` blocks publish when a bound connection is missing or the wrong type. A connection in `NeedsAuthorization` does not block (D7).
