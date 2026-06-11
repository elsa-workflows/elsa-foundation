# Requirements Checklist: Runtime Volatile Wait Contract

- [X] Volatile wait is distinct from durable suspension/bookmark resume.
- [X] Volatile waits are activity execution and branch scoped.
- [X] Volatile wait completion is represented as deterministic scheduler work.
- [X] Scheduler continuation work is separate from scheduled activity work.
- [X] Host shutdown, cancellation, duration, and fallback policy inputs are represented.
- [X] Full scheduler execution behavior remains out of scope.
