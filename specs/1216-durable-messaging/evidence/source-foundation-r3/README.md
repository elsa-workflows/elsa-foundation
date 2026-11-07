# Message foundation — actual EF fixture failures retained

**FAILED / INCOMPLETE.** The unchanged source was `fb82b1969612cc8a151f1fab57f582742dd3b94e`, tree `fb0009928afbde36baa1c58d59c074e064ee0e4c`.

The dependency compilation completed and all 37 focused EF tests actually executed: 35 passed, two failed, zero skipped and all other result counters zero. Both original stacks show `DbUpdateConcurrencyException` during test arrangement, before either behavior call: the terminal-source case at test line 165 and the revision-overflow case through `Fixture.ReplaceAsync` line 369 / test line 242. Each fixture loaded a detached row, changed its concurrency-token revision, then attached it using Update, so the original revision in the SQL predicate was already the replacement value.

The subsequent two-line test-only correction attaches each row before changing its revision. It preserves production checks, test assertions and the intended actual database state. A later successful run, if obtained, is separate evidence; these 35/2 results remain failed. Mutation/restoration and whole Runtime/EF projects did not run in this attempt.

All 80 original archive members and 79 manifest entries were acquired and checked. Source and own-unit/cgroup/process cleanup are separately VERIFIED, without deleting source/caches/stores or stopping another task. The original 69.438816-second attempt brings the conservative cumulative debit to 625.438816 seconds, rounded to 626/1800 for continuation. No Message producer, registration, successful physical capability or feature completion is established here.
