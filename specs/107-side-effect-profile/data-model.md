# Data Model

## SideEffectProfile (new enum)

`Elsa.Activities.Runtime.Core.Models.SideEffectProfile`

| Value | Meaning |
|---|---|
| `External` | Fail-safe default. The activity may perform an externally observable effect, so its logical-invocation identity + input snapshot MUST be durably flushed before its body runs (mandatory immediate claim boundary). Unmarked ⇒ External. |
| `ReplaySafe` | Pure/deterministic between checkpoint boundaries. Its claim checkpoint may be deferred and folded forward into the next flushed commit under a coalescing cadence. |

## ActivityContract (extended)

`Elsa.Activities.Runtime.Core.Models.ActivityContract` gains `SideEffectProfile SideEffectProfile { get; }`, defaulting to `External` on both constructors. It participates in `SchemaFingerprint` only when non-default (canonical key `sideEffectProfile` = the enum name). JSON round-trips via the optional `sideEffectProfile` constructor param; older pinned JSON without it deserializes to `External` and validates to the same fingerprint.

## ActivitySideEffectProfileAttribute (new)

`Elsa.Activities.Runtime.Core.Attributes.ActivitySideEffectProfileAttribute` — `[AttributeUsage(Class, Inherited = true)]`, carries `SideEffectProfile Profile`. Read by `ExecutableNodeCompiler.BuildActivityContract` via reflection (`GetCustomAttribute<…>(inherit: true)`); absence ⇒ `External`.

## Checkpoint metadata transport

`RuntimeMetadataKeys.CheckpointSideEffectProfile` (`"runtime.checkpointSideEffectProfile"`) stamped by the claimer onto the `ActivityAttemptClaimed` checkpoint. Values: `CheckpointSideEffectProfileExternal` (`"External"`), `CheckpointSideEffectProfileReplaySafe` (`"ReplaySafe"`). Contract is the source of truth; metadata is transport for the policy decision. Absent ⇒ treated as `External` (fail-safe).

## Persistence decision (behavioral)

`CoalescingRuntimeCheckpointPersistencePolicy.DecideAsync(ActivityAttemptClaimed)`:
- coalesced-flush marker present ⇒ `Immediate`
- profile metadata `ReplaySafe` ⇒ `Deferred`
- profile metadata `External` or absent ⇒ `Immediate`

The checkpoint remains `CheckpointRequirement=Mandatory`; `Deferred` is permitted because `IsMandatoryCheckpoint` forbids only `Skip`.
