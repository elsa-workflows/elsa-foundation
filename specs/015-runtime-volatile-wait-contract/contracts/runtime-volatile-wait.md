# Contract: Runtime Volatile Wait

Rules:

- Volatile wait is not durable suspension.
- Volatile wait state belongs to scheduler continuation state and is scoped to `ActivityExecutionId` plus branch.
- Completion of a volatile wait enqueues scheduler continuation work.
- Scheduler continuation work is separate from scheduled activity work.
- Runtime state mutation remains single-writer through scheduler-owned continuation work.
- Durable resume remains bookmark/resume-target based and is not modeled by volatile wait registrations.
