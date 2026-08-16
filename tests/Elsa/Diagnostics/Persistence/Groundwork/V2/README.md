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
payload round-trip, and a typed query. The boundary proofs intentionally record current public
contract limits:

- `ProviderSequence` must be the sole key and `Append` currently returns no generated cursor values,
  even though the row is persisted with an authoritative sequence.
- `KeepNewest(0)` is refused by portability validation, so an Elsa `TrimAsync(0)` equivalent is not
  expressible through the current declarative retention API.
- Scoped access and per-scope idempotency are currently isolated correctly.
