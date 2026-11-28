# Bookmark expiration

The classic `ExpiresAt` bookmark field limits when a matching delivery can
resume that bookmark. Scheduling work at the deadline requires its own timer.
Use the [classic bookmark guide](https://github.com/elsa-workflows/elsa-foundation/blob/18a16dee878106ac525aa9a861d851a200e43c55/docs/bookmark-expiration.md) with the
[pinned Full compatibility profile](../docker/profiles/full-compatibility.md).

In the Actor engine, activities wait for registered messages. Use a durable
[Delay](runtime-durable-timers.md) when a workflow must do something after a
deadline. See [how a workflow executes](how-a-workflow-executes.md) for current
Native and Flowchart execution.
