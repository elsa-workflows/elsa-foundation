# Explicit external environment inputs for candidate inspection

Status: executed discovery for [#2184](https://github.com/elsa-workflows/elsa-foundation/issues/2184), root and independent report review passed; delivery pending. Owner: [Runtime Composition & Configuration](../../program-goals/feature-composition-readiness.md), developer-composition epic [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962). Source baseline: `c24766adcb80e31f16040b779b7552b2d12e6300`, 2026-10-01. This extends the [developer persistence evidence](developer-persistence-evidence.md) and delivered [spec 187](../../../specs/187-effective-persistence-preview/spec.md), not production candidate v1.

## Finding and recommendation

An environment override can change both the logical persistence target and enabled features while candidate v1 truthfully stays on its captured JSON. A detached, privately owned configuration capture is sufficient for the actual runtime preparer and existing internal v2 tooling operation to agree on logical targets. It does not prove that those inputs came from a deployed target, that a database exists, or that migrations are ready.

**Go for one bounded specification of explicit, private environment inputs; no-go for silently extending candidate v1 or copying the CLI caller’s ambient environment.** The first contract should describe an operator/target-owner-supplied *intended configuration capture*, tied to one selected host, shell, environment and invocation. Self-declared owner metadata is not an attestation. A deployed-host-observed capture needs its own verifiable producer and remains a separate boundary. Host and input identities must be reconciled before composition, without publishing private input values or value fingerprints.

A new negotiated input capability is needed: the public candidate API cannot accept this external root today. Preserve v1’s four-file behavior and explicit external-input uncertainty. Use the existing EF preparer/resolver; do not introduce a second configuration engine. Preserve each host’s source order, then freeze the whole composed view once. Keep source provenance separate from target agreement and physical readiness.

## Actual source and lifetime boundaries

| Surface | Observed behavior | Consequence |
|---|---|---|
| [Workbench Program](../../../src/apps/Elsa.Workbench/Program.cs) | Default web-builder sources; adds base/environment shell JSON, then re-adds environment variables and command line. Registers `AddEfPersistenceResources`. | Environment wins over shell JSON; command line wins later. The prototype models only JSON plus a controlled environment provider. |
| [Foundation Host Program](../../../src/apps/Elsa.Foundation.Host/Program.cs) | Adds shell JSON after default builder sources without Workbench’s re-addition. Does not register that EF shell-resource preparer. | Do not apply Workbench order or claim an enrolled Foundation Host candidate lane. Host-owned cluster membership is a different consumer. |
| [Runtime preparer](../../../src/essentials/Modularity/EntityFramework/EfPersistenceShellSettingsPreparer.cs) | Captures supplied `IConfiguration.AsEnumerable()` into an in-memory root, checks the source reload token, calls `EfPersistencePreparation`. | Real preparation is reused. This is metadata preparation, not feature/context activation. |
| [Public tooling context factory](../../../src/essentials/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs) | Supports `workbench-json-v1` and `workbench-json-environment-v1`. The latter loads the worker process’s unprefixed ambient environment after the four files. | Selecting this existing mode does not establish that the worker’s environment is the target runtime’s. It is not a new candidate capture API. |
| [Public candidate operation](../../../src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs) | Builds a private root from exactly four captured JSON streams; public host method accepts request/response streams, not an external `IConfiguration`. | Production candidate v1 remains file-only. New external fields refuse before host discovery. |
| Internal tooling-context constructor | Can receive the identical test-owned root. `Dispose` disposes that root. | This is the unsupported test-only injection seam. Run runtime assertions before the tooling context disposes it; avoid shared-root use afterwards. |
| [Persistence adapter](../../../src/essentials/Persistence/EntityFramework/ResourceResolution/PersistenceConfigurationAdapter.cs) | Reports logical `root`, `shell-composed` and `shell-authored` scope, not exact winning-provider/file identity. | Neither generic scope nor the context’s source label is an authenticated origin receipt. |

The private prototype prefix is generated per fixture. Only its owned variables are changed, and each original value is restored on disposal. The real .NET environment provider is composed after the real four-file root, then the complete view is copied into a detached in-memory root. Separately merging two `AsEnumerable()` maps was rejected: structural null parent entries can corrupt lower scalar precedence. The final prototype uses the standard provider chain.

The detached root cannot reread ambient inputs on `Reload`, but it is still a mutable `IConfigurationRoot` if trusted code is given its setter. This experiment establishes source detachment and owner lifetime, not an immutable public configuration abstraction or sandbox. A production contract must own an immutable private input document/copy and avoid exposing mutable capture handles. Existing trusted host/composer code remains trusted.

## Executed evidence

The retained [noncompiled prototype patch](../evidence/external-environment-input-probe.patch) adds temporary cases/helpers to the existing `EfCandidateInspectionTests` fixture. Code-artifact SHA-256: `9fcb9bea69f3a2748e618c20e85f3001c5f36401de4f66373cb71a867ac5f9e8`. This identifies source code, never configuration values. No new project or permanent suite was added. The patch was removed before the restored full-suite run.

| Probe | Actual observation |
|---|---|
| JSON versus owned environment | JSON selects `primary`; the owned environment changes the root target to `root-base`. Actual runtime patch, detached EF preparation and internal v2 list consume the same frozen root and report that target. The real candidate producer consumes the same four file byte arrays and reports `primary`, with `externalInputs=unverified`, `runtimeParity=unobserved`. |
| Private connection input | A controlled private named-connection value reaches the shared root. Public tooling output contains neither that value nor unknown-setting canaries. Target verification remains `not-performed`. No connection is opened. |
| Mutation, Reload and removal | Existing capture stays on `root-base` after source mutation and `Reload`; deliberate new capture sees `primary`. Removing the variable reveals the later `appsettings.Production.json` selection `primary`. |
| Selection precedence | A shell default wins over the external root default; explicit feature binding wins over both. A later environment key overrides the authored shell binding before composition. |
| Feature selection | External `false` disables the actual Structured Logs EF feature and removes its persistence participant. File-only candidate selection still includes it. |
| JSON null presence | A captured JSON null remains a present invalid new-mode selector and refuses with `resource-selection-invalid`; it does not silently become an absent legacy/default selection. |
| Normalized-key ambiguity | Two owned raw keys using `__` and `:` fold into one case-insensitive configuration key. No winner is asserted. Raw source identities are lost after the standard provider loads them. |
| Unsupported ownership payload, two cases | Candidate v1 rejects both missing-owner and ambiguous-owner external payloads as `candidate-request-invalid`, before host discovery, without echoing values. This demonstrates a closed unsupported boundary, **not** an implemented ownership validator. |

Final focused run: **8 executed/passed, zero failures/skips**. The initial run had six passes and two harness failures because the new refusal assertion read `code` instead of `error.code`; that initial TRX/log is retained. The assertion and provider-chain implementation were repaired before the final run. No failed launch was counted as a pass.

Meaningful adverse controls each executed one case and failed its intended assertion:

- Reintroducing a live environment provider into the returned capture makes `Reload` reread changed input. The stability assertion fails.
- Omitting the environment provider from composition leaves the file target unchanged. The real divergent-target assertion fails.

After both mutations, source was restored and all eight cases passed again. Private fixture values and their SHA-256 forms are absent from final passing and adverse TRX/logs. The retained patch references existing private fixture constants; it contains no captured private value or value fingerprint. Then the temporary patch was removed, the project rebuilt, and the **full existing EF tooling/migrations suite passed 449/449, zero failures/skips**, including its existing public candidate-wrapper and context-contract coverage. Actual TRX identities contain no `Spike_` cases in that restored suite.

Results were inspected under `/tmp/runtime-composition-2184-probe/`: initial/final probe TRX/logs, both adverse controls, `restored-full-migrations.trx`, matching log and receipt. These are supplementary local evidence, not enduring hosted artifact links. The source patch, commands, counters, observations and limitations here make the result reviewable independently of those paths.

## Input semantics and provenance

Environment keys are string-valued. Removing a variable reveals the lower source; it is not a JSON-null tombstone. The executed JSON-null control is therefore separate. Empty environment values, service connection-string prefix mappings, case aliases, duplicate raw-key admission, whole-section replacement and command-line overrides were **not** fully exercised. Preserve these as explicit specification/compatibility gates, not invented passing coverage.

The loaded package is `Microsoft.Extensions.Configuration.EnvironmentVariables/10.0.10`. Its [pinned primary source](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Configuration.EnvironmentVariables/src/EnvironmentVariablesConfigurationProvider.cs) normalizes `__` into `:`, compares keys/prefixes without case sensitivity, maps service connection-string prefixes, and overwrites normalized entries as it enumerates. The executed alias control confirms information loss; deterministic collision detection needs raw owned inputs before that lossy provider stage. A future explicit lane should refuse ambiguous normalized keys rather than depend on enumeration order. Applying that admission rule to a new opt-in lane must not retroactively change legacy ambient-provider behavior.

The internal v2 helper intentionally retains its existing `workbench-json-v1` source label even though the injected test root contains environment inputs. That label is supplied by the internal constructor, not detected from providers. The comparison proves identical-root logical resolution only, **not truthful production external provenance or production support for that injected context**. The candidate control calls the real operation with a supplied assembly closure; existing restored-suite tests exercise its public host wrapper. No new installed-worker or deployed-target external-input journey was executed.

A future plan may explain that a target inherited a root default, used a shell default or had an explicit binding, and that a known external source family supplied a path. It must not claim exact winning file/key/provider provenance unless that information was retained and reconciled through composition. Freeze/disposal must not erase source-family metadata and then manufacture it from final values. Unknown values remain private; no portable export behavior is introduced.

## Required next contract

One specification follow-up can now define:

1. A separately versioned, negotiated environment-input capability; old hosts retain candidate v1 and cannot accept new payloads through a silent fallback.
2. Explicit selected-host/shell/environment/invocation binding and capture ownership. The first input is *supplied intended configuration*, not observed deployed environment. No automatic ambient-reader inheritance or self-declared attestation.
3. A privately owned immutable input representation; one complete capture feeds actual shell selection and the existing preparer/tooling consumers. Source drift, mixed captures, stale accepted IDs and mutation refuse before effects.
4. Host-specific order, scalar presence, null/removal/blank behavior, canonical key normalization and alias collision refusal. Bound entry count, key/value bytes and total private payload before composition; final numeric limits require the reviewed specification.
5. A closed redacted projection with separate selection/resource resolution, source-family provenance and unresolved physical/live evidence. No private values, excerpts, value fingerprints or arbitrary unknown portable export.
6. Real runtime/tooling same-capture tests, an honest file-only divergent control, producer-to-worker/client serialization and lifecycle proof, malicious/mixed/oversized inputs, private canaries, and compatibility controls. Reuse existing projects and the existing EF resolution engine.

[ADR 0076](../../adr/0076-persistence-tooling-runs-inside-the-host-closure.md) D1/D4/D7 and spec187’s file-only contract need an explicit reviewed extension before production implementation. Command-line/custom providers, deployed-host capture/attestation, physical databases, migration authorization, production builder/apply/recovery and portable unknown export remain their parent outcomes. Real developer/operator evaluation [#2064](https://github.com/elsa-workflows/elsa-foundation/issues/2064) is still a separate human gate.

## Reproduce the bounded probe

Use a disposable worktree at the stated source with SDK and restored dependencies. The existing fixture must be clean. Apply only the retained patch, run the eight cases, and always reverse it before rebuilding the restored suite:

```bash
(
  set -euo pipefail
  probe_patch=docs/reports/evidence/external-environment-input-probe.patch
  probe_fixture=tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs
  git diff --quiet -- "$probe_fixture"
  git apply --check "$probe_patch"
  git apply "$probe_patch"
  trap 'git apply --reverse "$probe_patch"' EXIT
  DOTNET_PROCESSOR_COUNT=3 dotnet test     tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj     --filter 'FullyQualifiedName~Spike_'     --logger 'trx;LogFileName=environment-input-probe.trx'     --results-directory /tmp/elsa-environment-input-probe
)
```

The recorded runs used `--no-restore` with existing restored dependencies. Reapply the two described capture mutations separately if checking the adverse proof, and restore the patch’s final source after each. Do not interpret the prototype as an installed public environment-input API.

## Review

Root reviewed the full provider chain, repaired the initial harness issue, executed/parsed the probe and adverse results, scanned private canaries, removed the temporary patch and inspected the rebuilt full-suite result. Luna Extra High independently audited the source seams and final prototype. One initial removal finding was withdrawn after rereading all four JSON layers and the actual passing TRX: the environment JSON overrides the earlier base default. Source-detachment, internal-context disposal, generic provenance and public-wrapper coverage limits are retained above. Luna Extra High independently reviewed the final canonical report, patch digest and actual TRX/receipt counters with no material finding. Root reviewed the recommendation and accepts one bounded specification follow-up; no implementation-ready production story is created by this discovery.

Generated maps were deliberately refreshed and checked at this source; no map or manifest bytes changed. Root reviewed both generated findings:137 source projects,131 test projects,105 features,219 specs, zero direct package-version clusters, four existing unindexed extension catalogs and zero runtime-to-design reference signals. Report-relative links, code-artifact digest, private-value/digest scans, clean patch apply and whitespace checks pass. Hosted exact-head review/CI and resulting-main gates remain required for report delivery.
