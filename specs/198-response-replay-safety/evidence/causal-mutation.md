# Causal profile mutation and restored verification

Root accepted this bounded SQLite causal proof on 8 October 2026. The complete classification decision remains conditional: independent review identified the separate FR-004 external-reference restart proof as still missing.

The candidate-before and mutation stages use the same test fixtures, input, provider, Coalesced/50 settings, publication path and count windows. Only the production profile annotation changes from ReplaySafe to explicit External. Measurement-only expected-profile selection is explicit (`ELSA_RESPONSE_REPLAY_T012_EXPECT_CANDIDATE_PROFILE=External`) and rejects other set values. Ordinary publication and crash/recovery tests keep their fixed ReplaySafe expectations. The mutation runner restores the original production bytes in `finally`.

| Stage | Tests | Candidate profile | External / candidate EF commands | External / candidate durable commits |
|---|---:|---|---:|---:|
| Candidate before mutation | 1/1 passed | ReplaySafe | 131 / 101 | 3 / 2 |
| One-line mutation | 1/1 passed | External | 131 / 131 | 3 / 3 |
| Restored complete HTTP integration project | 39/39 passed | ReplaySafe | 131 / 101 | 3 / 2 |

Every stage exited zero with no failed or skipped tests. Logical checkpoints remain 22 and activity claims 6. Durable claim commits change 2→1 under ReplaySafe and return 2 under External; segment flushes change 3→2 and return 3. All mutation candidate-minus-External counters were zero. The restored full suite includes normal publication, hard-process-loss recovery, Immediate/Coalesced equivalence, unchanged default and matched-count tests.

Root checked the exact one-line source difference, byte restoration, actual TRXs, raw report hashes and 56 retained raw-file hashes for the first two stages. Root separately opened all four measured databases read-only: correct Completed executions, original correlated requests, artifact pins, Alice Smith outputs, committed HTTP200/text/plain instructions and zero target incident/queue/outbox rows. Root verified the full restored 39/39 TRX, unchanged 13 pinned source/config inputs and 42 retained-file hashes. Its child DLL hash matches the candidate-before build exactly.

| Artifact | SHA-256 |
|---|---|
| Candidate and restored production source | `7a55faafe0260ce2871a43c74779d830fcb2c61355d6e629c49d14de0db9858c` |
| Temporary External source | `789ad6e7791c5a3e0a47484deb88080bc7681c281b86388737faf2aebb98a455` |
| Single-line mutation patch | `acdcf81a2eb30fa19416e031a6cfb30e82ef28518fe51e232b998c236da8eadd` |
| Candidate-before TRX | `28a40b33b3b57edfb30310f00cf3324cf78ca0c399e645f998e507c9d66f26c4` |
| Mutation TRX | `13286f8f7b21794fef375d5fba5efec82ac3dea0d44fed7354cbfca163ac31f0` |
| Restored full integration TRX | `fbc340c51ac0da816f7508d2b28cc21a971ec6860f67d11ff5f33982916a18d1` |
| Candidate/restored child DLL | `3554d1260eba22c29af812436aaa0ac48ca880616deec5053ed3b2357a917c60` |
| Mutation child DLL | `3cd0f83593b90e419f5030de5abce47ac979fceeb0c462edaf22c7308cc469d2` |
| Candidate-before raw report | `3e9a760b86f26873318536dacc8a86eda80d43d3a1f41c28e4e0c0a1313216d2` |
| Mutation raw report | `6d582c8a322aaef1e2e19113b3baa6a2768ead7e790f6ce828b362989b1ef7c9` |
| Restored raw report | `bc08f5febd9dcc833f269318cab4217bc411d07641506d03c0eb38ec85d67381` |

The private journal retains `2400-t012/` with stage directories, complete source/command/binary receipts, databases, IPC and both root acceptance receipts. Exact raw reports are committed beside this file as `causal-candidate-before.json`, `causal-external-mutation.json` and `causal-restored.json`.

This establishes the classification's causal effect on the observed SQLite checkpoint and command counts. It does not establish SQL statement counts, network round trips, PostgreSQL savings or latency. [The matched-accounting report](matched-counts.md) defines those counting and window limits. The [local gates](affected-project-verification.md) now include Architecture and Maps. FR-004, its affected revalidation and final review/delivery gates remain required before accepting the classification.
