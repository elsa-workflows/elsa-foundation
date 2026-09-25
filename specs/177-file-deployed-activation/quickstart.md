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

To request the safe handoff from the CLI, add `--handoff-host workbench-a` to the existing `composition generate` command. The alias is explicit and contains no path. The command still requires typing `generate` after reviewing its diff. On success it prints one compact `handoff` JSON line with an opaque ID and role inventory; save that line alongside the externally managed artifact if needed. The line is not a deployment receipt or proof that Workbench loaded those files. Without `--handoff-host`, generation retains its prior file-only behavior.

1. Record the configured default shell and selected environment, accepted catalog/feature pin, every copied file's safe role, unresolved package/connection checks, and one opaque candidate ID.
2. Change each selected and unselected copied file in turn between review and handoff. Each change must refuse; neither a stale handoff nor a raw source value may be published.
3. Regenerate the same source independently. The new candidate may receive a different opaque ID; the test must not assume content-addressed public IDs.
4. Scan standard output, standard error, logs, and shareable handoff for both canaries, physical paths, raw file bytes, and private digests. Each must occur zero times.
5. Verify no package, host, database, migration, or legacy management-save path is invoked.

The external deployer must provide an artifact-integrity receipt in a later fixture. A bare handoff ID never authorizes a complete-bundle switch.

## Default-shell host observation (#2041)

Run the focused rebuilt-host tests in `tests/essentials/Workbench/Tests/CompositionActivationObservationTests.cs` and controlled readback tests in `tests/essentials/Modularity/Tests/ServerReadinessTests.cs`. The Workbench root's management-key-protected GET `/_admin/composition/default-shell/observation` reads the configured default shell without activating it. POST `/_admin/composition/default-shell/reload` captures the prior generation, reloads once, and reports the per-shell result plus point-in-time active generation/readiness. Verify unauthorized calls are refused; an absent prior generation is `null`; a failed candidate leaves the prior generation ready; non-default routes are absent; and raw errors, canaries, blueprints and credentials do not appear in either response. Every response says `candidateMatch=unverified`.

If an observer times out or loses the POST response, GET is safe readback before any operator decision. A generation number alone cannot establish that the reviewed candidate was activated or that the in-flight reload has finished. The controlled timeout fixture demonstrates that a later promotion can occur after an earlier readback showed the prior generation. No automatic retry or deployment switch is part of this host slice.

## Host-attestation gate before verified activation

The #2039 probes established that the current Workbench can report default-shell generation/readiness and private package/configuration state, but cannot bind a reviewed complete bundle to that generation. The gate remains open for a future verified-match implementation. Use a disposable rebuilt Workbench with one configured **default** shell and a prior ready generation; a future gate needs a host-produced observation tied to the generation being promoted:

1. Deploy a reviewed complete bundle as one retained immutable artifact through a test deployment owner that records previous/current identities and can restore the prior complete bundle. Restart the process for startup-bound changes; use shell-only reload only after its root projection is proven unchanged. Observe process identity, active generation and default-shell readiness. Separately prove the host's process/generation-bound candidate marker equals the reviewed artifact; otherwise report match `unverified`.
2. Alter one included file, then change a process-level override or loaded package generation. Show which source identity and effective-host facts the marker actually covers. Any uncovered fact stays unresolved; a mismatch cannot be called verified.
3. Make a shell-only candidate initializer fail after deployment. Assert per-shell reload failure, previous generation still ready, deployed artifact changed, and candidate match not verified. Separately fail a fresh-process start and report the previous process as serving only when observed. Repair or restore the prior artifact, restart or reload as eligible, and verify the resulting process/generation.
4. Drop or time out the activation response. Read back deployed artifact identity and active process/generation before retry; do not send a blind second apply. If state remains ambiguous, keep an `uncertain` result.
5. Include a non-default shell as a refusal case for v1 verified activation, and scan all outward results for canary values, blueprint configuration, management key and raw exception text.

#2041 can report `active generation observed; candidate match unverified`. #2060 shows both a consistent fresh-process copy and a mixed-source file-at-a-time counterexample; it does not supply a production deployment owner or candidate marker. Do not publish a `candidate active` result or cut a verified-match implementation story until this gate passes with a secret-safe host observation. The #2039 package test varied versions across host starts, not within one process. #2041's timeout test stops a client observer while the HTTP operation continues; an actual lost response and external-deployment recovery remain later full-activation gates. No test here treats shell readiness as evidence of database migration, connection affinity or data rollback; those remain separate program dependencies.
