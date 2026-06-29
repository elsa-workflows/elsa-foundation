# Elsa.Activities.Parallel

Fork/join `Parallel` composite activity module. On execution it forks by scheduling every (non-empty)
branch at once, each through its own named child slot (`Parallel.Branch[{name}]`) and under a distinct
engine `BranchId`, the way the flowchart parallel fork/join policies use the engine branch scope. It then
joins: each branch completion is recorded and the composite completes with `Done` only once the join
condition is met — by default all branches must finish, or a configured subset/`Threshold` when fewer are
required.

True parallel OS threading is deferred (D11): the scheduler is single-threaded, so "concurrent" means all
branch executions are scheduled together at fork time and progress independently through the scheduler, not
that they run on separate threads. Each branch is forked under a distinct `BranchId` and is a distinct
executable node in its own slot, so branch outputs are recorded against distinct executions and never
overwrite each other.

The join is stateless: the child-completion callback recovers how many branches have finished by querying
the durable activity-execution store for this composite's completed branch children (the runtime persists
each completing child as `Completed` before enqueuing the parent-completion evaluation), rather than
carrying a mutable counter across the per-completion re-construction. The engine flips this composite to
`Completed` on the first satisfying completion and short-circuits later sibling evaluations, so the join
never double-completes. If a branch runs `Finish`, the engine ends the run terminally and cancels the
remaining queued sibling branches (#293).

The runtime activity class (`Activities/Parallel.cs`) references only the runtime contract surface. The
design-side `ParallelStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
