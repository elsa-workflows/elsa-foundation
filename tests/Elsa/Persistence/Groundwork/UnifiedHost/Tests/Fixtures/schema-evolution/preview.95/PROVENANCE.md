# Groundwork preview.95 schema-history fixtures

These gzip files are schema-model fixtures, not provider execution evidence. They preserve the
canonical `PhysicalSchemaAppliedStateSerializer` output for the complete
Identity + Diagnostics reference manifest that existed at:

- Elsa commit: `ca818b649d85c5167e2222c0ec534e215153d473`
- Elsa tree: `82433ec3170e88244f9d139b35c4b7c6e13225d5`
- Groundwork package family: `0.0.1-preview.95` (all seven runtime packages)
- Groundwork tool: `0.0.1-preview.95`
- Manifest type:
  `Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema`
- OpenIddict is not part of this composition; these fixtures make no OpenIddict schema-upgrade claim.
- Fixed plan/application timestamp: `2026-07-30T00:00:00Z`

Groundwork deliberately fingerprints the runtime-compatible Unicode ordinal-ignore-case table.
The physical schema is the same, but an applied state from a different Unicode table must fail
closed rather than silently reinterpret persisted identity keys. Two exact preview.95 fixture
families are therefore retained:

| Location | Runtime | Unicode identity algorithm |
|---|---|---|
| this directory | Darwin capture | `groundwork-unicode-ordinal-ignore-case-v1-3206f759667cb9cc764ec243dfb3d322a39970184efab619e80163c36d86818f` |
| `unicode-identity-124ca0d0d2b0/` | Ubuntu Noble / GitHub-hosted Linux | `groundwork-unicode-ordinal-ignore-case-v1-124ca0d0d2b045d7be0e6aea8f07f74fbc0428a13c53a47d8a7d41db71b5ec5f` |

`HistoricalSchemaUpgradeTests` selects only the family whose exact algorithm ID matches the current
runtime. An unknown runtime fingerprint fails explicitly; the test never rewrites or normalizes an
applied state's identity evidence.

Each source target was compiled in a clean detached worktree at the commit above with its
provider's exact physical-name normalizer. MongoDB used
`MongoDbPhysicalStorageModel.Compile` so provider-owned definitions are included. The initial
empty-history plan was completed with exact operation fingerprints, serialized canonically, and
gzip-compressed without modifying the canonical payload. `HistoricalSchemaUpgradeTests` pins the
compressed SHA-256 of every file before deserialization and exercises the preview.102 additive
diff against all four histories.

The source claim is independently replayable; it is not inferred from those fixture hashes.
Run `bash tools/evidence/replay-preview95-schema-fixtures.sh` for the host family or add
`--runtime linux-noble` for the pinned Ubuntu Noble container family. The replay:

1. creates a detached worktree at the exact Elsa commit and verifies its Git tree;
2. verifies all seven Groundwork package pins plus `Groundwork.Tool` are preview.95;
3. verifies the restored package nuspecs all bind to Groundwork commit
   `d297147e0cd6b018d70b1f7d61fef771e32b022f`;
4. injects only the committed reproduction harness into that detached source tree; and
5. regenerates the canonical applied-state JSON and compares its SHA-256 with the decompressed
   committed fixtures for every provider.

The gzip containers retain their original capture metadata, so the replay compares canonical
payload bytes rather than attempting to reproduce filename and timestamp fields in the gzip
headers.
