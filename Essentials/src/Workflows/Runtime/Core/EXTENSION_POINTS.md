

### Optional timer history responsibility (#3849, S10l)

`IDurableTimerHistoryStore : IDurableTimerStore` is an optional provider port for retained timer-source responsibility; its capability defaults false and no implementation or registration is enabled by these contracts. `DurableTimer.HistoryResponsibility` is null-omitted on legacy documents. Its bounded typed input retains at most the schedule and one terminal observation, with one exact run/timer/activity/scope/deadline and a pinned policy. The common observation validator accepts only the closed timer vocabulary with deterministic owner identities and metadata. No timer payload or arbitrary metadata is copied.

Provider atomicity, first-winner replay, claim fencing, retirement/acknowledgement, pending pages, native liveness/deletion and Ephemeral no-record discharge must be supplied and qualified by the actual owner. `RuntimeHistoryNoRecordCompletion.Validate` checks only the input shape; it grants no completion or execution authority and proves no native absence. See the canonical [timer ownership model](../../../../../../specs/1211-run-history-timeline/data-model.md) and [atomic history contract](../../../../../../specs/1211-run-history-timeline/contracts/atomic-history.md). Production timer enrollment/pump/cleanup and both provider implementations remain successor work.
