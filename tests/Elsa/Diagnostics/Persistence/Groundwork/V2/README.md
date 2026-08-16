# E2 v2 diagnostics package-only proof

This consumer is deliberately isolated from Elsa's v1 Groundwork graph. It restores only packed
Groundwork v2 production packages, with no source project references, `Groundwork.Testing`, internal
access, reflection, or adapter dependencies.

Pack the merged Groundwork target and run the proof with:

```sh
dotnet pack /Users/sipke/.codex/worktrees/groundwork-v2/Groundwork.slnx \
  --configuration Release --output /tmp/groundwork-v2-feed-e2
GROUNDWORK_E2_V2_PACKAGES=/tmp/groundwork-v2-feed-e2 \
  tests/Elsa/Diagnostics/Persistence/Groundwork/V2/verify-e2-v2.sh
```

The green journey proves an ordinary SQLite manifest, schema application, idempotent append/replay,
payload round-trip, a typed query, and an exact provider-sequence append/replay whose generated
cursor is returned through the public `AppendWithOutcomes` API. The boundary proofs intentionally
record current public contract limits:

- `ProviderSequence` must be the sole non-nullable `Int64` key; the exact append path returns the
  authoritative generated cursor and replays it without allocating a second sequence.
- `KeepNewest(0)` is refused by portability validation, so an Elsa `TrimAsync(0)` equivalent is not
  expressible through the current declarative retention API.
- Scoped access and per-scope idempotency are currently isolated correctly.
