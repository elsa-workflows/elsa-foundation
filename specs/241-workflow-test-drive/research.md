# Research decisions

- Existing Test Run compiles to the shared content-addressed store and appends an expiring reference. Keep it; obsolete spec076's transient artifact store is not revived (ADR0040).
- Preflight Evaluate derives exact valid bindings without index writes. Recompute on start and hash full entry identity for stale selection rejection. Preserve descriptor collision refusals.
- HTTP production middleware uses shared routes/global routing, so it cannot be mounted unchanged for a test. Isolated target resolution shares route matching, bounded body projection, authored auth and committed response delivery. Actual authenticated client transport sends the request; a model-only injection is rejected.
- Runtime-only HTTP cannot reference Publishing.Core, which depends on Design. Public test control shapes and separate runtime target dispatch therefore live in Runtime.Core.
- Timer/Cron FireNow uses one scoped dispatch; preview calls production IRecurringScheduleCalculator, currently UTC. No production schedule pump registration.
- Third-party extensibility is explicitly required by Sipke. Driver and Studio action/editor registries have no built-in activity switch in shared orchestration.
- Existing exact bookmark lookup and durable delivery receipts need explicit work before claiming continuation semantics. Global ingress must exclude test waits even if continuation is not exposed yet.
