# Research: pure profile selection planner

## R1 — Ownership and placement

**Decision:** Put the deterministic parser and planner in a non-activatable `Elsa.Modularity.Planning` helper library. Keep the current Nuplane feature-catalog contributors unchanged. A later command/API adapter can translate live evidence into the planner's supplied inventory.

**Rationale:** `Elsa.Modularity.Core` currently has no package references and owns contract models, while `Elsa.Modularity.Nuplane` references CShells, package manifests, and Nuplane Admin. The planner is substantial reusable behavior but needs none of those implementation dependencies. This placement respects framework §§2.1, 2.3 and 2.19, and offers one implementation to the planned command and builder API.

**Alternative considered:** Place the algorithm in Nuplane. That is the cheapest immediate project count, but would make pure file planning depend on host/package-management implementation details. Putting the full algorithm in Core would stretch Core's thin-utility boundary. Revisit if a real consumer proves the helper has no separate dependency value.

## R2 — Canonical catalog digests

**Decision:** Use the v1 contract's `sha256-jcs-v1` projection, with a strict string/array/object-only semantic schema. Schema and definition versions are strings; no JSON number is owned by a digest projection. Validate duplicate keys and Unicode before projecting, normalize set-valued arrays, sort object names by UTF-16 code unit order, serialize strings per RFC 8785, and hash the UTF-8 bytes. Test fixed expected digests against an independent ECMAScript `JSON.stringify` vector containing ASCII and non-ASCII text. Opaque authored settings/resources are never canonicalized or hashed by this planner.

**Rationale:** [RFC 8785](https://www.rfc-editor.org/rfc/rfc8785.html) requires I-JSON input, recursive UTF-16 property sorting, ECMAScript primitive serialization and UTF-8 output. Restricting the owned projection to strings eliminates numeric formatting ambiguity without weakening the contract's owned fields. The serializer used for ordinary JSON documents is not assumed to produce JCS.

**Alternative considered:** Add a general JCS dependency. A vetted package would broaden the input domain, but v1 owns no numeric digest fields and adding package supply/license surface solely for that generality has no demonstrated value. If the reviewed schema later owns numeric fields, use a conformant full implementation rather than extending the bounded writer casually.

## R3 — Inventory authority and knowledge gaps

**Decision:** Consume an explicit target/time-stamped inventory whose feature rows distinguish a present runtime descriptor with an empty required-edge set from no descriptor. Preserve separate manifest edges/read status, observed package identity/hash or explicit host-bundled marker, and sourced compatibility outcome. Runtime descriptor edges govern loaded features; manifest optionality is lower-confidence evidence only when no runtime descriptor exists. Divergence is a finding.

**Rationale:** `RuntimeFeatureCatalogContributor` knows descriptor presence, while `PackageManifestCatalogMapper` knows manifest optionality. The current `FeatureCatalogItem` drops `DependenciesResolved` when its builder materializes the response, so its empty dependency list cannot supply this distinction to a pure planner. Installed metadata is not remote availability or activation proof.

**Alternative considered:** Treat the management catalog response as authoritative inventory. This would turn missing evidence into an apparently dependency-free feature. A future adapter under #1962 must preserve the needed provenance or report it unavailable.

## R4 — Authored intent, pins, and upgrade

**Decision:** Parse catalog, workspace profile and authored composition as distinct documents. Reject malformed catalog publication, duplicate members/definitions, unsupported schema and mismatched content digests. Keep the authored accepted expansion and locks separate from any candidate result. If a current pin cannot resolve, return a visible unresolved finding without editing the authored document. Re-resolution requires both old and candidate pinned inputs and reports evidenced differences plus unverified settings/resource impact.

**Rationale:** A profile is an immutable starting point, not a live runtime authority. The current shell feature revision tracks enabled features rather than immutable catalog identity. A later save/revision flow owns acceptance and persistence.

**Knowledge limit:** A standalone catalog file can prove its own digest but cannot know whether someone previously published different content under the same ID/version. The planner detects that conflict when comparing old/candidate snapshots or checking an authored pin; Foundation publication review must compare against prior published snapshots. Do not describe standalone self-validation as a historical immutability guarantee.

**Alternative considered:** Implicitly take the latest catalog or auto-fix required dependencies. Either would silently change user intent and undermine the exact-selection model.

## R5 — Effective persistence context

**Decision:** Accept a caller-supplied redacted summary of the reviewed spec 173 effective-persistence result, with status, provenance and resource reference identifiers only. When absent, report provider/connection/schema/migration/layout as unchecked. Never resolve connection values or infer a resource from a feature group.

**Rationale:** Selection and persistence resolution have separate owners. A group-wide persistence edit belongs to an editor that writes explicit feature bindings. This planner must not create a third settings-precedence layer or expose secrets.

## Revisit triggers

- A command/API adapter needs additional inventory facts that the current Nuplane contributor cannot supply: extend its source contract, then add a separately tested adapter; do not alter planner evidence semantics.
- A reviewed catalog owns number-valued fields: evaluate a complete RFC 8785 implementation and new cross-runtime vectors.
- Rebuilt-host closure/API/persistence proof for candidate Embedded, Authoring, Worker: open the separate live-profile story under #1961.
