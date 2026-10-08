# Integrated response replay verification

Source: `2a173d345467e7ec35d95633e858eb6c88c0c571`, incorporating accepted #2497 and #2515. These fresh results supersede the earlier candidate's counts for current-source acceptance; earlier failed checks and source-pinned evidence remain retained. The only production delta from accepted main `9c9f475929018d96a16fb3a24549a304a7612254` is the WriteHttpResponse ReplaySafe annotation.

## Publication and correctness

The full Publisher API suite passed 723/723, the HTTP activity suite 204/204, and the normal-published External/candidate HTTP pair with valid REST companion 1/1, all without skips. The current-format External closure is unchanged. A separate two-case hard-process-loss run passed 2/2; the restored full HTTP integration suite later passed 43/43. Source remained frozen during the gates.

Root independently inspected the retained SQLite databases and raw stage receipts. Both crash children exited 137, setup and recovery children exited zero, and recovery preserved each original execution/request/artifact and completed with HTTP 200, `Alice Smith`, `text/plain`. There were no target incidents or scheduler work remaining. T009 covers the inline fused buffered-completion window. T019 separately covers a stable external payload at cap 2 with fusion disabled, including a fresh process/provider reading the same payload after restart. T019's four final outbox entries are Delivered; T009 has none. Lease-expiry ordering is enforced by the executed harness assertions, not a separately timestamped IPC event. Neither proof promises revival of the interrupted socket or exactly-once transport.

## Same-build causal comparison

All stages used the same Git HEAD, frozen inputs, current-format baseline, SQLite provider, Coalesced/50 policy, fresh child processes and equivalent online-backup database copies. Source mutation changed only ReplaySafe to External; source was restored byte-for-byte. Across the three normally published candidate closures, activity type versions match exactly. After removing publication identity/time and the response profile/fingerprint, their complete artifact content matches. Candidate and restored hashes match; the External mutation has a different hash. All restored child-output DLL hashes match the before-mutation manifest, including the child hash recorded by the earlier recovery run.

| Stage | New candidate profile | Request commands, External / candidate | Background commands | Unattributed commands | All observed commands | Durable commits |
|---|---|---:|---:|---:|---:|---:|
| Candidate before | ReplaySafe | 133 / 102 | 0 / 0 | 3 / 0 | 136 / 102 | 3 / 2 |
| One-line mutation | External | 133 / 133 | 0 / 0 | 3 / 0 | 136 / 133 | 3 / 3 |
| Restored full suite | ReplaySafe | 133 / 102 | 1 / 0 | 199 / 3 | 333 / 105 | 3 / 2 |

Every numeric pair is External baseline / newly published candidate. The request-attributed contrast reverses with the single profile mutation: **31 fewer EF command attempts (133 to 102), one fewer durable commit/flush (3 to 2), and one fewer durable claim commit (2 to 1)** in this bounded workflow. Logical checkpoints remain 22 and logical activity claims 6; dispatches change 17 to 11 and revert 17 with External. Every captured command has a successful outcome after callback drain; there are zero in-flight or orphan outcomes at finalization. All 15 matched-count child processes exit zero. Root opened all six measured databases read-only and confirmed the intended artifact pin, same completed response and no target incident or scheduler work.

The full-suite total difference of 228 is **not** the classification saving. Background and unattributed work remain visible; their variation is not causally attributed. These are EF interceptor command attempts, not physical SQL statements, result-row counts or network round trips. The shared Mac supplied correctness and bounded counting evidence only. PostgreSQL and low-logging Azure timings remain #2413 work.

The older External baseline was captured under build-stamped versions `11c852…`; new candidates carry `2a173…`. Therefore baseline-versus-candidate artifact hash differences alone cannot establish profile causality. The same-version candidate/mutation/restored comparison above and the dedicated #2515 compiler regression supply that isolation.

## Pins

