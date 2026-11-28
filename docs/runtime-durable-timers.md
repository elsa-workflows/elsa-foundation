# Durable timers and the `Delay` activity

On the built-in Actor Foundation, a Native `Delay` commits its waiting state
and a message to the same run with a due time. An owner can deliver that
message when it is due. The waiting state and delivery survive a restart on
SQLite and an owner change on PostgreSQL. The delay can complete after the due
time when the host is stopped or busy.

A recurring schedule starts a new run for each admitted occurrence. Its
schedule occurrence is separate from a `Delay` that resumes an existing run.
See [how a workflow executes](how-a-workflow-executes.md) and the
[current deployment guide](../docker/profiles/essentials-curated/README.md#workflow-execution).

The [classic timer guide](https://github.com/elsa-workflows/elsa-foundation/blob/18a16dee878106ac525aa9a861d851a200e43c55/docs/runtime-durable-timers.md) documents the
matching Full compatibility profile's scheduler and checkpoint model.
