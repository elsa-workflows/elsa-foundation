# Preparation retention data model — plan R3

**Status**: Proposed typed metadata model; ordinary reference and material ABI unchanged.

## Representation without a native anchor

One WorkflowPreparationRetentionEntity belongs to the existing canonical source-reference backend/RuntimeArtifact family. It is a retention-root document, not a WorkflowExecutableSourceReference and not material. There is no anchor, ArtifactId, DefinitionId, DefinitionVersionId or artifact-version projection.

| Field group | Invariant |
| --- | --- |
| Physical key/scope | Existing canonical scope encoding/hash and ordinal keyset order; explicit global versus tenant scope; no scope inferred from display strings. |
| Preparation ID | Actual Runtime scope, canonical owner address and operation ID; request fingerprint binds immutable intent. |
| Header token | Independent physical metadata IncarnationId + Revision; expected token CAS protects update/replay. |
| AcceptedOwner | Actual canonical operation ledger incarnation, Revision+State, immutable request/snapshot/source/compiler and winning PreparedProfile/PreparedContentFingerprint at acceptance. Historical fact, not a competing current worker claim. |
| Selection | Snapshot/source/prepared-envelope binding; exact selected typed roots and complete typed physical closure; stable fingerprint projects identities/content hashes/incarnations, excluding mutable CAS/lease observations. |
| Native/model pin | Existing complete WorkflowExecutableIdentity, actual native/model kind and material IncarnationId. |
| Template pin | Existing TemplateId/TemplateHash and actual template row/hash-claim incarnation are stable identity. Revision is a historical acceptance observation, excluded from selection equality/fingerprint; fresh native revision is checked separately on every attempt. No native definition fields. |
| State | Retained, Transferred or RejectedReleased; mirrored projection must agree with checked payload. |
| ClosedFrom | Exact accepted header token closed by the terminal CAS, allowing lost-ack replay without accepting another incarnation/selection or impossible revision. Null while Retained. |
| TerminalDecision | Historical VerifiedAt owner stamp, immutable decision/receipt fingerprint and exact published source-reference IDs where applicable; mutable operation revision/state, template CAS revisions and other coordination observations are excluded from decision identity. |
| Payload schema | Checked codec/version and physical projection agreement; malformed data is uncertainty, never false absence. |

No material foreign key/cascade anchors the document. The terminal row remains valid after every native/model/template member disappears. A directly selected template consists of a TemplateRoot plus its actual template/nested-template pins; executable root/pin arrays may be empty. Ordinary source-reference fields are neither fabricated nor made nullable.

## Immutability and lifecycle

1. Roots and closure are frozen owned copies; caller mutation through IReadOnlyList cannot alter an accepted fingerprint.
   Stable selection comparison/hashing uses an explicit projection, not whole-record/JSON equality. Refresh attempt-local native CAS observations under the fence without rewriting accepted pins/fingerprint/token. A legitimate template revision advance with unchanged content/incarnations does not change selection identity; current-attempt CAS races and content/incarnation replacement still refuse.
2. Retain verifies actual source-operation/preparation binding, exact full closure and issuing/provider fences. No complete acceptance is reported for a partial graph.
3. Identical replay returns the persisted confirmation. Changed selection, winning prepared envelope/source binding or owner incarnation conflicts. Current canonical legal revision advances do not change immutable selection identity.
4. Canonical logical Revision may advance through review/reserve/receipt states. A current actual revision/state fence can adopt/complete the same immutable preparation; no AdvanceOwner/claim/takeover operation exists.
5. A stale expected revision/state fails at the actual canonical owner row before Runtime metadata acceptance, even if a delayed command still holds material grants.
6. Only actual committed/rejected canonical decisions may close Retained. Runtime verifies actual publication roots covering the selection and the Agent Design evidence is verified by the actual owner adapter.
7. Closure/state/terminal fact is one Runtime metadata commit. Physical material collection is later guarded GC; partial GC does not reopen the header.
8. Terminal metadata is permanent. Retain after closure conflicts. Exact terminal completion replay matching ClosedFrom/current terminal token returns AlreadyReleased without material/reference grants/existence or another publish; mutable VerifiedAt revision/state are not part of immutable decision equality.
9. Unknown commit/authority remains Unknown until authoritative readback. Querying a terminal record confirms existing facts; it grants no new write authority.
10. Closed-state metadata is never a material root and is never pruned by ordinary reference/material cleanup.

