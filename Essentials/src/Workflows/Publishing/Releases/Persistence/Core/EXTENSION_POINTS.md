# Frozen archive replacement ports

`IReleaseArchiveReader`, `IReleaseArchiveWriter` and `IReleaseArchiveStoreCapabilities` are scoped backend replacement
ports. Select one coherent implementation for all three. Trusted scope is selected by server composition rather than
bundle content. The store guarantees frozen transport retention only; semantic verification, source pinning, origin and
deployment authority remain separate gates. A replacement must preserve equal-content retries and ambiguous commit recovery.