| Item | Identity / SHA-256 |
|---|---|
| Current-format External artifact | `artifact-48fcccd01c49`, `sha256:48fcccd01c4967d6ea576fdf6dc5a8d9383d6f6d858f9886c910cd3167ea5198` |
| ReplaySafe candidate and restored artifact | `artifact-f82def3aac4a`, `sha256:f82def3aac4ae66f86595629628725d60029201a09957a667830f6ee5aaa9526` |
| External mutation artifact | `artifact-c3bda14be89e`, `sha256:c3bda14be89ed38fabd9138f3d0b43bb9aacf309f44917caeced5d772d16b76f` |
| Candidate/restored source | `7a55faafe0260ce2871a43c74779d830fcb2c61355d6e629c49d14de0db9858c` |
| Mutated source | `789ad6e7791c5a3e0a47484deb88080bc7681c281b86388737faf2aebb98a455` |
| Candidate/restored child DLL | `91651a10d73474038cbb70384094a246c4352e392190e803fd10aa2bbd2e8860` |
| Mutated child DLL | `acccbc76f551b7532351dbb5bfd8884c2ccb76893c1482992f8501d93260b991` |
| Publisher TRX | `19521baffadcb7b2e6bfd8855da970aa170fd59f596813abda6070676e705dbd` |
| HTTP activity TRX | `07e2048aba61c56c04f142a58a7ec3c5d5f07ecb28c0f9b3887f25c60e56c78e` |
| Normal publication TRX | `05d9b5c839091f2d9af0ed93731021edf4e727f62e0be2491ff611f08847e0b9` |
| Focused recovery TRX | `25841d01dbc46a5d379469e3dec8f75ec23c9c2de28b0e298a80ecc0a1abafcb` |
| Candidate count TRX | `2be2367891799ecb172d23b7aae71553ffbc20f07eaa37de70c014357bac0239` |
| External mutation TRX | `3d2a8da5d218ff35907a73df9ad49666ec15c2ed010d4efa56097089ee73c2af` |
| Restored full HTTP TRX | `df11810192e249bb7669eff4d3e4087a95665ffc338766b0c3c5cc14450a44d7` |
| Candidate raw report | `41e90435d0af09419c2a01e32173c8335a04633dd0abf878569f2e5033925e7f` |
| Mutation raw report | `117f373107f12f1e36e93b427be20f4fc8b38b59eb1f9cafd58fe6c2bf462d39` |
| Restored raw report | `69ebdd0ad8d87b3cb20208b2fcec4882bfc87879d1bb1ce7296beda1b516f3ac` |

Private journal `2400-integrated-2a173d3/` retains all command receipts, actual TRXs, source/binary manifests, source/causal/recovery reviews, IPC/provider logs and read-only database audits. Successful retained test databases and the original exported fixtures are preserved. Historical binary hashes were recorded live; matching restored bytes were independently rehashed rather than claiming a separately archived original binary.

## Delivery gates

The rebuilt Workbench passed all four HTTP methods against a fresh owned SQLite content root. Source/configuration remained unchanged, all three configuration copies matched source, and the owned server stopped with no remaining listener. The Workbench DLL hashes to `f8fb0e91f5da00bfe4be0d1865fc0ef621b365b5301ee0c343d03ba1e011cd55`; the method log hashes to `eb04a702fe6e49cae4210f326e35df63baf848f7eda08e9633e01a6fc7cebe82`.

Root and independent review accepted the source, recovery and three-stage accounting packet at this pin. The [Maps check](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37829593310) passed on the same head; root read its actual successful checker log. Remaining affected Runtime, Resumption, Runtime EF and Architecture results will be adopted from the exact-head PR CI only after their actual results are verified. Earlier #2515 passes are not substituted for unavailable current checks. Final PR review and resulting-main verification remain required under [PR #2518](https://github.com/elsa-workflows/elsa-foundation/pull/2518). #2400 and the program remain incomplete.

Exact raw reports: [candidate](integrated-candidate.json), [External mutation](integrated-external-mutation.json), [restored](integrated-restored.json).
