# First developer composition plan command

Status: reviewed contract from [#1999](https://github.com/elsa-workflows/elsa-foundation/issues/1999), implemented as the offline command in [#2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001), under [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962). Source baseline: `47c3ff92641ec3063499d15f27f6b6328e5cf8e6`. This contract covers file-only inspection; it does not publish profiles or prove runtime readiness.

## Decision and user task

Add `dotnet elsa composition plan` to the existing [Elsa CLI](../../../src/essentials/Cli/ElsaCli.cs). A developer with a pinned selection catalog and an authored composition can ask, before starting a host, “which exact feature IDs would this choice select, why, and what remains unverified?” The first command reads files and calls the existing [selection planner](../../../src/essentials/Modularity/Planning/Services/SelectionPlanner.cs). It does not run the CLI's persistence worker, resolve a host directory, connect to a database, or turn a candidate into accepted state.

```text
dotnet elsa composition plan \
  --catalog ./selection-catalog.json \
  --composition ./composition.json \
  --format json
```

`--inventory ./host-inventory.json`, `--persistence-evidence ./resource-hints.json`, and repeatable `--workspace-profile ./profile.json` are optional. Without an inventory, the same exact selection is produced with `inventory-unverified` and `persistence-unverified` findings. The command name `composition plan` is intentionally distinct from the existing `persistence plan`, which reports EF migrations against a selected host. The CLI is already packaged as `dotnet-elsa` and its front end has no EF project reference; the [worker boundary](../../../src/essentials/Cli/Elsa.Cli.csproj) exists for persistence operations. The [planning assembly](../../../src/essentials/Modularity/Planning/Elsa.Modularity.Planning.csproj) has no package dependencies, so referencing it from the CLI front end preserves a no-database path. A separate tool would duplicate command distribution and error conventions before there is evidence of a distinct audience or dependency need.

## Input and evidence boundary

The first two files use the existing schema `1` and [strict JSON reader](../../../src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs). The authored document contains pinned catalog/definition references, explicit additions and removals, and a **previously accepted** exact set with package locks. The plan is a **candidate** calculation: it may differ from that accepted set and must show `candidate-re-resolution` without rewriting the file. Catalog/profile digests use the existing `sha256-jcs-v1` rules in the [planner contract](../../../specs/174-profile-selection-planner/contracts/selection-planner-v1.md). Reuse [SelectionPlanner.Plan](../../../src/essentials/Modularity/Planning/Services/SelectionPlanner.cs) for selection and [HostAssessment](../../../src/essentials/Modularity/Planning/Services/HostAssessment.cs) for supplied host evidence. Do not build a second dependency or membership engine in the command.

An optional inventory file is a **supplied snapshot**, never a live host query. The existing [typed inventory](../../../src/essentials/Modularity/Planning/Models/HostInventory.cs) records target ID, source, observation time, loaded/installed/absent/unknown features, descriptor and manifest edges, package locks, and compatibility. There is no inventory JSON reader yet; the implementation story must define a strict, versioned `schemaVersion: "1"` adapter for this type, reject duplicate keys/rows and invalid enum values, and keep the same validation as `HostAssessment`. Its reported source and time identify what was supplied; they do not authenticate it. Runtime descriptors govern loaded-feature dependencies, including an observed empty list. Manifests are lower-confidence evidence for unloaded features, not proof of feed availability or loadability. Optional companions remain suggestions, and explicit removal of a required dependency stays a visible unresolved finding.

The v1 inventory adapter takes this shape (shown with a host-bundled feature; `runtimeDependencies: null` instead means no loaded descriptor was observed):

```json
{
  "schemaVersion": "1",
  "inventoryId": "snapshot-42",
  "targetId": "staging",
  "observedAt": "2026-09-25T09:00:00Z",
  "source": "supplied-snapshot",
  "features": [
    {
      "featureId": "Events",
      "availability": "loaded",
      "runtimeDependencies": [],
      "manifestDependencies": null,
      "manifestReadStatus": "absent",
      "package": null,
      "hostBundled": true,
      "compatibility": "unknown",
      "evidenceSource": "target-export"
    }
  ]
}
```

Missing optional fields do not silently become observations: the adapter requires every feature-row field shown, with explicit null for unavailable evidence. It parses an ISO 8601 offset timestamp and requires a unique feature ID per row. It adds only the schema marker around the existing typed inventory; it does not invent a live inventory producer.

Workspace profile files use the existing reader and must match authored immutable references. Unknown feature IDs are retained in the authored file and in the candidate selection when they are safe identity labels; a missing inventory observation yields `feature-unknown`. Unknown objects under `settings` and `resources` remain opaque [authored content](../../../src/essentials/Modularity/Planning/Models/SelectionDocuments.cs). Planning must neither parse them into invented settings semantics nor output or modify them. A later exporter/editor must own their lossless round trip.

The optional `--persistence-evidence` file has a deliberately limited v1 shape: `schemaVersion: "1"`, `source` (safe label), and `resourceReferences` (unique, safe labels). It has **no** `status`, provider, connection, credential, schema, or migration field. The adapter constructs the typed [PersistenceEvidence](../../../src/essentials/Modularity/Planning/Models/HostInventory.cs) with `status: "unchecked"`, `provenance: "supplied-file"`, sorted resource reference IDs, and an unresolved reason such as `external-evidence-unverified`. Thus the command can show the safe resource targets the developer supplied, without treating them as effective runtime bindings. The planner's current `checked` value is caller asserted, with no provenance verification; a user-editable file must never be allowed to set it. Without this file, output `persistence.status: "unchecked"`, `provenance: "none"`, and the planner's `persistence-unverified` finding. A later host-produced, redacted evidence adapter may supply source context, checked scope, and effective resource targets after it has a trust and drift contract. It may reveal a safe resource **name**, source kind, and whether an external value was present and checked; it must never reveal the value, connection string, credential, secret-store key, raw environment variable content, or driver exception. An externally managed value with no trusted check remains `unverified`, even if its reference exists in a file.

```json
{"schemaVersion":"1","source":"developer-file","resourceReferences":["primary"]}
```

## Output contract

`--format text` is the default for a person; `--format json` emits one deterministic JSON object on stdout, with `schemaVersion: "1"`, `kind: "composition-plan"`, `evidenceScope: "supplied-files"`, `candidate`, `accepted`, `reasons`, `dependencyEvidence`, `findings`, `observedLocks`, `catalog`, `inventory`, and `persistence`. Both formats must use the **same** planner result; presentation cannot add features or reclassify findings. `candidate.featureIds` and accepted IDs are distinct. The JSON shape is a redacted projection of [SelectionPlan](../../../src/essentials/Modularity/Planning/Models/SelectionPlan.cs), not a raw serialization of `AuthoredComposition`. Include catalog ID/version/digest, inventory target/source/observation time when supplied, source-tagged selection/removal reasons, sorted unresolved/advisory findings and observed package locks. `persistence.resourceReferences` contains only supplied safe IDs and remains `unchecked`. A finding explains source and affected feature/dependency without implying activation. There is no `runtimeReady` or `deployable` field.

`dependencyEvidence` needs a small additive planner-result field because today's result emits findings for **missing** required and optional edges but does not expose all observed edges or reviewed definition explanations. Have the planner produce sorted, source-tagged rows with `featureId`, `dependencyId`, `mode` (`required` or `optional`), `evidenceKind` (`reviewed-definition`, `runtime-descriptor`, or `package-manifest`), and whether the target is in the candidate set. Reviewed definition explanations are rationale, not host-observed edges; for a loaded feature, runtime descriptor edges govern and even an empty observed list is meaningful. Manifest edges are operative only where runtime descriptor evidence is absent. This extends the existing pure planner result for both CLI and future UI; it does not make the CLI a second dependency engine, add missing features, or upgrade metadata to a host guarantee.

For a candidate `{A,C,D}` with an observed required `A -> B` and a supplied `primary` resource reference, the text view should read along these lines:

```text
Candidate: A, C, D (3 exact IDs); accepted: A, B, C, D (4 IDs)
Missing required dependency: A -> B [runtime descriptor; B was removed]
Resource reference: primary [supplied file; unchecked]
Live host readiness: not assessed; persistence: not verified
```

The plan model already sorts feature IDs, reasons, findings and locks. The command's projection must fix property order and array ordering so equivalent set-valued inputs with different whitespace, object-key order, group/member declaration order, and opaque settings/resources yield byte-identical JSON **when all supplied evidence identities and timestamps are equal**. Catalog/definition digests follow the existing semantic rules; the command must not make a digest of the whole authored file or include secret-bearing opaque content. Inventory observation time or source changes are meaningful evidence changes and can change output bytes. Text should lead with the exact candidate count, accepted-vs-candidate difference, and unresolved count, then reasons and checks by source; never summarize unresolved findings as “ready.”

Safe-output validation is a prerequisite to rendering. The [JSON reader](../../../src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs) currently accepts any nonempty feature ID and some exception messages interpolate untrusted IDs. The [reference rule](../../../src/essentials/Modularity/Planning/Json/SelectionValueRules.cs) currently protects package/evidence/resource labels, not all candidate IDs or definition labels. The implementation must reject unsafe identities with a generic code/message that does not echo their contents (or make the core planner and both consumers enforce an equivalent safe identity rule). The planner also copies free-text definition rationale into `SelectionReason`; because a supplied catalog or workspace profile is not authenticated, the command must not serialize that raw rationale. Its redacted projection explains inclusion through action and source kind/ID, and maps finding codes to fixed, reviewed human text. It must render errors from known codes and safe fields, not echo raw JSON, opaque settings/resources, parser excerpts, paths containing secrets, or arbitrary exception messages. Test with a fake connection string placed in every untrusted string position that could reach stdout/stderr, including rationale and finding labels. This is a command output boundary, not a claim that the existing planner already sanitizes every string.

## Refusal and negative examples

Valid plans return exit `0`, even with unresolved findings, because the requested inspection succeeded; automation must inspect `findings` or `persistence.status`. Malformed/unsafe schema, duplicate key, tampered digest, or unsupported format return exit `2` with a stable refusal code on stderr and no partial JSON on stdout. An unreadable/missing input or failed input resolution returns exit `3` with a safe code and no partial JSON. These reuse the CLI's [exit-code vocabulary](../../../src/essentials/Cli/Worker/WorkerContract.cs), without making `persistence plan` semantics apply to this command. Cancellation uses the existing CLI convention. No user input can select a database operation through this command.

| Supplied case | Required observation |
|---|---|
| Profile `{A,B}`, group `{B,C}`, add `D`, remove `B` | Candidate `{A,C,D}`; both inclusion sources and the explicit removal of `B` appear. No file changes. |
| Add unknown `X` and omit inventory | `X` remains selected; inventory and persistence are unverified. No invented package lock. |
| Supply inventory with loaded `A -> B`, but remove `B` | `required-dependency-missing` names the descriptor source; `B` stays absent. |
| Supply reviewed optional `A -> C` and an observed required descriptor `A -> B` | `dependencyEvidence` separates reviewed optional rationale from the governing runtime edge; neither adds an ID. |
| Change pinned catalog content without matching digest | Refusal or unresolved pin as defined by the existing reader/planner; no silent re-resolution or accepted-lock update. |
| Put `Server=db;Password=sentinel` in `resources`, `settings`, rationale, or an unsafe ID/label | No `sentinel` in stdout/stderr or JSON; opaque objects stay untouched, unsafe identity is refused safely. |
| Supply resource hint `primary` in `--persistence-evidence` | The plan shows `primary` as a supplied, unchecked reference; it does not report a provider or effective connection. |
| Supply an inventory whose timestamp is old or whose package is merely installed | Report that exact supplied observation; do not claim a current host check or remote package availability. |

The existing [reresolver](../../../src/essentials/Modularity/Planning/Services/SelectionReresolver.cs) compares two explicit immutable snapshots. Re-resolution as a separate command or flag, accepted-lock writing, CShells export, legacy import, and source-precedence/live-host drift checks follow later. The current first command can report **what the supplied files say** and compare the candidate with the accepted lock; it cannot report active host environment/CLI overlays, current package loading, effective database equality, migration readiness, or management-store revision. Those depend on the [effective-configuration boundary](effective-configuration.md), [shared-persistence spec](../../../specs/173-shared-persistence/spec.md), and separate host evidence. The pending API-free Embedded-profile decision does not change this file-only inspection contract.

## Follow-on work boundary

The CLI front-end command, strict inventory and resource-hint adapters, additive dependency evidence, safe redacted plan projection, deterministic text/JSON renderers, and command tests were delivered in [#2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001). The command references the existing planner assembly and leaves `persistence plan` and its worker untouched. A separate export/import story needs a lossless authored-document serializer, secret-reference policy, unknown-content round-trip tests, and comparison to the actual CShells output contract. The [selected-host evidence spike #2003](selected-host-evidence-boundary.md) investigates source precedence and trust before a host-check story is filed. None of these follow-on stories should release the proposed Embedded, Authoring, or Worker planning fixtures as profiles without their remaining product and host gates.
