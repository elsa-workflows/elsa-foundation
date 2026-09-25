# Validation guide: File-deployed activation v1

This guide separates checks runnable on the current tree from the fixtures required before a future activation implementation can claim success. The [contract](contracts/activation-v1.md) defines outcomes; this is a validation path, not a live deployment instruction.

## Current baseline

From the repository root, run the delivered file bridge and lifecycle tests:

```bash
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj --filter 'FullyQualifiedName~CompositionBridgeSourceTests'
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --filter 'FullyQualifiedName~CompositionFileSourceTests|FullyQualifiedName~CompositionGenerateCliTests'
dotnet test tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj --filter 'FullyQualifiedName~FeatureManagementServiceTests|FullyQualifiedName~ServerReadinessTests'
```

The first two commands prove supported local source shapes, all-file change refusal and fresh candidate publication. The third proves the legacy editor's post-save failure windows and that the real registry keeps the previous ready generation after an initializer failure. They do **not** prove a deployer switched an artifact or that the host loaded a candidate's exact bytes.

Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` before relying on the committed maps. The architecture guard requires the repository's Release/Debug restore preparation: `bash tools/architecture/restore-ci-project-graph.sh -p:WarningsNotAsErrors=NU1603`, then `dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603`.

## Disposable handoff fixture for the first implementation story

Use the two-shell base/Production overlay fixture from [file bridge v1](../176-composition-file-bridge/contracts/file-bridge-v1.md). Keep its connection and unknown-setting canaries. Generate a fresh reviewed candidate as that contract describes. The new handoff test must:

1. Record the configured default shell and selected environment, accepted catalog/feature pin, every copied file's safe role, unresolved package/connection checks, and one opaque candidate ID.
2. Change each selected and unselected copied file in turn between review and handoff. Each change must refuse; neither a stale handoff nor a raw source value may be published.
3. Regenerate the same source independently. The new candidate may receive a different opaque ID; the test must not assume content-addressed public IDs.
4. Scan standard output, standard error, logs, and shareable handoff for both canaries, physical paths, raw file bytes, and private digests. Each must occur zero times.
5. Verify no package, host, database, migration, or legacy management-save path is invoked.

The external deployer must provide an artifact-integrity receipt in a later fixture. A bare handoff ID never authorizes a complete-bundle switch.

## Host-attestation gate before verified activation

Use a disposable rebuilt Workbench with one configured **default** shell and a prior ready generation. The gate is not passed by the existing tests alone. It needs a host-produced observation tied to the generation being promoted:

1. Deploy a reviewed complete bundle through a test deployment owner that records previous/current artifact identities and can restore the prior complete bundle. Observe a successful root reload, active generation, and default-shell readiness. Separately prove the host's generation-bound candidate marker equals the reviewed artifact; otherwise report match `unverified`.
2. Alter one included file, then change a process-level override or loaded package generation. Show which source identity and effective-host facts the marker actually covers. Any uncovered fact stays unresolved; a mismatch cannot be called verified.
3. Make the candidate initializer fail after deployment. Assert per-shell reload failure, previous generation still ready, deployed artifact changed, and candidate match not verified. Repair or restore the prior artifact, reload explicitly, and verify the resulting generation.
4. Drop or time out the reload response. Read back deployed artifact identity and active generation before retry; do not send a blind second apply. If state remains ambiguous, keep an `uncertain` result.
5. Include a non-default shell as a refusal case for v1 verified activation, and scan all outward results for canary values, blueprint configuration, management key and raw exception text.

Do not publish a `candidate active` result or cut a corresponding implementation story until this gate passes with a secret-safe host observation. No test here treats shell readiness as evidence of database migration, connection affinity or data rollback; those remain separate program dependencies.
