# Research: A step writes its reusable body's own variables

## R-1 Where a session write into a body variable is refused today

`StepVariableSession.Stage` faults when the resolved frame is the closed-scope frame (`IsClosedScopeFrame`: the session
has a `ClosedScopeBoundary` and the frame is `Frames[0]`). The closed-scope frame is built by
`RuntimeClosedScope.CreateFrame` from the boundary's variable rows: kind `Root`, scope id = the boundary's executable
node id, activation id = the boundary's invocation id, versions read from the rows' metadata.

**Decision**: remove the refusal. The intent gets an explicit closed-scope target (the boundary's invocation id), because
an intent whose frame kind is `Root` is otherwise taken for the workflow's root frame by `StepVariableCommit`.

## R-2 How a body variable row is written today

Body variable rows are seeded when the body starts (`GraphActivityScope`, role `variable`, id
`ActivityExecutionScopeKeys.Variable(boundary, key)`, encoded by the row's storage driver). Only captures write them
afterwards: `RuntimeOutputCaptureProjector.ProjectGraphVariableAsync` builds the new row and `VersionRowAsync` judges it
with `VariableFrameState.Commit` against a read frame made of the stored row, then stamps version, digest and writer
on the row (`ClosedScopeVariableRows.WithVersion`) and returns a `VariableWriteRecord` whose commit order is the new
version.

**Decision**: extract the row judgement into one helper (`ClosedScopeRowCommit`) used by both captures and session
writes, so D-9 holds by construction. Session writes re-encode the value through the row's own storage driver and keep
the row's metadata (declared name, driver, protection minimum).

## R-3 Order of session writes and captures in one commit

The invoke and resume handlers project captures (including row captures) before they apply session writes
(`StepVariableCommit.TryApplyAsync`). Frame writes are ordered correctly because captures are applied on the workflow
state the session commit returns. Row captures instead read the stored row, so a session write and a capture into the
same row would both judge against the stored version, and two captures into one row already do (a listed limitation).

**Decision**: one row overlay per commit (durable value id → row as last written). Session row intents apply first into
the overlay, before the capture projection; captures then judge against the overlay. `StepVariableCommit` applies the
row intents itself on the suspension path, where there is no capture projection, and skips them on completion when they
were applied ahead of the projection. This fixes the two-captures limitation too (spec FR-007).

*Alternative rejected*: moving the whole session commit ahead of the capture projection. It would reorder frame writes
in two large handlers for no gain.

## R-4 Write history, protection, snapshots

Row records already carry source, writer, base/current/new version and digest; a refused (`Fault`) row write refuses
the completion through the handler's `rowLostUpdateFault` path and is not recorded for acceptance. Session row writes
use source `SessionChange` and the same path. As for row captures, no value snapshot is kept and the record takes the
written value's protection (combined with the stored row's), the documented row limitations.

## R-5 Isolation

The session's visible chain stops at the nearest boundary (`RuntimeContainerScopeService`, `IndexOfNearest`): the
caller's frames are not in it. Writes therefore cannot reach the caller. Tests pin this with a same-named caller
variable (spec SC-005).
