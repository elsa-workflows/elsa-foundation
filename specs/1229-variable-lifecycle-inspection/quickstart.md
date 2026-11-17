# Quickstart: Variable lifecycle inspection

Validates the spec end to end on a Workbench or Foundation host built from the initiative branch.

1. Publish a workflow with a Fork of three branches. Two branches read `count` and capture `count + 1`; the third
   has a `Set` that sets `count` to 100 (a blind write; a script `setVariable` behaves the same once session writes
   exist).
2. Run it twice.
3. `GET runtime/workflows/instances/{id}`: `lostUpdateWarningCount` is 1.
4. `GET …/variable-writes?variableKey=count`: entries in commit order; the last applied one has `isCurrentValue: true`;
   the stale-base write is `lostUpdateWarning` with both writers; whichever of the `Set` write and the write it met came second is marked as a concurrent overwrite.
5. `GET …/variable-writes?overwrites=true`: only concurrent overwrites, with `concurrentOverwriteTotal` ≥ 1.
6. `GET …/journal`: commits in order with each branch's transitions and writes.
7. `GET …/compare/{otherId}`: if the branches committed in a different order, the first divergence and `count` are
   reported; values are not included.
8. Repeat 4 as a caller without the sensitive-value permission: values show `redacted`, metadata unchanged.
9. Make the branches sequential and run again: no lost updates, no concurrent overwrites.
