# Extension points — Elsa 3 Activity Design Import

The one-way Elsa 3 compatibility boundary. Reusable workflows are analyzed as a collection and
applied only as a reviewed dependency-closed mutation; Runtime never consumes these contracts.

---

## Implementable contributor interfaces

### `IReusableActivityImportMaterializer`

- **Kind:** Design mapping strategy.
- **Purpose:** converts a reviewed collection plan into Activity/Workflow Design mutations.
- **Default implementation:** `Elsa3ReusableActivityImportMaterializer` from `Elsa3.Mapping`.
- **Invariant:** exact planned reference rewrites only; recursive composition is never replaced by separate-workflow execution. A replacement must also build each reusable activity version's `DescriptorPayload` from the `Body` it returns beside it, because the importer judges the credential-literal rule on `Body` (see Mapped bodies).
- **Mapped bodies:** each `ImportedReusableActivity` carries its mapped workflow state as `Body`, so the importer can admit it before the commit. The importer judges `Body`, not the version's `DescriptorPayload` (the graph manifest format belongs to the graph activity module, which the import does not reference), so an implementation must build the descriptor payload from that `Body` and put no activity input into it that the body does not hold; a payload built otherwise is stored unjudged. The default implementation builds the payload's `rootActivity` from the body's root in the same call (`ReusableActivityCollectionCredentialLiteralTests` pins it).

### `IReusableActivityImportCommand`

- **Kind:** atomic persistence command.
- **Purpose:** commits one selected dependency closure across Activity Design and Workflow Design.
- **Default implementation:** `EfReusableActivityImportCommand`, registered by `Elsa3ImportActivitiesEntityFrameworkCoreFeature`.
- **Opt-in EF Core implementation:** `EfReusableActivityImportCommand`, registered by `Elsa3ImportActivitiesEntityFrameworkCoreFeature`.
  It enlists the import ledger, Activities Design, and Workflows Design contexts in one `EfSharedTransaction`
  and writes through those lanes' own commands and atomic writers, so all three must name the same database;
  a split target is refused before any write.
- **Invariant:** all candidate documents and the durable receipt are preflighted before one cross-kind commit; identical reapply is an `AlreadyImported` no-op.
- **Ownership boundary:** imported Activity/Workflow Definitions, immutable versions, and their
  provenance bindings are tenant-owned Design resources. User identity never participates in a
  provenance binding, so another user in the same tenant reuses exact imported resources.
- **Composition:** the generic `Elsa3ImportActivitiesFeature` depends only on mapping and contracts.
  A host selects `Elsa3ImportActivitiesEntityFrameworkCoreFeature` or `Elsa3ImportActivitiesEntityFrameworkCoreFeature`
  explicitly. Both register through `Elsa3ImportPersistenceBackend`: a repeat is idempotent, either order
  switches cleanly, and a custom store or command is refused rather than silently replaced.

### `IReusableActivityImportOperationStore`

- **Kind:** scoped durable operation store.
- **Purpose:** stores immutable expiring collection handles, deletes them, and reads completed apply receipts.
- **Default implementation:** `EfReusableActivityImportOperationStore`; opt-in EF Core
  implementation `EfReusableActivityImportOperationStore`.
- **Invariant:** a stored row is never rewritten: receipts are append-only and a collection upload is deleted,
  never updated. Reads are bound to the exact ambient tenant plus user scope; authorization mismatches are
  indistinguishable from absence.
- **Deletes:** a collection upload is deleted rather than kept.
  `DeleteCollectionAsync` removes one upload in its exact tenant-plus-user scope; `DeleteExpiredCollectionsAsync`
  removes a bounded batch of the ambient persistence scope's uploads, every user's, whose stored expiry has passed,
  oldest first. Both remove rows and decide nothing: when an upload is deleted is the operation service's rule (see
  "Upload retention" below). An implementation must delete the content, not mark the row.