## Queries and paging

Find and keyset pages are methods of the existing source-reference provider. They read the typed metadata table separately from ordinary source-reference rows. Page binding includes actual scope/query/version; limits use existing RuntimeStorePageRequest. Validate payload and projections before filtering Retained versus terminal; corrupt projected state cannot hide a live root.

GC and reference-candidate checks page all checked preparations in scope, selecting Retained closure members in memory. The first implementation uses finite bounded pages, not a new material index or generic store scan inside locked material rows. Initial and final guarded root reads include this source. Creation grants/template revision fences prevent a late new root from racing deletion.

HasPreparationMaterialAsync checks retained AND terminal typed history. It prevents the legacy reference-less escape for preparation material; absence of ordinary references is not absence of preparation authority. Read failure/unsupported query cannot become false. Independent valid publication/test/execution authority is evaluated normally.

## Persistence and migration ownership

Add one metadata table with key/scope/order fields, Revision/IncarnationId, state/schema and typed selection/decision JSON. No member-source rows or material content copy. Ordinary source-reference readers/constructors stay unchanged.

Concrete EF proposal: internal WorkflowPreparationRetentionEntity and its configuration, table elsa_runtime_workflow_preparation_retention, registered with RuntimeDbContext and the consumer-owned-context mapping path. Persist Id (existing length-framed scope/owner/operation hash), ScopeKey/ScopeKeyHash, OwnerAddress, OperationId, an ordinal PreparationOrderKey, Revision, IncarnationId, State, SchemaVersion and ContentJson. Unique key is the checked physical scope + owner address + operation identity; hash collisions require full-value agreement, never silent aliasing. Index scope plus PreparationOrderKey for bounded stable traversal. Include the entity in RuntimeArtifactEfModule/AssemblyInfo's family/content declarations, payload-column handling and finalization readiness. It is metadata, not content-addressed material, and receives no ContentAddressed exemption.

SourcePublicationPreparation.ContentFingerprint hashes its existing Profile+Content envelope. Runtime selection fingerprint hashes the distinct full stable material selection/incarnations and bound source/prepared values, excluding TemplatePin.Revision and every mutable CAS/lease observation. Both are stored separately and verified under Join. Roots come from the actual adapter's winning persisted projection; Runtime never substitutes a source-kind policy or a caller's selected-material permission.

RuntimeArtifact receives additive entity/mapping/schema-chain/readiness and all four provider migration/designer/snapshot changes. Publishing owner separately adds a TRUE immutable physical operation IncarnationId on insertion and FenceRevision to its canonical operation ledger representation; expiring review-entity incarnation, deterministic key/fingerprint or revision zero cannot stand in for it. Delete/recreate changes incarnation and every stale operation CAS refuses it. Logical operation.Revision continues to equal serialized operation.Revision. Every competing operation transition checks/increments the coordination field; fence staging must not increment logical Revision alone.

Allocate exact versions from the actual integrated owner chains; no numeric version/migration is reserved here. Preserve old rows using reviewed upcasters/atomic initialization under canonical provider CAS, never reset live stores. Both families must be ready before positive admission.

Runtime schema ownership is RuntimeArtifactEfModule.cs, RuntimeDbContext.cs, Configuration/RuntimeArtifactEntityConfigurations.cs (or its dedicated preparation configuration), AssemblyInfo.cs, existing RuntimeArtifactJson codec boundary, and Migrations/Runtime/{Sqlite,PostgreSql,MySql,SqlServer} migrations/designers/snapshots. Add the family chain/upcaster/readiness changes in that same module. Owner coordination is SourcePublicationLedgerEntity/SourcePublicationLedgerModel, PublishingLedgerEfModule/AssemblyInfo and its actual provider chains, delegated to the canonical Publishing owner. Existing native/model/template content and authored/prepared fingerprints are never migration backfill targets.

## Newer-source preservation

Reconcile #3912's protected PublicationScope/model/comparer/scoped publication writes before implementation. Preparation does not stamp that protected assertion. Preserve #3904 dedicated interest-fence/helper changes and actual transaction/schema/incarnation/revision checks; no preparation-derived interest eligibility.
