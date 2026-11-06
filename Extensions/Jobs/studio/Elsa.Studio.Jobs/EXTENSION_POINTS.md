# Jobs Studio contributions

The [Jobs API catalog](../Api/EXTENSION_POINTS.md) owns the HTTP boundary. This package contributes
presentation only and uses the [shared module controls](../../../../Studio/docs/module-ui.md).

`JobsStudioFeature : IShellFeature` is the optional `JobsStudio` shell feature, annotated as Studio
runtime and Jobs/Studio category. Its `StudioModule("jobs", ...)` declaration selects the client entry;
it adds no service authority. The public client `register(api)` contributes one feature area owning
`/jobs` and eleven capability-gated routes, using the host's navigation, workspace tabs, dialogs and
notifications. No route or control grants a backend permission.

The private client transport resolves `elsa.api.jobs/1` links for jobs, queues, schedules, holds,
capacity and access through the injected backend session. Job/schedule resource paths append encoded
identities. Current actor and permission facts come from the server's access relation; caller requests
contain no tenant, partition, actor or model association. The ordinary submission/schedule editor
constructs the explicit invocation fields rather than accepting arbitrary command JSON.

Module-owned request sequences discard stale completions. Unconfirmed commands retain exact replay
content in memory, guarded by the existing SDK navigation lifecycle. The backend owns idempotency,
current resource decisions, retained results and every durable transition. These UI controls add no
scheduler, handler registry, workflow continuation resolver, identity provider or secret abstraction.

The implementation and fixture source are not product acceptance. The
[acceptance record](SOURCE-ACCEPTANCE.md) names the required actual composed and independent UX proof.
