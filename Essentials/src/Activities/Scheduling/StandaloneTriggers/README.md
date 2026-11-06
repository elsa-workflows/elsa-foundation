# Standalone Timer and Cron integration

`StandaloneScheduleTriggerAuthoringFeature` supplies metadata and pure validation without installing a scheduler or pump. Cron parsing uses the repository's pinned Cronos dependency; the runtime calculator owns actual due-occurrence and timezone behavior.

The [operator guide](../../../../../docs/standalone-triggers.md) owns resource/API examples, mapping, grants and missed-firing instructions. These source paths are an implementation candidate; actual due firing, restart, multi-worker, timezone/DST and Studio qualification remains open.

## Composition and occurrence handling

Select `StandaloneScheduleTriggerAuthoring` for Timer/Cron metadata and validation. Select `StandaloneScheduleTriggers` for `ScheduleResourcePlanner`, producer-bound Timer/Cron intake and `ScheduleResourceSource`; it depends on `WorkflowsRuntimeRecurringTriggers`. The latter supplies the existing recurring pump and depends on Runtime triggers and Tasks. Keep the ordinary scheduling, selected protected Runtime storage and recovery composition. `ActivitiesScheduling` supplies embedded activity schedules and does not install this standalone source feature.

Timer configuration is `{ "interval": "PT5M" }` (a positive ISO-8601 duration or invariant `TimeSpan`). Cron configuration is `{ "expression": "0 17 * * 1-5", "timezone": "Europe/Amsterdam" }`; five fields omit seconds, six include seconds first. Use timezone IDs installed consistently on every participating host. No `http` options apply to either family.

One resource owns one schedule, independent of subscriber count. The physical management provider applies `IStandaloneSchedulePlanner`'s pure plan with the resource change. The existing pump passes its actual occurrence claim to `IStandaloneScheduleSource`; it does not synthesize publisher workflows. The typed signal carries `payload.dueAt`, `payload.generation`, `metadata.dueAt`, `metadata.timezone` and `metadata.generation`. The due instant is captured as UTC; Timer uses timezone `UTC`.

Admission with no accepted subscriber retains the same overdue occurrence for retry. After a nonempty cohort commits, the pump advances to the next future instant from its current clock, providing one catch-up rather than a missed-interval backlog. Intentional pause deactivates the schedule; re-enable calculates the next future occurrence. Recurrence/timezone changes replace the schedule generation. A subscription-only edit preserves an active schedule, and already accepted cohorts retain their old capture.

The management projection reports the actual cursor/generation and, when supported, failure count, retry time and occurrence-in-flight state. Missing recovery observation capability is an explicit finding, not a zero-failure assertion. Source admission and cursor settlement use the selected physical provider; metadata validation and calculator tests alone do not prove a firing or restart guarantee.

## Cross-domain contributions

`ScheduleTriggerTypeSource` implements Runtime Core's `IStandaloneTriggerTypeSource`, supplying Timer and Cron metadata to the single `CollectTriggerTypes` aggregator and `StandaloneTriggerTypesInitializing`. `TimerTriggerConfigurationValidator` and `CronTriggerConfigurationValidator` implement scoped `ITriggerConfigurationValidator`; the single `ValidateTriggerConfiguration` aggregator invokes the matching owner through `StandaloneTriggerConfigurationValidating`.

`ScheduleResourcePlanner` implements Core's `IStandaloneSchedulePlanner`; `ScheduleResourceSource` implements `IStandaloneScheduleSource`. The producer contexts implement `IStandaloneTriggerSourceContext<TSource>` and bind only the actual pump claim. The owning SPI and provider obligations are in the [Runtime extension catalog](../../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#standalone-trigger-authoring-and-source-boundaries).

See [spec 1217](../../../../../specs/1217-standalone-triggers/contracts/transports.md) for source ownership, retained overdue occurrences, admission and qualification.
