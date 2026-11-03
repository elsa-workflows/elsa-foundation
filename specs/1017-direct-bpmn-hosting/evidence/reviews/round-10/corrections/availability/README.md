# Round 10 S1: prior-source read availability

The existing SQLite EF test harness injects I/O failure, unrequested provider cancellation and a database provider failure into the physical prior-source read. Before the production correction: 1 passed, 2 failed because raw IOException/OperationCanceledException escaped. After the one-filter correction: 3 passed, zero skipped. Every control preserves the original cause, names the actual scope and execution, preserves requested cancellation, and can still read the retained owed source after the injection is disabled. The read never fabricates absence.

This is a provider-boundary regression, not an executed dropped-input or host-recovery scenario. Compressed full logs and TRX are hash-indexed in manifest.json.
