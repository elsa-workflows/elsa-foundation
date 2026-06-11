# Contract: Runtime Remove Execution Pool

Runtime execution ownership is represented by `IWorkflowExecutionAgentProvider`.

`IWorkflowExecutionPool` is removed because it lacks:

- pinned executable artifact identity;
- cancellation;
- checkpoint boundary semantics;
- single-writer mailbox ownership;
- provider capability negotiation.

Runtime composition must not register the removed pool seam.
