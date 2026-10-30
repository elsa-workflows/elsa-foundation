# Research

- Delay calculates `now + Duration` correctly; its fixed recovery schedule adds latency.
- Startup must sweep immediately to discover future persisted timers.
- Effective eligibility is `max(DueTime, VisibleAfter)` for claimed/released timers.
- Retain a generation during store reads so a notification racing the query cannot be overwritten. The hint holds one instant, with no unbounded timer map.
- Use TimeProvider timers and one running sweep with rearm after completion. Retain whole-sweep failure backoff independently of healthy deadlines.
- Seek through the existing scope/claim-order index rather than sorting future timers. Its visibility/due prefix is a lower bound on effective eligibility: one candidate normally proves the minimum. At most 16 one-row index seeks handle legacy/custom releases whose visibility precedes due time; if the minimum remains unproved, return unknown and retain periodic recovery. No schema change. High-frequency polling retains jitter and adds idle queries. Distributed notification transport is outside this fix.
- An independent planning research agent inspected visibility, scope lifetimes, startup and query races; its concrete findings agree with these constraints.
