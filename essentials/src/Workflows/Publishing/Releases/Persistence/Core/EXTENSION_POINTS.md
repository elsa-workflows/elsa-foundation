# Frozen archive replacement ports

- **Layer and contracts:** Persistence Core; `IReleaseArchiveReader`, `IReleaseArchiveWriter` and `IReleaseArchiveStoreCapabilities`.
- **Kind:** Scoped backend replacement ports. Select one coherent implementation for all three; they describe the same store and trusted scope.
- **Signatures:** `FindAsync(captureId, cancellationToken)` returns `ValueTask<IReleaseArchiveSnapshot?>`;
  `CreateAsync(captureId, zip, cancellationToken)` returns `ValueTask<ReleaseArchiveCreateReceipt>`;
  `GetCapabilitiesAsync(cancellationToken)` returns `ValueTask<ReleaseArchiveStoreCapabilities>`.
  Exact parameter types/defaults are in [the contracts](ReleaseArchiveContracts.cs).
- **Default:** No Core implementation. The opt-in SQLite facade is in [the EF package](../EntityFrameworkCore/README.md).
- **Override when:** Supplying another qualified durable archive backend that preserves exact bytes, coherent scoped reads/writes,
  equal-content retries, first representation and ambiguous commit recovery. No provider success is inferred from an unknown receipt.
- **Dependent callers:** Host capture/import/download consumers and archive tests. Source acquisition and release activation are separate capabilities.

Trusted scope is selected by server composition rather than bundle content. The store guarantees frozen transport retention only;
semantic verification, source pinning, origin and deployment authority remain separate gates. No named events or contribution loops.
