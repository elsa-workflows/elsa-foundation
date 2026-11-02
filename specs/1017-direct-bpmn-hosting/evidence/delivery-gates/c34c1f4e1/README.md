# Expanded hosting regression refusal atc34c1f4e1

The whole32-case hosting suite compiled at **c34c1f4e1f279c66598a5ab83a8c2cc05e9b4a72** finishes with **31 passed, one failed, zero skipped**. The failure is the actual legacy called-result acknowledgement test at its final checked snapshot: the provider reports `ObservationUnavailable` / `InvalidDataException`. The raw command, output and losslessly compressed TRX are retained with original counters/hash in [summary](summary.json).

This is an unresolved regression gate, not a clean run. No failed closed database was retained at this source. Commit02a19e865 adds refusal-time closed-snapshot/input/reader retention to the test observer without changing runtime semantics; a focused reproduction follows. The earlier28-case green suite and four new positive partial-fork captures retain their own scopes. Machine load was4.13 on15 logical CPUs when the failure was investigated; no machine-load explanation is assumed.
