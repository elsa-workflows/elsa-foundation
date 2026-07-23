# Elsa.Activities.Primitives

Primitive hand-written activities and the transient CLR activation implementation. This runtime feature
references no Design project.

`ActivitiesPrimitivesFeature` registers `ClrActivityActivator` as `IActivityActivator`. For each invocation
attempt the activator resolves the canonical type alias from `ClrActivityDescriptor`, creates an owned DI
scope, uses `ActivatorUtilities` so activity authors can use constructor injection, constructs a fresh CLR
instance, and hydrates its plain `[ActivityInput]` properties from the committed input snapshot. The
activation lease disposes both the activity and its scope.

`WriteLine` is the minimal shipped example: a plain annotated `string Text` property and one atomic
`ActivityUnit` result. It contains no argument wrapper or activity-owned value address.

## Named-event surface: `Event` and `PublishEvent`

`Event` (`Elsa.Event`) is the named-event **receive** side: as a start trigger it registers a durable
`EventStimulus` binding at publish time; scheduled mid-flow (`CanStartWorkflow = false`) it suspends on the
same stimulus identity and completes when the event is raised. `EventStimulus` maps an event name to the
opaque `(StimulusType = "Event", StimulusHash = SHA-256 of the name)` routing pair — deliberately
cross-workflow (the collaboration key).

`PublishEvent` (`Elsa.PublishEvent`, spec 135) is its **send** sibling. Its inputs are `EventName` (required),
`CorrelationId` (optional; threaded verbatim into the dispatch for issue #1001-readiness — broadcast until it
lands), and `Payload` (optional `JsonElement`). Execution is **durable-first**: it never calls the stimulus
router in-line. It validates the name and stages a typed `PublishStimulusRequest` on the activity's own commit
through `IPublishStimulusStager`, then completes `Done` immediately (fire-and-continue). The invocation-keyed
`PublishStimulusStagingBuffer` (registered as both the activity-facing stager and the engine-facing
`IPublishStimulusStagingAccessor` the invoke handler drains) builds a `PublishStimulusIntentKind` post-commit
intent carrying the shared `Event` stimulus identity. After the checkpoint commits, `PublishStimulusExecutor`
routes it via `IStimulusRouter.RouteAsync(StartAndResume)` — so one send both starts every published
message-start workflow listening on the name and resumes every parked same-name catch — with the outbox's
retry/poison discipline. The staging seam mirrors the DispatchWorkflow stager's layering exactly but is
publish-typed (it takes the send's facets, never a raw intent), so an activity cannot forge an arbitrary
post-commit intent kind through it.

Coverage lives in `tests/Elsa/Activities/Runtime/Tests`, especially the CLR activator, input hydrator,
pinned-input, and completion contract fixtures, plus `PublishEventTests` (durable-first staging, the buffer's
intent shape, and the `StartAndResume` handler pin).
