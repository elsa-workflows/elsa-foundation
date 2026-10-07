# Scope contract and mutation proof

Root retained the exact Release command/log/TRX for each run and reviewed outcomes; the tests were held unchanged throughout the default-registration mutation. The same real-EF composition test resolves the actual collaborators without database operations. Only the default factory registration changed in runtime source.

| Run | Registration | Passed | Failed | Skipped | Wrapper exit |
|---|---|---:|---:|---:|---:|
| before-fix | Singleton | 0 | 2 | 0 | 1 |
| custom-before-fix | Singleton | 2 | 0 | 0 | 0 |
| after-fix | Scoped | 2 | 0 | 0 | 0 |
| mutation | Singleton | 0 | 2 | 0 | 1 |
| restored | Scoped | 2 | 0 | 0 | 0 |
| runtime | Scoped | 2013 | 0 | 0 | 0 |

Both failing scope runs have the same intended oracles: validation enabled rejects scoped `RuntimeCheckpointCommitter` from singleton `IRuntimeCoalescingDrainScopeFactory`; validation disabled fails the distinct-factory identity assertion across independent scopes. The temporary mutation was restored before the confirmation run and the full runtime suite. Custom factory Singleton/Scoped lifetimes and repeated-registration byte stability pass before the default correction and in the corrected runtime suite. The Immediate default and existing checkpoint/replay/outbox assertions remain in that suite.

Root and independent Sol accepted the test-only diff and actual before-fix evidence before the runtime patch. Independent Sol 5.6 High also accepted the production source and actual mutation/restoration records. Broader provider integration, rebuilt HTTP/REST controls, architecture, Maps and exact-head review are pending; this is not completion evidence for those gates or attribution of historical C4 responses. Builds emitted existing warnings; zero-warning compilation is not claimed.

Retained evidence digests:

- before-fix: TRX `1708e4928f3798651014591e780ca7abadb7d3a495d919dd04ea98ede8fbcf8e`; log `e7bb419392246d140ab7bea8df1470d7af4bae5a877aa635c6a42522d17210a2`.
- custom-before-fix: TRX `ed5f74fec284859a9528bdb72906ab2519fc52e369aa88a43cd9824eda2cfa78`; log `0794de72cbb0de7e62a0be5deb14969447f4ff01c40fe1ab3e664242ab36783e`.
- after-fix: TRX `7a8407a4298ad1e6848801d37882f53841374846ffc9811c06aad4f3b20c904c`; log `5463627c16a6e44bc2f0c1f769d40f7c1468f1e9acac490665a32f669f5bcd0c`.
- mutation: TRX `d86edd1c467f1b941ec52e3f6ed0590cbe6b21a64dd528d45c6ab2da2f8bcd38`; log `66aab83dbadb61195f3770cbf6f173d14393197a7519a2f2d3db48cf67df6cb1`.
- restored: TRX `ce95fbc28e413627ce2f48bfa465baeed980e1c316ab448fc90641b93253295a`; log `82ba4679dc8f96ba3abd3112b58aff6bfab169ad06d662c2292e6b02e4f52fe2`.
- runtime: TRX `9b96602e4692a16ff6641b44e69597fba34532002eac30113b9691c698ad3e11`; log `2f9191f24d3f31580eb3f14acd16b59a9985c1d9752be47dfa5ac23ca1ab2e53`.

Restored runtime source SHA-256 `f60ebcb2c31590c160021c2042b1b877c140194061eb9983ca64a88edf8f6a15`.

Temporary singleton mutant source SHA-256 `9acbc901ccb472a890b48acb39d09745925c715a5a2bd67e312ddcaa4c273d99`, identical to the original before-fix runtime source. Root changed only the default registration between red and green builds and restored the scoped source before subsequent verification.
