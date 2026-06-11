# Requirements Checklist: Runtime Activity Start State Transition

- [x] The slice does not reopen locked runtime execution architecture decisions.
- [x] `ActivityExecutionId` remains the durable identity for one concrete execution of one executable node.
- [x] Runtime start work references executable artifact, executable node, and activity execution IDs only.
- [x] Activity invocation, checkpoints, bookmarks, retry, graph traversal, and durable persistence providers remain out of scope.
