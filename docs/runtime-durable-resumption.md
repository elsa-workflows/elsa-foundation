# Recovering a workflow after a crash

Native and Flowchart runs resume through the built-in Actor Foundation. The
owner reads a committed run and processes its pending messages. On PostgreSQL,
a successor can take the partition after the previous owner loses its lease.
Each commit checks the ownership fence, so the previous owner cannot publish a
delayed commit after the successor takes over. For SQLite, restart the single
owner against the same persistent database file.

A committed run keeps its selected workflow publication. Inspect its committed
activity records in History. See [how a workflow executes](how-a-workflow-executes.md)
for the run and History path and [ownership and fencing](architecture/ownership-and-fencing.md)
for the ownership protocol.

## Retiring the clustered runner

Drain classic executions with their matching deployment and store. The
[pinned Full compatibility profile](../docker/profiles/full-compatibility.md)
records the earlier host and its source. The [classic resumption guide](https://github.com/elsa-workflows/elsa-foundation/blob/18a16dee878106ac525aa9a861d851a200e43c55/docs/runtime-durable-resumption.md)
explains the earlier queue and checkpoint mechanics.
