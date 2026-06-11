# Requirements Checklist: Runtime Schedule Activity State Creation

- [x] The slice does not reopen locked runtime execution architecture decisions.
- [x] `ActivityExecution` remains the durable identity for one concrete execution of one executable node.
- [x] Runtime scheduling references executable artifact and executable node IDs only.
- [x] Activity invocation, checkpoints, bookmarks, retry, and durable persistence providers remain out of scope.
