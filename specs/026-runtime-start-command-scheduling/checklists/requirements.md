# Requirements Checklist: Runtime Start Command Scheduling

- [x] The slice does not reopen locked runtime execution architecture decisions.
- [x] Start command handling remains scheduler work, not recursive execution.
- [x] Runtime scheduling references executable artifacts and executable node IDs only.
- [x] Activity execution, bookmarks, checkpoints, retry, and distributed provider behavior remain out of scope.
