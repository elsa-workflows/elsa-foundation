# File bridge v1 contract

Status: file-only import delivered under [#2019](https://github.com/elsa-workflows/elsa-foundation/issues/2019) and reviewed candidate generation under [#2023](https://github.com/elsa-workflows/elsa-foundation/issues/2023). This narrows the [source investigation](../../../docs/reports/runtime-composition/import-export-boundary.md) to supported local files. It does not claim a running-host converter or in-place apply.

## Authority and source association

Three outputs have different jobs. A **portable authored composition** records human-approved selection and only reviewed, safe logical references. A **redacted plan** derives selection/dependency findings from the existing planner and omits opaque settings and resources. A **generated host configuration bundle** is a local candidate made by patching copies of supported existing files. Neither the plan nor authored document can reconstruct a host without its local source files.

The first bridge keeps a **source association only for the current invocation**. The caller names a local host directory, shell, environment, pinned catalog, and fresh destination. The supported layout is Workbench-style CShells JSON: required base shells.json and appsettings.json, with shells.<Environment>.json and appsettings.<Environment>.json when present. Other shell/appsettings environment files are copied unchanged into the candidate so they are not lost, but their effective values are not inspected. A caller who explicitly names an overlay that is absent gets a missing-source refusal. A different host loading order is unsupported until evidenced; the bridge does not infer one from a directory scan. Symlinks or paths escaping the host directory refuse.

The bridge reads every file it will copy, selected or unselected, into one frozen in-memory snapshot and retains private per-file change tokens. It checks all of them again before acceptance and before candidate publication. Unselected files have no effective-configuration claim, but a change to one invalidates the snapshot just as a selected-file change does. It never prints or serializes the tokens, source bytes, physical paths, or raw unknown values into portable output. A later invocation reopens the sources and requires a new preview and diff review; no persisted sidecar or accepted document purports to prove that the source is unchanged since an earlier invocation. A revisioned in-place apply is [#1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964).

A preview is not an accepted baseline. Acceptance is an explicit decision tied to the preview in the same invocation. An unattended caller cannot silently select an accept-all default in v1. The accepted authored file is written only after that decision and a fresh source-token check. Export from an accepted document is a separate local action that re-reads the named source bundle, shows a redacted diff, requires explicit approval, and writes a fresh destination. Cancellation writes nothing. A future web builder uses the same preview, decision, and candidate states; its session transport is not defined here.

## Selection, provenance, and portable projection

The bridge observes the selected shell's base feature object-map or array and selected environment overlay using the host's precedence for these files. It records explicit object-map enabled/disabled state and enabled array entries. In the pinned CShells package, array objects have no disabled-state encoding: `Enabled` and `State` are settings, not activation flags. An explicit authored removal targeting array syntax therefore refuses in v1; it is not silently translated to an absent array entry. Case-equivalent or repeated IDs within one layer are invalid. A selected object-map overlay may override settings for an object-valued base entry. If a feature changes between scalar and object values across layers, v1 refuses: the pinned package may retain the base scalar value and ignore the apparent override. Array overlays replace numeric indexes, leaving untouched base indexes in place. An effective duplicate after layering also refuses. A change between object-map and array shapes across layers refuses. It does not assign a released profile, resolve a package, or treat categories as activation policy. The planner from [spec 174](../../174-profile-selection-planner/spec.md) remains the sole selection/dependency engine.

For an accepted no-profile baseline, enabled IDs become explicit additions and accepted feature IDs. Explicitly disabled object-map IDs become explicit removals and remain disabled in copied host files. Groups are empty, profile is null, locks are empty, and the supplied catalog pin is exact. If the planner reports unresolved IDs or dependencies, acceptance may preserve the intent but must keep those findings visible; it cannot label the host ready.

The portable settings projection is intentionally narrow. A reviewed field may enter authored settings only when its feature/field identity, value type, safe-to-export classification, and intended output layer have been accepted. For the fixture below, A.Flag and A.Limit are reviewed nonsecret fields; A.Future is unknown and stays local. A reviewed existing field is patched in its recorded source layer (A.Flag in Production, A.Limit in base). A newly authored field without an explicit target layer refuses rather than guessing. Unknown fields appear in preview by safe identity, type, and presence; nonempty raw values are masked. Explicit null may be displayed as null, but is never inferred to mean absent. The local generated bundle retains untouched unknown values.

Only logical persistence names enter the portable resources projection, for example a shell default resource name and per-feature binding names. Provider, physical connection string, ConnectionStrings node, and private/vendor store options stay in host files. A new logical name must be present in the selected local resource definitions; existence is still not proof of connectivity or transaction compatibility. The source's ConnectionName reference can remain in generated local appsettings, but no value under ConnectionStrings is copied to the authored document. Any unknown nonempty value proposed for portability requires explicit mapping under a later reviewed contract; v1 refuses that edit rather than guessing whether it is a secret. The authored v1 top level is unchanged; its optional settings and resources objects carry only this reviewed projection.

The redacted preview and plan may show safe field paths, type/presence, exact feature IDs, logical resource names, source layer labels, and unresolved codes. They cannot show raw paths, raw unknown values, inline connection values, arbitrary source excerpts, or claims that environment/command-line overrides, packages, running host, or database targets were checked.

## Two-shell acceptance fixture

The required local fixture has four relevant documents:

- Base shells.json: default shell A enabled with Flag=false, Limit=0, Future.x=null; B explicitly disabled; default shell Configuration binds A and shell default to primary. A sibling tenant-b shell has TenantOnly.Keep="unchanged". CustomRoot.Keep=true.
- Production shell overlay: default A.Flag=true plus an unrelated root annotation.
- Base appsettings.json: Elsa:Persistence:Resources:primary has provider PostgreSql and ConnectionName Shared; ConnectionStrings:Shared contains a local canary value. Other logging/CORS settings are unrelated.
- Optional unrelated Staging files: unchanged byte-for-byte if included in the candidate.

A second canary is a nonempty unknown field under A.Future. Both canaries are test data, not real credentials. The fixture defines A.Flag and A.Limit as reviewed, safe settings; A.Future is deliberately unclassified. Preview for default/Production reports A enabled, B disabled, effective Flag=true with Production provenance, Limit=0 with base provenance, Future.x present and null, A.Future's other value present but masked, and binding primary. It says package inventory, process overrides, provider reachability, and physical layout are unchecked.

An accepted authored baseline is schematically:

~~~json
{
  "schemaVersion": "1",
  "catalog": {"id": "reviewed-catalog", "version": "1", "digest": "<real pinned digest>"},
  "profile": null,
  "groups": [],
  "add": ["A"],
  "remove": ["B"],
  "accepted": {"catalogDigest": "<same digest>", "featureIds": ["A"], "locks": []},
  "settings": {"A": {"Flag": true, "Limit": 0}},
  "resources": {"persistence": {"defaultResource": "primary", "bindings": {"A": "primary"}}}
}
~~~

The digest placeholder is schematic; an executable fixture supplies a valid current digest. Unknown A.Future and the physical provider/connection do not enter the portable file. Editing reviewed A.Limit to 1 then generating a fresh candidate changes that field in copied base shells.json; it does not move Flag out of the Production overlay. Parsed JSON comparison must show tenant-b, B disabled, Future.x=null, CustomRoot, unrelated overlay annotation, and appsettings nodes unchanged. Original source files remain byte-identical. Untouched recognized files in the candidate remain byte-identical where possible. A host-specific serializer may change formatting of a touched file; semantic JSON equality outside reviewed paths is the contract.

## Failure and publication boundary

| Code | Trigger | Outcome |
|---|---|---|
| bridge-source-missing | Required or explicitly named source absent | No preview or published output |
| bridge-source-unreadable | Permission, unsafe path, symlink, or read failure | No preview or published output |
| bridge-source-invalid | Malformed JSON, unsupported shape, or invalid layer | No preview or published output |
| bridge-source-duplicate | Duplicate JSON key or case-equivalent feature ID | No preview or published output |
| bridge-source-changed | Any copied file differs from its frozen snapshot before acceptance/publication | Refuse; re-preview required |
| bridge-review-required | Acceptance or diff approval absent/cancelled | No authored/candidate output |
| bridge-portable-unsafe | Proposed portable value is inline, unclassified, or secret-like | Refuse portable output; retain local source |
| bridge-mapping-unresolved | Edited field has no reviewed layer or logical reference target | Refuse candidate; report safe identity |
| bridge-output-exists | Destination exists or overlaps a source | Refuse without overwrite |
| bridge-output-failed | Candidate cannot be fully prepared | No partial published destination |

Preparation may use temporary local work, but publication is all-or-nothing: no output directory is exposed as a successful candidate until every file is complete and the source tokens still match. Failure cleans temporary work without touching source or pre-existing destination. The authored acceptance and candidate generation are separate outputs and decisions; each must be atomic on its own. Exit/status mapping can be chosen during implementation planning, but these refusal codes and redaction behavior are stable contract inputs.

The first implementation must demonstrate the fixture end to end, including canary scans over stdout, stderr, logs, redacted plan, and portable authored file; parsed subtree comparison of source/candidate; refusal cases; and zero package, host, database, migration, or management-save calls. These checks prove file fidelity and disclosure boundaries, not runtime readiness.
