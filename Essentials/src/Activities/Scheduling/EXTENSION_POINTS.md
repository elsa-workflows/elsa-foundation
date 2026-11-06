# Scheduling release inspection

`ActivitiesSchedulingReleaseVerificationFeature` is an optional, inheritable feature with virtual service configuration.
It contributes one scoped internal `IReleaseOccurrenceInspector` for the exact `Delay` CLR site. It depends on the
Runtime release bridge; it does not compose timer stores, schedulers, pumps or recurring-trigger features.

The supported profile checks `Duration`'s TimeSpan contract/literal, Unit result, direct secret refusal, actual external
side-effect classification, empty structural/output surfaces and the exact durable-timer resume handler/local identity.
The resulting timer-provider prerequisite is an admission requirement, not proof that a target scheduler is installed.
Unknown resume metadata and conversion operations remain unsupported. Timer/Cron are separate owner surfaces.

The verifier reads captured bodies only and never schedules or resumes a timer. Existing runs and continuation state
are not copied by release transport. Successful inspection grants no import, readiness or serving authority.
