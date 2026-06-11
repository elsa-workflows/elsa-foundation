# Contract: Runtime Recovery Scanner

`IRuntimeRecoveryScanner` performs one bounded scan over runtime operational state and returns recovery candidates.

## Scan Rule

1. Read operational state through `IOperationalStateStore`.
2. If an interrupted execution is already `Detected`, return it as a candidate.
3. Else, if the execution lease is expired at `RuntimeRecoveryScanRequest.Now` or exceeds the request lease timeout, return a `LeaseLost` candidate.
4. Else, if the heartbeat is older than `HeartbeatTimeout`, return a `HeartbeatExpired` candidate.
5. Apply the optional owner filter and requested limit.
6. Results are candidates only; requeue, ownership claiming, actor placement, and domain retry decisions remain separate.
