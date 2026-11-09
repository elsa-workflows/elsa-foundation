# Background execution contract

## Authoring and pinned artifact

- Default Inline placement preserves old JSON/hash behavior. Explicit Background is authored on ActivityNode and compiled onto ExecutableNode.
- A separate `[BackgroundExecutionSafe]` declaration pins `SupportsBackgroundExecution=true` into ActivityContract and its fingerprint. Default false preserves historical contract bytes/fingerprints.
- Publishing validates the capability, leaf shape and supported result/value policies. Runtime repeats validation from the pinned artifact and activated runtime type. An invalid configuration faults with a stable diagnostic; it never silently falls back to Inline.

## Durable store

`IBackgroundActivityWorkStore` is provider-neutral. Operations: find by scoped workflow/work identity; claim next eligible item with owner/time/lease; renew exact current unexpired claim; complete exact claim with terminal result; list bounded ResultReady candidates for repair delivery. Query methods expose immutable values. Worker methods never mutate native workflow/activity state. Exact naming may be refined with the implementation, but semantics are mandatory.

The checkpoint state set carries background admissions, acceptance and cancellation. Admission, acceptance and cancellation must be transaction participants, not calls to the worker store before/after the native checkpoint. First-party EF and in-memory test/transient providers share lifecycle validation; unsupported provider composition fails clearly before admission.

## Coordinator result transport

The durable result pointer names WorkId and native invocation, not arbitrary state to apply. Its stable coordinator work identity survives retries. Enqueue and actor wake are hints repaired by scanning ResultReady. The coordinator validates scope, artifact hash/version, activity contract fingerprint, logical attempt and current activity state; loads the accepted result; reconstructs TResult only from the well-known type registry; then uses the existing completion/fault path. Acceptance is tied to the same terminal checkpoint. The coordinator retains the OwnerId and epoch from explicit lease acquisition for admission, acceptance and cancellation; it never rereads and adopts a newer owner's fence.

## Worker host

Workers run as opt-in CShell task-pump features with bounded configured concurrency, lease duration, renewal cadence and poll interval. Each claimed body has its own service scope; renewal and cancellation observation use a separate scope from the running body. Cooperative cancellation is linked to shutdown/claim loss, but only an activity-returned Cancel is a business transition. No fire-and-forget untracked Task.Run work, shared DbContext across parallel bodies, or long-running outbox handler.

## Contract limits

Physical invocation can repeat after lease loss. `ActivityExecutionContext.InvocationId`/AttemptId are stable logical identity for idempotency. Effects outside Elsa's transaction need the activity's own idempotency support. Parallel worker execution does not grant permission to write shared workflow variables directly; returned outputs are applied serially by the coordinator.