- **Idempotency boundary:** the key namespace is the exact tenant-plus-user operation scope. The same
  textual key is independent for two users in one tenant, while each user can reconcile only their own receipt.

### `IReusableActivityCollectionAnalyzer`

- **Kind:** replaceable pure analysis strategy.
- **Default implementation:** `ReusableActivityCollectionAnalyzer`.
- **Output:** deterministic identities, exact rewrites, direct-start wrapper facts, missing/unsupported diagnostics, and complete cycle paths.

## Authorized HTTP contract

`Elsa3ImportActivitiesFeature` exposes these permission-guarded routes:

- `POST migration/elsa3/reusable-activities/collections` — bounded authored-definition array upload.
- `GET .../collections/{collectionHandle}/analysis` — deterministic, side-effect-free, offset-paged analysis.
- `POST .../collections/{collectionHandle}/selection` — authoritative dependency-closure expansion and readiness.
- `POST .../collections/{collectionHandle}/apply` — exact Plan ID, selection, tenant-plus-user
  operation scope, and user-scoped idempotency binding; resulting Design resources remain tenant-owned.
- `GET .../imports/{idempotencyKey}` — durable lost-response reconciliation.

Uploads default to 16 MiB, 20,000 source versions, a 24-hour lifetime, and analysis pages of at
most 500 rows. Hosts may lower or raise these finite bounds through `ReusableActivityImportOptions`.
Apply never falls back to `ExecuteWorkflow`; recursive reusable composition remains a blocking
diagnostic with a complete typed cycle.

