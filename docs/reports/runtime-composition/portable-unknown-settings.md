# Portable imported settings: ownership and transfer

Status: discovery for [#2327](https://github.com/elsa-workflows/elsa-foundation/issues/2327), under [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962) and [program #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959). Recommendation pending Sipke's product decision; this report does not approve an implementation contract.

## Finding

The current file bridge can reproduce reviewed settings against compatible local files. It preserves unreviewed values in a generated local candidate, but cannot reconstruct those values from the authored composition alone. A later invocation opens the source supplied for that invocation. With compatible paths, a different source's unknown values win, even when the accepted composition came from another source.

This is the documented invocation-local boundary, rather than a newly discovered source-drift defect. It leaves the program's portable unknown-settings outcome incomplete. A safe transfer needs both public intent and an explicit private configuration owner. Simply adding an export command, copying every value into authored JSON, or retaining a source path on the original machine would not supply that outcome.

Broad configuration classification remains deferred under Elsa constitution §E4. This investigation uses the accepted [file-bridge contract](../../../specs/176-composition-file-bridge/contracts/file-bridge-v1.md), [setting-review input](../../../specs/176-composition-file-bridge/contracts/setting-review-v1.md), and [explicit environment inspection contract](../../../specs/189-explicit-environment-inputs/contracts/cli-inspect-environment-v1.md). It does not ratify a general settings framework.

## Current ownership and source rules

- [CompositionImporter](../../../src/essentials/Modularity/Planning/Bridge/CompositionImporter.cs) exports only explicitly reviewed setting leaves and supported logical persistence references. A review is a human assertion, not a secret detector. Known physical persistence fields remain excluded even when reviewed.
- [CshellsSourceReader](../../../src/essentials/Modularity/Planning/Bridge/CshellsSourceReader.cs) and the importer retain JSON kinds and selected-layer presence. Selected shell overlay settings override matching base settings. A shell persistence default outranks the appsettings default; overlay outranks base within each source class. Omitted values, explicit null, false, zero and empty values are distinct.
- [CompositionCandidateBuilder](../../../src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs) starts from captured local source files. Authored changes need an exact reviewed type and an existing mapped path. Unknown siblings remain in the local copy. A missing path, incompatible type, missing resource definition or unsupported shape refuses instead of guessing.
- [CompositionFileSource](../../../src/essentials/Cli/CompositionFileSource.cs) requires base shells/appsettings and the selected shell overlay, captures supported sibling files, and rechecks every copied file before publication. Selected appsettings overlay is optional. Its source association is private and invocation-local; authored v1 stores no origin host identity or source revision.
- [SelectionJsonReader](../../../src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs) accepts already-authored opaque settings/resources. [CompositionAcceptCommand](../../../src/essentials/Cli/CompositionAcceptCommand.cs) preserves those nodes while accepting feature selection. Its preview omits their values. This preservation is separate from import safety: an accepted file is not certified safe to share. Generation still subjects authored settings to review and mapping checks.
- The [explicit environment lane](../../../specs/189-explicit-environment-inputs/contracts/cli-inspect-environment-v1.md) applies supported captured sources followed by an operator-supplied private overlay. It inspects intended persistence configuration; it does not transport arbitrary typed unknown JSON, observe ambient/deployed overrides, or certify arbitrary feature option semantics.

## Value decision table

The table describes current behavior. Proposed ownership below is not implemented.

| Value or owner | Import preview | Imported authored output | Generated local candidate | Later same-source reopen | Different/missing source |
|---|---|---|---|---|---|
| Reviewed safe scalar leaf | Value, type and selected source layer may be shown | Exact reviewed value | Patches an existing reviewed path of matching type | Fresh capture and generation diff review against current files | Compatible target path may receive the reviewed value; missing mapping refuses |
| Explicit false, zero, null, empty string | Presence and kind retained; a review permits the value | Retained as values, not absence | No implicit default, omission or deletion interpretation | Current path/type rechecked | No conversion of null/empty to missing; owning setting semantics remain separate |
| Reviewed empty object/array | Exact empty composite is a leaf | Supported with exact object/array review | Retained or patched at an existing matching path | Fresh capture | Missing/incompatible mapping refuses |
| Nonempty object with reviewed and unknown children | Individual leaves; unknown values masked | Only reviewed leaves reconstruct the portable subset | Unknown siblings remain in local files | Current source supplies the siblings again | Target source's siblings win; authored subset cannot reproduce origin siblings |
| Nonempty array | Indexed unknown values masked | Reviewed portable values inside a nonempty array refuse in the current importer | Untouched local array survives; separately authored indexed edits have narrower existing mapping rules | Local source required | No complete imported portable array claim; absent index/unsupported mapping refuses |
| Unreviewed unknown or secret-like value | Safe identity/type/presence; raw value masked | Omitted | Retained only in the local candidate | Reopened from current local source | Different source supplies its own value; no origin value can be recovered from public intent |
| Physical provider/connection and host-owned persistence fields | Forced nonportable/masked | Omitted; logical resource names are separate | Remain in private host files | Current configuration owner supplies them | Resource/path checks refuse unavailable mappings; no generic inheritance into private/vendor stores |
| Opaque value already in authored input | Accept preview contains selection, not opaque values | Generic accept preserves it, including secret-like data | Generation refuses an unreviewed authored leaf | Accept can preserve it again | Acceptance alone supplies neither safety classification nor origin association |

There is no automatic content-based safety inference. A misleading manual review can expose a field that the operator wrongly called portable. Parent object approval does not classify child leaves. Existing nonempty-array import restrictions must remain explicit rather than being described as universal JSON round-trip support.

## Reopen and drift decision table

| Situation | Current winning source / refusal | Required future distinction |
|---|---|---|
| Same source and same accepted intent | New local capture plus reviewed authored patches | Explain private input ownership alongside public selection |
| Source changed between invocations | New source is captured and separately reviewed; there is no persisted origin comparison | Reopen a bound private input, or explicitly review a replacement owner/source |
| Source changes after preview | `bridge-source-changed`; no candidate publication | Retain this refusal for every captured public/private input |
| Different compatible source | Reviewed authored leaves patch the target; unreviewed target values remain | Do not call this reproduction of the origin's unknown settings without an explicit transfer/rebind |
| Required source file missing | `bridge-source-missing`; no output | Missing required private input must remain visible and refuse complete generation |
| Required path/resource unavailable | Mapping/type/resource refusal | No silent fallback, omission, fabrication or copying unrelated values |
| Explicit environment input supplied | Supported intended overlay, private values withheld from projection | Keep intended configuration separate from deployed state and from private bundle transport |

## Executed actor and evidence

The [sanitized evidence ledger](portable-unknown-settings-evidence.json) records executed cases and artifact identities. Source inspection used main `4db78fbc1989aca36073376ae2637dc811059fea`; it changed only the Worker closeout documents relative to the previously qualified implementation. The actual Feedz `dotnet-elsa` 4.0.10-preview package has nuspec repository commit `edffd0a4ea18a17c4faa2c3f8ce073f48bc09af6` and SHA256 `74fe11835d3b8beeb749b2ca0b20abd5b238367304d44f2d52ee9fdf5ee4dd96`. This actor ran its extracted CLI, rather than substituting an in-process importer.

Disposable Workbench-style source files selected four actual features: ModularityApi, RuntimeFaultStackTrace, DiagnosticsStructuredLogs and DiagnosticsStructuredLogsEntityFrameworkCore. The selected shell used a declared `primary` SQLite resource and `Local` connection reference. Feature-local, root-local and connection canaries stayed in private source files. An exact review admitted boolean, number, string, null, empty object and empty array leaves; a separate false/nested-leaf fixture distinguished false from the overlay's true value. All prompt responses were supplied through a real terminal against disposable fixture data.

The final controlled runs completed fifteen file-command cases: eleven successes and four expected refusals. Three additional supported inspection cases succeeded:

- Public CLI import → interactive accept → same-source generation reproduced the reviewed values; complete parsed JSON equality held for every source/candidate file. Unknown nested values, arrays, root settings and connections remained local. Source files were preserved.
- A second source with a different unknown canary generated successfully and retained that source's canary. A source changed between invocations also generated after a new review. Neither outcome reproduced the origin unknown value from the authored file.
- Missing source refused with exit 3 / `bridge-source-missing`. Mutation after the generated diff and before approval refused with exit 3 / `bridge-source-changed` and no published directory.
- Adding an opaque canary to an already-authored document and accepting it preserved the canary in that private output, while omitting it from the public preview. Generation then refused the unreviewed leaf with exit 2 / `bridge-portable-unsafe`.
- The separate false/nested fixture exported false and only the reviewed child of a nonempty object, preserving its unknown sibling locally. Import without review emitted no settings. A reviewed nonempty-array element refused with exit 2 / `bridge-portable-unsafe`.
- A manual private handoff copied the fake origin bundle into a new input directory, made the original source directory unavailable, and generated a fresh candidate. Complete parsed JSON equality with the origin bundle held, including unknown nested/array/root values. Existing destination source files stayed byte-identical. An actual Workbench inspection consumed this candidate with a separate target-owned private connection overlay and retained its intended/unverified labels. The original fixture directory was restored byte-identically in teardown. This demonstrates the need for actual origin value transfer; it implements neither a required-input protocol nor destination merge or real secret transport.
- File-only and explicit-input `composition inspect` consumed the generated candidate against a disposable byte-identical copy of the actual existing Workbench closure. Both reconciled the four accepted/requested/effective features and reported the primary/SQLite/Local participant. The former reported `externalInputs: unverified`; the latter reported `supplied-intended` and the explicit-environment source policy. Private values and input paths were absent. The fixture database was absent and source/input/accepted files remained unchanged.

Unreviewed imported canaries were absent from every captured public stream and the imported/accepted public-intent artifacts. The deliberately contaminated, already-authored acceptance control is a private artifact, not a safe-export success. The inspection projects persistence and selection only: it does not inspect unknown option values or prove their runtime consumption. Exact file provenance remained unavailable; target verification was not performed, runtime parity/activation were unobserved, and connectivity/schema/migration/package reachability were unverified. Workbench artifact hashes bind the copy actually executed; its historical build source was not reconstructed, so no fresh exact-head Workbench-build claim is made.

No database, activation or deployed readiness outcome is claimed. No new test project, EF suite, provider matrix or CI cadence was introduced. The shared machine load exceeded 700; no local build was started. The first private runner completed, and an accidental repeat refused an existing output; clean, unique final directories supplied the ledger. That repeat is not counted as a product failure or as final qualification.

### Reproduction

Use the existing [CLI fixture](../../../tests/essentials/Cli/Tests/CompositionBridgeFixture.cs), [terminal helper](../../../tests/essentials/Cli/Tests/PseudoTerminalCli.cs), and [Workbench inspection fixture](../../../tests/essentials/Cli/Tests/CandidateInspectionFixture.cs) as setup references; their presence is not a test-pass claim. Source audits inspected existing tests but did not run them.

Create fresh private directories with base shells/appsettings, a selected Production shell overlay, the four features above, a declared resource/connection, unknown nested/array/root canaries, and six exact reviewed JSON kinds. Add a separate reviewed false and a reviewed child with an unreviewed sibling. Use the pinned catalog v3 and the extracted produced CLI. The command sequence is:

```text
dotnet <produced-Elsa.Cli.dll> composition import --host-dir <source-A> --shell default --environment Production --catalog <catalog-v3> --setting-review <review> --output <fresh-imported>
  # Respond: accept
dotnet <produced-Elsa.Cli.dll> composition accept --composition <fresh-imported> --catalog <catalog-v3> --output <fresh-accepted>
  # Respond: accept
dotnet <produced-Elsa.Cli.dll> composition generate --host-dir <source-A> --shell default --environment Production --catalog <catalog-v3> --composition <fresh-accepted> --setting-review <review> --output-dir <fresh-candidate>
  # Respond: generate
dotnet <produced-Elsa.Cli.dll> composition inspect --host <disposable-actual-Workbench-closure> --host-dir <fresh-candidate> --shell default --environment Production --composition <fresh-accepted> --catalog <catalog-v3> --setting-review <review> --trust-host-code --format json
  # Repeat with --environment-input <private-version-1-input>
```

For an origin-unavailable control, privately copy the fake origin bundle to a fresh directory, make the original fixture source unavailable, generate against that copy, then inspect with a separate private target connection overlay. Assert full origin JSON fidelity, unchanged destination files and restored original files in teardown; this is a filesystem handoff simulation, not a security or production transfer-protocol claim. For transfer, repeat generation against a compatible source B with different unknown values; compare the unknown subtrees. For drift, mutate a captured file at the approval prompt. For missing source, supply an empty source directory. For opaque acceptance, add an unreviewed fake canary to authored settings, accept, then generate. For the array refusal, review one element of an existing nonempty array. Scan all streams and public-intent files for private canaries and paths; separately assert private candidate fidelity and absence of the database. Keep any contaminated control output private.

Private executed receipts and runners are under `/tmp/runtime2327-probe`, `/tmp/runtime2327-probe-final`, `/tmp/runtime2327-probe-extra` and `/tmp/runtime2327-probe-handoff`; the sanitized committed ledger carries the result without those value-bearing artifacts. Reproduction must create fresh destinations, not overwrite this evidence.

## Alternatives and recommendation

| Alternative | Full portable outcome | Trade-off |
|---|---|---|
| Reviewed nonsecret leaves plus declared, required private configuration inputs | Can reproduce all supported original values when the destination supplies the corresponding privately transported input; reviewed target replacements are explicit | Public intent alone is intentionally insufficient. An operator owns private transfer and replacement review |
| Durable private origin association only | Improves same-machine reopen/drift but cannot recover an unavailable origin on another machine | Useful supporting mechanism, insufficient program outcome |
| Exact reviewed mapping for every unknown leaf before portability | Can work where all owning semantics and target mappings are known | Large unknown trees/arrays require extensive review; unclassified values still need an explicit private route |
| Copy arbitrary values into shareable authored JSON | Does not meet privacy/ownership requirements | Reject; JSON shape/name alone cannot establish safety |

Recommend the first alternative, conditional on actual origin-to-destination transfer and an approved ownership contract. Keep reviewed nonsecret intent portable and declare logical required private inputs for the remaining supported configuration. The origin-owned input must actually be available at the destination and used as the required value source. Binding the declaration to a different destination `--host-dir` is insufficient: the executed counterexample keeps that destination's unknown values.

The existing supported host JSON bundle is a candidate private value carrier because it retains objects, arrays, null and empty values without a generic setting interpreter. A future contract must specify which origin-owned files/subtrees are reproduced and which destination-owned settings are explicitly replaced, with their precedence and replacement approval. It cannot silently substitute destination unknown values or overwrite unrelated destination configuration. That coexistence rule is unresolved specification work, not functionality supplied by a local association. The current `--environment-input` lane is inspection-only and admits flat string entries; it cannot carry this arbitrary typed JSON bundle.

This recommendation does not authorize copying or transporting real secret values. The fixture uses fake canaries only. Secret references, owner-approved secret delivery and any authority to move credentials need an explicit contract; where a required secret cannot be supplied safely, the portable outcome remains incomplete. The tool must not automatically export credentials or upload a private bundle. Operator-owned secret storage is separate from public intent, GitHub records and previews.

The logical declaration must be safe to display and contain no physical path, value or public fingerprint derived from secret-bearing bytes. A private association can retain source/owner identity and revision checks. A required input that is absent, unavailable or changed must refuse complete reproduction. Rebinding to another owner/source needs a fresh explicit review, with truthful disclosure that target values can differ. Existing intended-environment overlays remain separate and preserve the supported source precedence. Private transport must cover the full supported source bundle, including unrelated preserved siblings; it cannot mean local preservation only.

The builder could then explain one missing configuration input and let an operator bind it, instead of silently dropping settings or presenting every unknown field as a checkbox. This is a proposed UX consequence, not evaluated human usability evidence. [#2064](https://github.com/elsa-workflows/elsa-foundation/issues/2064) still needs its actual participants.

## Product decision and smallest follow-up

Sipke must decide whether portable compositions should explicitly depend on privately supplied configuration inputs, with refusal when they are missing. The alternative is to require owner-reviewed nonsecret mappings before a composition may be called portable, leaving other compositions explicitly incomplete until such mappings exist. Neither alternative may silently omit unknown values or call local-only preservation the full outcome.

After that decision, refine one specification checkpoint under #1962. It must define the versioned public/private boundary, logical owner/input identity, private binding/revision lifecycle, complete bundle scope, cross-machine transfer/rebind approval, source precedence, typed JSON fidelity, contaminated-authored-input handling, upgrade/legacy compatibility and fixed safe refusals. Do not change authored v1 interpretation implicitly or treat generic `accept` as a portability certificate.

Its acceptance should extend the existing file/tooling fixtures: real produced CLI import → acceptance → transfer without the original directory → generation → supported inspection; unchanged and deliberately changed owner inputs; absent/drifted required inputs; false/zero/null/empty/object/array fidelity; safe reviewed edits; exact-source privacy scans; and separation of intended inspection from activation/readiness. Use existing projects and fixtures, not another EF suite.

An implementation story can become Ready only after that contract and the owner choice are approved. Host-private source association alone, a safe settings subset, or another planning-only fixture must not close #1962's portable outcome. Authoring publication, the six-person UX study, production builder and apply/recovery remain independently required. Program #1959 is active.
