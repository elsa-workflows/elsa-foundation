# Portable input v1 — proposed contract

Status: Draft, [specification #2457](https://github.com/elsa-workflows/elsa-foundation/issues/2457). Approved product model; technical contract is not implemented or yet approved for implementation.

## Public and private artifacts

The public envelope has exactly `schemaVersion: "1"`, `kind: "portable-composition"`, `composition` (complete unchanged authored v1), `requiredInput` (`kind: "workbench-json-bundle"`, `id`, `revision`), and `inputDisposition` (`origin` or `replacement`). Exactly one private input is required. ID and revision are fresh version-4 UUIDs independent of private bytes. Rebind retains logical ID and changes revision. Unknown fields, duplicate keys, null declarations, unsupported versions/kinds or unsafe context refuse. Actual shell/environment labels stay private in the receipts, not in this envelope or portable preview; generated input/revision tokens bind the context indirectly.

```json
{
  "schemaVersion": "1",
  "kind": "portable-composition",
  "composition": {
    "schemaVersion": "1",
    "catalog": {
      "id": "elsa-foundation",
      "version": "3",
      "digest": "6563d77f116b7aefb2a67425f28b72cab736297d4e46df964bf9fb507cf91c3c"
    },
    "profile": null,
    "groups": [],
    "add": [],
    "remove": [],
    "accepted": {
      "catalogDigest": "6563d77f116b7aefb2a67425f28b72cab736297d4e46df964bf9fb507cf91c3c",
      "featureIds": [],
      "locks": []
    }
  },
  "requiredInput": {
    "kind": "workbench-json-bundle",
    "id": "b6f2b17f-180a-4ee3-9d5d-4b42d81fd747",
    "revision": "739e9c76-fd64-467e-bbc2-e48163d50fba"
  },
  "inputDisposition": "origin"
}
```

This minimal envelope uses the actual bundled catalog v3 pin and an empty accepted selection to illustrate wire shape. A useful imported composition supplies its real feature/settings/resource content. The existing authored-v1 reader validates the inner object; this example is not host or execution evidence. Catalog/profile/group schemas/digests retain meaning.

No source path, owner prose, URI, private-byte hash, physical connection/provider value or unknown source content is public. Authored settings/resources must pass exact leaf/type review and existing forced-private persistence exclusions before public preview/publication. Unknown authored resource shapes cannot bypass the existing supported logical resource projection. `accepted.featureIds` certifies no values. Manual review is an operator assertion, not automatic secret detection; existing unsupported nonempty-array public edits still refuse. Only safe reviewed intent may enter a public-envelope digest.

A private input receipt has exactly `schemaVersion: "1"`, `kind: "portable-input-receipt"`, `inputId`, `inputRevision`, `publicEnvelopeSha256`, `context`, `files`. Context contains shell/environment; each file entry contains `name` and `sha256`. SHA256 fields are lowercase 64-digit hex. Full-envelope correlation uses exact accepted public bytes, not catalog `SelectionDigest` (its projection does not admit arbitrary settings). Receipt inventory/digests derive from private content and must stay private. Receipt has no source bytes or absolute paths.

Inventory is sorted, complete, unique including case-equivalent collisions, and contains only existing supported basenames. Required bundle: `shells.json`, `appsettings.json`, `shells.<environment>.json`; selected appsettings overlay is optional but its presence is bound by inventory. Preserve all supported sibling `shells.*.json`/`appsettings.*.json`, other shells/root nodes and unedited JSON including arbitrary private objects/arrays/null/empty/false/zero. Do not flatten source layers or infer unknown option semantics. Unsupported file kinds are not silently included.

Use existing opt-in finite regular-file capture: 32 files per collection, 1 MiB per file, 8 MiB per collection, plus existing inspection request bounds. Count envelope/receipt/review/catalog/profiles in supplied-input collection. Receipt-provided names cannot bypass path/byte checks. Reject links/nonregular inputs, malformed selected files and existing unsupported source shapes. Existing local-v1 bounds/behavior are unchanged.

Before preview/publication, match IDs/revision/public bytes and validate the private receipt context against the supplied bundle, full supported file inventory and exact byte hashes. Absent/extra/changed supported files or unrelated compatible source refuse. Recheck envelope/receipt/review/catalog/profiles and bundle at publication; never consult original absolute paths or ambient configuration. Receipt is trusted operator-owned integrity metadata, not signed authentication against an operator replacing both files and metadata.

## Proposed additive commands

```text
composition import --portable --host-dir <private-source>
  --shell <id> --environment <name> [--catalog <catalog>]
  [--setting-review <review>] --output-dir <fresh-artifact-pair>

composition accept --portable-composition <public-envelope>
  --private-input <complete-private-dir> --private-receipt <input-receipt>
  [--edited-composition <draft-authored-v1>]
  [--catalog <catalog>] [--workspace-profile <profile>]...
  [--setting-review <review>] --output-dir <fresh-artifact-pair>

composition rebind --portable-composition <public-envelope>
  --private-input <replacement-complete-dir> --private-receipt <previous-input-receipt>
  [--catalog <catalog>] [--workspace-profile <profile>]...
  [--setting-review <review>] --output-dir <fresh-artifact-pair>

composition generate --portable-composition <public-envelope>
  --private-input <complete-private-dir> --private-receipt <input-receipt>
  [--catalog <catalog>] [--workspace-profile <profile>]...
  [--setting-review <review>] --output-dir <fresh-candidate-artifacts>
```

Portable import/accept/rebind stage and atomically rename one fresh directory containing `public/composition.json` and `private/input-receipt.json`. No bundle bytes are automatically copied there. No pre-existing directory overwrite; no half-pair publication. Import excludes legacy `--output`; portable accept/generate exclude legacy `--composition`, `--host-dir`, explicit shell/environment flags. Context comes from the validated private receipt. Portable preview and public result redact actual shell/environment labels, including path-like or credential-bearing source labels accepted by legacy grammar; do not serialize the existing importer preview directly. No automatic acceptance, inline private values, stdin secrets or repeatable private inputs. Existing no-portability commands retain their semantics.

Acceptance first validates the unchanged accepted envelope against its receipt. Optional `--edited-composition` supplies a separately captured draft authored-v1 object for edits; it is admitted only by portable accept. Validate that draft against the same pinned catalog/private context and existing acceptance/re-resolution rules, and recheck its bytes before publication. Direct edits to the accepted envelope invalidate its receipt; do not weaken digest matching to accommodate them. Acceptance shares existing exact selection resolution and revalidates public safety; it creates a fresh revision/receipt for accepted changes and preserves existing `inputDisposition` (acceptance must never reset replacement to origin). Generation uses supplied bundle as sole source, existing candidate builder/activation readback and exact reviewed settings/logical resource patches. Untouched files are byte-identical; edited files retain all unedited semantic content. Existing object-map-only selection edits and unsupported mapping/type refusals remain. Generation requires a separate interactive redacted diff approval. Its safe preview and success output include the fixed `inputDisposition: origin|replacement`; candidate receipt must match envelope disposition. Do not label replacement as origin reproduction.

Generate stages/publishes one directory containing `candidate/` (only supported generated host files) and `private/candidate-receipt.json`. Candidate receipt has exactly schema `1`, kind `portable-candidate-receipt`, fresh `candidateId`, source `inputId`/`inputRevision`, `publicEnvelopeSha256`, `context`, `inputDisposition`, and actual generated `files` inventory. All IDs/hash/context/inventory validation mirrors input receipt, with fresh candidate UUID and output byte hashes. Receipts are not host files or shareable export. Host/deployer selects `candidate/`, not enclosing artifact directory. Neither half is published alone.

Operator separately supplies existing complete configuration and receipt through their own authorized private process. Tool does not pack/upload/download/encrypt/resolve/copy source configuration across machines or automatically export credentials. Absent declared private input leaves generation incomplete. Operators supply secrets required by their configuration; inventory integrity alone cannot detect a missing secret key inside an otherwise valid unknown subtree. Retain that operator-owned completeness limitation and existing supported mapping checks; do not invent a secret/option schema.

## Replacement and coexistence

`rebind` alone admits a different complete private bundle against the previous private receipt. The old receipt and unchanged public envelope must still match; the old bundle need not remain available. The explicit rebind skips old-bundle inventory equality only for the proposed replacement, not public/context/identity validation. It validates public safety, the full replacement under the previous receipt’s fixed private context, and supported reviewed mappings/types. Preview includes a fixed statement that private values may differ and origin reproduction is no longer claimed; requires interactive `rebind` acceptance. Fresh output keeps logical ID, creates new revision and marks disposition `replacement`. Old artifacts and source stay unchanged; generation then needs its own approval.

No silent receipt renewal, in-place change, implicit target override or unknown-field merge. Changed shell/environment requires fresh import. Operators prepare a complete private destination bundle for changed connections, then rebind. Tool cannot certify replacement unknown values equal origin values; the disposition remains truthful.

## Intended inspection

```text
composition inspect --portable-composition <public-envelope>
  --host <actual-installed-host-closure> --host-dir <candidate-dir>
  --private-receipt <candidate-receipt> --trust-host-code
  [--environment-input <private-intended-overlay>]
  [--catalog <catalog>] [--workspace-profile <profile>]...
  [--setting-review <review>] [existing bounded inspection options]
```

Context comes from private candidate receipt; legacy composition/shell/environment flags and private-input flag refuse. Parent validates receipt kind, public/input identity, private context, exact envelope/receipt disposition equality and complete generated candidate bytes, then uses existing candidate/explicit-intended lanes. Recheck candidate/receipt/envelope before/after bounded worker exchange. Private associations/hashes never enter public/worker results. Existing private worker requests may carry the selected shell/environment as today; the portable public projection must omit actual context labels from import, generation and inspection results, rather than printing legacy preview objects. No new host source layer/capability.

Optional Spec189 overlay retains its inspection-only precedence/privacy/refusals; does not rewrite candidate/receipt. Keep file-only `unverified` or explicit `supplied-intended` and existing unobserved/unavailable/not-performed facts. No unknown-option runtime consumption, deployed attestation, database readiness, connectivity or migration authority claim. Tampered generated files refuse; no manual receipt restamping workflow.

## Fixed safe refusals

| Case | Exit / code | Meaning |
|---|---|---|
| Malformed/unsupported artifact or conflicting options | 2 / `portable-input-invalid` | Invalid portable request/association. |
| Required input/receipt absent or unreadable | 3 / `portable-input-missing` or `portable-input-unreadable` | Supply required private configuration. |
| ID/revision/context/public digest or pre-existing inventory mismatch | 3 / `portable-input-mismatch` | Input differs; review replacement explicitly. |
| Captured input/inventory changes after preview | 3 / `bridge-source-changed` | Capture/review again. |
| Unsafe public authored settings/resources | 2 / `bridge-portable-unsafe` | Unreviewed/unsupported public content. |
| Missing/type-incompatible mapping or activation shape | Existing bridge exit/code | No guessing/omission. |
| Capture limits exceeded | 2 / `candidate-capture-invalid` | Supported bound exceeded. |
| Output conflict/cancellation/publication failure | Existing bridge exit/code | No overwrite/partial pair. |
| Intended inspection/worker failure | Existing candidate/Spec189 exit/code | Existing truthful refusal and owned cleanup. |

Messages are fixed: no values/paths/receipt hashes/raw keys/parser excerpts/peer exception text. Pre-existing mismatch differs from in-invocation drift. Cleanup removes only unpublished task-owned staging, never existing targets. Originals and unrelated target files stay unchanged. No local fallback or implicit omission. Inventory fidelity is not validation of unknown required secret/option semantics. Acceptance authorizes neither credential transport nor live activation.
