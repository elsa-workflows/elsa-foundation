# Jobs Workflows (Design)

`JobsWorkflowsDesignFeature` contributes `IExecutableCompilationSource` to the normal Publishing fan-in. For each `RunJob` or `SubmitJob` node, it requires literal `Handler`, `InputSchema`, `ResultSchema`, `Queue`, `Policy` and `AssociationMode` selections and writes the bounded `JobWorkflowHandlerPin` descriptor to `jobs.handler.v1` before executable hashing. It uses the [Runtime pin contract](../Runtime/Models/JobWorkflowHandlerPin.cs), including its supported association modes and validation bounds. `RunJob` accepts only `Awaited`; `SubmitJob` accepts only explicit `Independent` or `Detached`.

Publication does not resolve a handler implementation, consult a Jobs registry or start a Jobs worker. A coordinator can publish these logical contracts without installing the worker's CLR handler. The selected worker independently verifies handler and schema compatibility when it receives accepted work. The shell feature depends on `WorkflowsPublishing`; the host separately selects any runtime and provider features it needs.

`Input` and `NotBefore` remain ordinary runtime materialized bindings. This contributor neither evaluates them nor adds their values to the handler pin. The runtime adapter captures their materialized values in the saved invocation and verifies the frozen selections before admission. Secret references and withheld literals cannot be read as fixed selections; the activity's own secret-binding declarations also protect values it persists.

An existing unequal metadata claim is refused by Publishing's ownership validation. Publication never silently replaces an accepted pin. A runtime coordinator consumes the compiled descriptor and saved invocation without this Design package or mutable Design storage. See the [extension catalog](EXTENSION_POINTS.md) for registration and the [Jobs lifecycle contract](../../../../../specs/1218-standalone-jobs/contracts/job-lifecycle.md) for ownership boundaries.

This package is an optional adapter. Standalone Jobs Core, runtime and provider projects do not reference it. Source publication tests do not establish packaged, native execution, BPMN, API or Studio acceptance.
