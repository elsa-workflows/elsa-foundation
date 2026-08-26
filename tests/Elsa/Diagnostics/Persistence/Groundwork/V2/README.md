# E2 v2 diagnostics package-only proof

This consumer is deliberately isolated from Elsa's v1 Groundwork graph. It restores only packed
Groundwork v2 production packages, with no source project references, `Groundwork.Testing`, internal
access, reflection, or adapter dependencies.

From a checked-out Groundwork v2 repository, pack the target and run the proof from this
repository with:

```sh
dotnet pack Groundwork.slnx \
  --configuration Release --output /tmp/groundwork-v2-feed-e2
GROUNDWORK_E2_V2_PACKAGES=/tmp/groundwork-v2-feed-e2 \
  GROUNDWORK_E2_V2_VERSION=0.2.0-preview.2 \
  /path/to/elsa-foundation/tests/Elsa/Diagnostics/Persistence/Groundwork/V2/verify-e2-v2.sh
```

`GroundworkVersion` defaults to `0.2.0-preview.2`; set `GROUNDWORK_E2_V2_VERSION` when
verifying another explicitly packed version.

The green journey proves an ordinary SQLite manifest, schema application, idempotent append/replay,
payload round-trip, a typed query, and an exact provider-sequence append/replay whose generated
cursor is returned through the public `AppendWithOutcomes` API. The boundary proofs intentionally
record current public contract limits:

- `ProviderSequence` must be the sole non-nullable `Int64` key; the exact append path returns the
  authoritative generated cursor and replays it without allocating a second sequence.
- `KeepNewestOverride=0` is supported by exact retention execution: it removes all retained rows while
  preserving the lifetime ProviderSequence high-water, which is the public equivalent of Elsa
  `TrimAsync(0)`.
- Scoped access and per-scope idempotency are currently isolated correctly.
