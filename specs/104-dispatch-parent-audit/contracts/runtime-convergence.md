# Contract: Dispatch Runtime Convergence

- Original child-start delivery and terminal consequence finalization are separate replay phases.
- A finalization replay never invokes the original child-start handler.
- Failure projection writes are deterministic and independently idempotent.
- Redrive is accepted only from `DispatchFailed`; an active dispatch is duplicate only when its deterministic redrive evidence exists.
- Admission racing with cancellation must either prevent materialization or issue deterministic cancellation for the admitted child.
- Durable distributed forwarding exposes an admitted lifecycle state before terminal projection.
- Resume and cancellation work retry with bounded backoff until delivered, converged, or safely terminal.