**Upload retention (#2330).** An upload is stored verbatim, every property of every activity included, so it can hold
literal credentials (an `Authorization` header value on an Elsa 3 HTTP request activity, for example). It is
review-session state: it lives until its apply is decided or its lifetime runs out, whichever comes first, and is then
deleted from the import ledger. `ReusableActivityImportOperationService` owns that rule:

- **A completed apply consumes the upload.** The collection row is deleted once the commit returns its receipt. The
  receipt is self-contained, so a replay of the same idempotency key and `GET .../imports/{idempotencyKey}` still
  answer; the replay repeats the delete, which covers an apply that committed and stopped before its delete ran. After
  an apply, analysis, selection and a second apply against that handle answer 404: importing a further subset of the
  same export needs a new upload.
- **A refused apply deletes the upload, unless its caller can continue with the same upload.** The outcomes that keep
  it are a stale plan or an invalid or non-closed selection (422), an idempotency conflict raised inside the commit
  (409: a concurrent request won the same key with other content, and if it applied another upload this one is still
  usable under a new key), an identity collision (409), a persistence failure (which includes a
  commit whose outcome is unknown, where the repeat needs the collection again), a schema write refusal and a
  cancellation. Every other outcome refuses the upload's content and deletes it; unknown outcomes fall
  on the deleting side on purpose. A malformed request (a blank plan ID or idempotency key) is refused before the
  upload is read and leaves it alone.
- **An expired upload is deleted, not only refused.** The read that finds an upload past its expiry deletes it and
  answers 410; later reads of that handle answer 404.
- **A recurring sweep deletes the expired uploads nobody reads again.** `ExpiredImportCollectionSweepTask` is an
  `IRecurringTask`, which is why this feature depends on `Tasks`. Every
  `ReusableActivityImportOptions.ExpiredCollectionSweepInterval` (15 minutes by default) it visits each persistence
  scope the host supplies (`IPersistenceScopeRunner`) and deletes at most `ExpiredCollectionSweepBatchSize` (100)
  expired uploads in each. Every node runs it; the delete is idempotent. It leaves a row at a schema version this
  build does not read (ADR 0077): such a row was written by a newer build and is left to a build that reads it. It does not
  visit the global partition, which no host-supplied scope names: an upload stored under a global persistence
  context is deleted by its apply or by the read that finds it expired.

The outcome is decided when these deletes run, so they do not observe the caller's cancellation: a client that
disconnects after a refusal does not leave the refused upload behind. A delete that fails leaves the outcome the caller
asked for in place (the receipt, the refusal, the 410) and is logged. The row then stays, and until its expiry it is
still readable and can still be applied, so a refused upload whose delete failed keeps its content at rest until
something deletes it: after a completed apply, a replay of the idempotency key repeats the delete; any later read of
the handle that finds it expired deletes it; and in a tenant partition, the sweep deletes it once its lifetime runs
out. So the one row nothing retries is a refused or expired upload in the global partition whose handle is never used
again. This paragraph is the one statement of that behavior; the code's documentation points here.

Between its upload and its apply or expiry the document is at rest in the ledger, as a reviewed import needs it to be.
The rule bounds that time; it does not encrypt the column, and it does not reach database backups.

**Credential literals (spec 188, FR-008).** Before the commit, apply judges every activity node the commit would store
through the credential-literal rule, `ICredentialLiteralValidator`, registered by `WorkflowDesignValidations`, on which
`Elsa3ImportJsonActivities` depends: the nodes of each imported workflow version's state and of each reusable
activity's `Body` (from which the materializer builds that activity version's descriptor payload), the root and every
node nested under it, up to `Elsa3ImportedActivityStructure.MaxNestingDepth` (14) containers below the root. The mapping
refuses deeper nesting with 400 naming the limit: the structure payload, the stored workflow state and a reusable
activity's descriptor payload keep the default JSON nesting limit of 64. A binding value that is itself deeply nested
can still exceed that limit within 14 containers, and the apply then fails with a 500 before anything is committed. The mapping nests child activities under `elsa3.imported-activity.structure`, which
no structure handler projects, so the rule's own tree walk sees only each root. `Elsa3ImportedActivityStructure`
(`Models`) is the one definition of that shape: the mapping writes it through `Create`, and the importer reads every
nested node back through `Nodes` and judges each as the root of its own state. The apply is all or nothing, so a
literal, object, value read, expression or malformed secret reference on an input the installed activity declares a
credential refuses the whole apply and no workflow or activity is committed: one `CredentialLiteralRefusedException`,
answered 400 (`elsa3.import.request-invalid`) with the messages of every refused binding in `detail`. Each message names
the rule, the node, the input and the Elsa 3 workflow it was found in (`CredentialLiteralFinding.Located`), never the
value. The mapping stores each input binding under the declared input's reference key, whatever casing the Elsa 3
property name has, which is the key the rule matches; two properties that bind one input, or a property that matches
several declared inputs only ignoring case, refuse the apply with 400. A property that names no declared input or
output is not mapped. As at every entry point, a node whose activity the catalog does not hold is not judged;
publication refuses a version holding one. The rule does not apply to the upload itself; a refused apply deletes it,
as Upload retention above states, and until the apply is decided it rests in the ledger (spec 188 credential-literal
contract, Known gaps).

### `IActivityCollectionJsonSource` *(Feature contract — `Elsa3.Activities.Design.Import`)*
- **Kind:** Source (opens a stream of activity JSON — pull pattern).
- **Contract defined in:** `Elsa3.Activities.Design.Import` (this feature project). Implementing it requires a reference to this feature.
- **Signature:** `Task<Stream> OpenStream(CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IActivityCollectionJsonSource, MySource>()`.
- **Consumed by:** the import startup task (this feature), which reads each source's JSON stream and feeds the parsed activity definitions into the Activities.Design reconciliation pipeline.
- **Purpose:** plug in a new source of Elsa3-format activity JSON (e.g. embedded resource, remote URL, filesystem path).

**Known implementations (shipped):**
- None currently in-repo. Implement to point the importer at your Elsa3 activity definition file.

---

## Cross-references

- Activity catalog reconciliation that consumes the imported data: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../../../../Elsa/Activities/Design/Reconciliation/EXTENSION_POINTS.md).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
