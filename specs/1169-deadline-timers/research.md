# Research

- Delay calculates `now + Duration` correctly; its fixed recovery schedule adds latency.
- Startup must sweep immediately to discover future persisted timers.
- Effective eligibility is `max(DueTime, VisibleAfter)` for claimed/released timers.
- Retain a generation during store reads so a notification racing the query cannot be overwritten. The hint holds one instant, with no unbounded timer map.
- Use TimeProvider timers and one running sweep with rearm after completion. Retain whole-sweep failure backoff independently of healthy deadlines.
- Prefer a single scoped EF result over listing every future timer. Effective-eligibility ordering includes claim visibility and may require a database-side sort within the scope; it materializes at most one row and introduces no schema change. High-frequency polling retains jitter and adds idle queries. Distributed notification transport is outside this fix.
- An independent planning research agent inspected visibility, scope lifetimes, startup and query races; its concrete findings agree with these constraints.
