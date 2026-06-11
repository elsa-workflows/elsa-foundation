# Contract: Runtime Completion Propagation

Rules:

- Activity completion propagation is deterministic scheduler work.
- Completion propagation is not immediate recursive bubbling.
- Completion work is drained before unrelated activity scheduling work.
- Parent completion evaluation must identify both parent and completed child activity execution identities.
- Join/branch prerequisites are explicit contract data.
- Completion work is internal scheduler state, not fire-and-forget application events.
- Checkpoints remain named runtime boundaries outside the completion work item itself.
