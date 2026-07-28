# Extension points — Elsa 3 Activity Design Import

The one-way Elsa 3 compatibility boundary. Reusable workflows are analyzed as a collection and
applied only as a reviewed dependency-closed mutation; Runtime never consumes these contracts.

---

## Implementable contributor interfaces

### `IReusableActivityImportMaterializer`

- **Kind:** Design mapping strategy.
- **Purpose:** converts a reviewed collection plan into Activity/Workflow Design mutations.
- **Default implementation:** `Elsa3ReusableActivityImportMaterializer` from `Elsa3.Mapping`.
- **Invariant:** exact planned reference rewrites only; recursive composition is never replaced by separate-workflow execution.

### `IReusableActivityImportCommand`

- **Kind:** atomic persistence command.
- **Purpose:** commits one selected dependency closure across Activity Design and Workflow Design.
- **Default implementation:** `GroundworkReusableActivityImportCommand`, registered by `Elsa3ImportActivitiesGroundworkFeature`.
- **Invariant:** all candidate documents and the durable receipt are preflighted before one cross-kind commit; identical reapply is an `AlreadyImported` no-op.
- **Ownership boundary:** imported Activity/Workflow Definitions, immutable versions, and their
  provenance bindings are tenant-owned Design resources. User identity never participates in a
  provenance binding, so another user in the same tenant reuses exact imported resources.
- **Composition:** the generic `Elsa3ImportActivitiesFeature` depends only on mapping and contracts.
  A host selects `Elsa3ImportActivitiesGroundworkFeature` (or another provider feature) explicitly.

### `IReusableActivityImportOperationStore`

- **Kind:** scoped durable operation store.
- **Purpose:** stores immutable expiring collection handles and reads completed apply receipts.
- **Default implementation:** `GroundworkReusableActivityImportOperationStore`.
- **Invariant:** collection and receipt writes are append-only, and reads are bound to the exact
  ambient tenant plus user scope; authorization mismatches are indistinguishable from absence.
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
