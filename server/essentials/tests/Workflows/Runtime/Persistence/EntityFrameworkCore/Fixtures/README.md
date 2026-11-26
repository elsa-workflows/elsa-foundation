# Durable runtime history restart fixtures

`TimerHistoryRestartHost` publishes a real Delay and reopens an owned SQLite store across seed, fire and confirm processes. Durable, Balanced and Ephemeral each require separate process identities and identical binary/version receipts, original timer identity/time, the current eight migrations and operational format1.2, bookmark consumption, activity completion and duplicate-free repeat pumps. Logical time advances; processes and persisted storage are real.

`RuntimeHistoryRestartHost` publishes a real bookmark-wait workflow, resumes it after restart, then replays and purges its retained prefix and confirms the result in a fourth process. Durable and Balanced runs retain exact event/occurrence identities and invalidate old windows after retention.

These executable fixtures are built through the owning EF test project's non-reference project links. They accept bounded JSON on standard input and output bounded receipts. Tests create and remove their own nonce-marked temporary directories; no personal database, external listener or shared runtime is used. These controls do not constitute Studio visual acceptance or deployment qualification.
